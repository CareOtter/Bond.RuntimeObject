namespace Bond.RuntimeObject.Expressions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using Bond;
    using Bond.Expressions;

    internal class RuntimeDeserializerTransform<R>
    {
        readonly RuntimeFactory newBonded = NewBonded;
        readonly RuntimeFactory newContainer = NewContainer;
        readonly bool inlineNested;

        readonly Expression<Func<R, int, object>> deferredDeserialize;
        readonly List<Expression<Func<R, object>>> deserializeFuncs = new List<Expression<Func<R, object>>>();
        readonly Dictionary<TypeDef, int> deserializeIndex = new Dictionary<TypeDef, int>();
        static readonly MethodInfo arrayResize =
            ReflectionCopy.GenericMethodInfoOf((object[] o) => Array.Resize(ref o, default(int)));
        static readonly ConstructorInfo arraySegmentCtor =
            typeof(ArraySegment<byte>).GetConstructor(typeof(byte[]), typeof(int), typeof(int));

        public RuntimeDeserializerTransform(
            Expression<Func<R, int, object>> deferredDeserialize,
            RuntimeFactory factory,
            bool inlineNested = true)
        {
            this.deferredDeserialize = deferredDeserialize;
            this.inlineNested = inlineNested;

            if (factory != null)
            {
                newContainer = (t1, t2, a) =>
                    factory(t1, t2, a) ?? NewContainer(t1, t2, a);
                newBonded = (t1, t2, a) =>
                    factory(t1, t2, a) ?? NewBonded(t1, t2, a);
            }
        }

        public RuntimeDeserializerTransform(
            Expression<Func<R, int, object>> deferredDeserialize,
            bool inlineNested = true,
            Expression<Func<RuntimeSchema, TypeDef, int, object>> createContainer = null)
        {
            this.deferredDeserialize = deferredDeserialize;
            this.inlineNested = inlineNested;

            if (createContainer != null)
            {
                newContainer = (t1, t2, a) =>
                    Expression.Convert(
                        Expression.Invoke(
                            createContainer,
                            Expression.Constant(t1),
                            Expression.Constant(t2),
                            a[0]),
                        GetSchemaType(t1, t2));
            }
        }

        public IEnumerable<Expression<Func<R, object>>> Generate(IParser parser, RuntimeSchema schema)
        {
            return Generate(parser, schema, schema.SchemaDef.root);

        }
        public IEnumerable<Expression<Func<R, object>>> Generate(IParser parser, RuntimeSchema schema, TypeDef typeDef)
        {
            AuditCopy.ArgNotNull(schema, "schema");
            AuditCopy.ArgNotNull(typeDef, "typeDef");

            var type = GetSchemaType(schema, typeDef);
            
            Deserialize(parser, null, schema, typeDef);
            return deserializeFuncs;
        }

        Expression Deserialize(IParser parser, Expression var, RuntimeSchema schema, TypeDef typeDef)
        {
            var objectType = GetSchemaType(schema, typeDef);

            Expression body;

            int index;
            if (!deserializeIndex.TryGetValue(typeDef, out index))
            {
                index = deserializeFuncs.Count;
                deserializeIndex[typeDef] = index;
                deserializeFuncs.Add(null);
                var result = Expression.Variable(objectType, objectType.Name);
                deserializeFuncs[index] = Expression.Lambda<Func<R, object>>(
                    Expression.Block(
                        new[] { result },
                        Struct(parser, result, schema, typeDef),
                        Expression.Convert(result, typeof(object))),
                    parser.ReaderParam);
            }

            if (var == null)
                body = null;
            else
                body = Expression.Assign(var,
                    Expression.Convert(
                        Expression.Invoke(
                            deferredDeserialize,
                            parser.ReaderValue,
                            Expression.Constant(index)),
                        objectType));

            return body;
        }

        Expression Struct(IParser parser, Expression var, RuntimeSchema schema, TypeDef typeDef)
        {
            var body = new List<Expression>();
            
            body.Add(Expression.Assign(var, Expression.New(typeof(RuntimeObject))));

            ITransform transform;

            var structDef = schema.SchemaDef.structs[typeDef.struct_def];

            if (parser.HierarchyDepth > GetHierarchyDepth(schema, structDef))
            {
                // Parser inheritance hierarchy is deeper than the type we are deserializing.
                // Recurse until hierarchies align.

                transform = new Transform(
                    Base: baseParser => Struct(baseParser, var, schema, structDef.base_def));
            }
            else
            {
                var baseTypeDef = structDef.base_def;

                transform = new Transform(
                    Fields:
                        from field in structDef.fields
                        select new Field(
                            Id: field.id,
                            Value: (fieldParser, fieldType) => FieldValue(
                                fieldParser,
                                RuntimeDataExpression.RuntimePropertySetter(var, field.metadata.name),
                                fieldType,
                                schema,
                                field.type,
                                field: field),
                            Omitted: () => field.metadata.modifier == Modifier.Required ?
                                ThrowExpressionCopy.RequiredFieldMissingException(
                                    structDef.metadata.name, Expression.Constant(field.metadata.name)) : 
                                Expression.Empty()),
                    Base: baseParser => baseTypeDef != null
                        ? Struct(baseParser, var, schema, baseTypeDef)
                        : Expression.Empty());
            }

            body.Add(parser.Apply(transform));
            return Expression.Block(body);
        }

        private int GetHierarchyDepth(RuntimeSchema schema, StructDef structDef)
        {
            int depth = 0;
            
            while (structDef != null && structDef.base_def != null)
            {
                depth += 1;

                structDef = schema.SchemaDef.structs[structDef.base_def.struct_def];
            }

            return depth;
        }

        Expression Nullable(IParser parser, Expression var, RuntimeSchema schema, TypeDef typeDef)
        {
            return parser.Container(typeDef.element.id,
                (valueParser, valueType, next, count) =>
                {
                    var body = new List<Expression>();

                    body.Add(Expression.Assign(var, Expression.Default(var.Type)));

                    body.Add(ControlExpressionCopy.While(next,
                        Value(valueParser, var, valueType, schema, typeDef.element, field: null)));

                    return Expression.Block(body);
                });
        }

        Expression Container(IParser parser, Expression container, RuntimeSchema schema, TypeDef typeDef)
        {
            var itemTypeDef = typeDef.element;

            return parser.Container(itemTypeDef.id,
                (valueParser, elementType, next, count) =>
                {
                    Expression addItem;
                    ParameterExpression[] parameters;
                    Expression beforeLoop = Expression.Empty();
                    Expression afterLoop = Expression.Empty();

                    if (typeDef.list_sub_type == ListSubType.BLOB_SUBTYPE
                        || ((typeDef.id == BondDataType.BT_LIST || typeDef.id == BondDataType.BT_SET)
                            && itemTypeDef.id == BondDataType.BT_UINT8))
                    {
                        var blob = parser.Blob(count);
                        if (blob != null)
                            return blob;

                        // Parser doesn't provide optimized read for blob so we will have to read byte-by-byte.
                        var index = Expression.Variable(typeof(int), "index");
                        var array = Expression.Variable(typeof(byte[]), "array");

                        beforeLoop = Expression.Block(
                            Expression.Assign(index, Expression.Constant(0)),
                            Expression.Assign(array, Expression.NewArrayBounds(typeof(byte), count)));

                        // If parser didn't provide real item count we may need to resize the array
                        var newSize = Expression.Condition(
                            Expression.GreaterThan(index, Expression.Constant(512)),
                            Expression.Multiply(index, Expression.Constant(2)),
                            Expression.Constant(1024));

                        addItem = Expression.Block(
                            Expression.IfThen(
                                Expression.GreaterThanOrEqual(index, Expression.ArrayLength(array)),
                                Expression.Call(null, arrayResize.MakeGenericMethod(typeof(byte)), array, newSize)),
                                valueParser.Scalar(elementType, BondDataType.BT_INT8, value => Expression.Assign(
                                    Expression.ArrayAccess(array, Expression.PostIncrementAssign(index)),
                                    Expression.Convert(value, typeof(byte)))));

                        parameters = new[] { index, array };
                    }
                    else
                    {
                        var itemType = GetSchemaType(schema, itemTypeDef);
                        Type containerType;
                        if (itemTypeDef.bonded_type)
                        {
                            itemType = typeof(R) == typeof(object)
                                ? typeof(RuntimeBonded<>).MakeGenericType(itemType)
                                : typeof(RuntimeBonded<,>).MakeGenericType(itemType, typeof(R));
                            containerType = typeof(List<>).MakeGenericType(itemType);
                        }
                        else
                        {
                            containerType = GetSchemaType(schema, typeDef);
                        }

                        var item = Expression.Variable(itemType, container + "_item");

                        beforeLoop = Expression.Assign(container, newContainer(schema, typeDef, count));

                        var add = containerType.GetMethod(typeof(ICollection<>), "Add", itemType);

                        addItem = Expression.Block(
                            Value(valueParser, item, elementType, schema, itemTypeDef, field: null),
                            Expression.Call(Expression.Convert(container, containerType), add, item));

                        parameters = new[] { item };
                    }

                    return Expression.Block(
                        parameters,
                        beforeLoop,
                        ControlExpressionCopy.While(next, addItem),
                        afterLoop);
                });
        }

        Expression Map(IParser parser, Expression map, RuntimeSchema schema, TypeDef typeDef)
        {
            return parser.Map(typeDef.key.id, typeDef.element.id,
                (keyParser, valueParser, keyType, valueType, nextKey, nextValue, count) =>
                {
                    var keyType2 = GetSchemaType(schema, typeDef.key);
                    var valueType2 = GetSchemaType(schema, typeDef.element, true);

                    var key = Expression.Variable(keyType2, map + "_key");
                    var value = Expression.Variable(valueType2, map + "_value");

                    // TODO: should we use non-default Comparer
                    var init = Expression.Assign(map, newContainer(schema, typeDef, count));

                    var mapType = GetSchemaType(schema, typeDef);
                    var add = mapType.GetDeclaredProperty(typeof(IDictionary<,>), "Item", value.Type);

                    Expression addItem = Expression.Block(
                        Value(keyParser, key, keyType, schema, typeDef.key, field: null),
                        nextValue,
                        Value(valueParser, value, valueType, schema, typeDef.element, field: null),
                        Expression.Assign(
                            Expression.Property(
                                Expression.Convert(map, mapType),
                                add,
                                new Expression[] { key }), value));

                    return Expression.Block(
                        new[] { key, value },
                        init,
                        ControlExpressionCopy.While(nextKey,
                            addItem));
                });
        }

        Expression FieldValue(IParser parser, Expression var, Expression valueType, RuntimeSchema schema, TypeDef typeDef,
            FieldDef field)
        {
            var body = Value(parser, var, valueType, schema, typeDef, field);

            if (typeDef.id == BondDataType.BT_LIST
                || typeDef.id == BondDataType.BT_SET
                || typeDef.id == BondDataType.BT_STRUCT)
            {
                var expectedType = Expression.Constant(typeDef.id);
                return PrunedExpressionCopy.IfThenElse(
                    Expression.Equal(valueType, expectedType),
                    body,
                    ThrowExpressionCopy.InvalidTypeException(expectedType, valueType));
            }

            return body;
        }

        Expression Value(IParser parser, Expression var, Expression valueType, RuntimeSchema schema, TypeDef typeDef,
            FieldDef field)
        {
            var schemaType = GetSchemaType(schema, typeDef);

            if (typeDef.bonded_type)
            {
                // RuntimeObjectParser.Bonded returns RuntimeBonded<T>, which can just be returned directly.
                if (parser is RuntimeObjectParser)
                {
                    // Upcast Lists and Dictionaries to the concrete type
                    return parser.Bonded(value => Expression.Assign(var, Expression.Convert(value, var.Type)));
                }

                // Normal implementations of the Bonded method return Bonded<T>, which can not be used here.
                // Instead, a new RuntimeBonded<T,R> needs to be explicitly created to read from the parser.
                var runtimeBondedType = typeof(RuntimeBonded<,>)
                    .MakeGenericType(schemaType, parser.ReaderParam.Type);
                var runtimeBondedConstructor = runtimeBondedType.GetConstructor(
                    parser.ReaderParam.Type, typeof(RuntimeSchema), typeof(TypeDef));

                return Expression.Block(
                    Expression.Assign(var,
                        Expression.New(runtimeBondedConstructor, parser.ReaderParam, Expression.Constant(schema), Expression.Constant(typeDef))),
                    parser.Skip(Expression.Constant(typeDef.id)));
            }

            if (typeDef.id == BondDataType.BT_STRUCT)
            {
                if (parser.IsBonded)
                {
                    var runtimeBondedType = typeof(RuntimeBonded<,>)
                        .MakeGenericType(schemaType, parser.ReaderParam.Type);
                    var runtimeBondedConstructor = runtimeBondedType.GetConstructor(
                        parser.ReaderParam.Type, typeof(RuntimeSchema), typeof(TypeDef));

                    return Expression.Block(
                        Expression.Assign(var,
                            Expression.New(runtimeBondedConstructor, parser.ReaderParam, Expression.Constant(schema),
                                Expression.Constant(typeDef))),
                        parser.Skip(Expression.Constant(typeDef.id)));
                }

                return Deserialize(parser, var, schema, typeDef);
            }

            if (typeDef.id == BondDataType.BT_MAP)
                return Map(parser, var, schema, typeDef);

            if (typeDef.id == BondDataType.BT_LIST || typeDef.id == BondDataType.BT_SET)
            {
                // If no FieldDef is provided, assume the value is a vector<T> (not a nullable<T>) to ensure
                // deserialization can always be completed. A nullable<T> value can always be deserialized as a
                // vector<T>, but a vector<T> can not be deserialized as a nullable<T> if it contains more than one
                // value.
                if (field == null || field.type.list_sub_type != ListSubType.NULLABLE_SUBTYPE)
                {
                    return Container(parser, var, schema, typeDef);
                }

                return Nullable(parser, var, schema, typeDef);
            }

            return parser.Scalar(valueType, typeDef.id,
                value => Expression.Assign(var,
                    Expression.Convert(PrunedExpressionCopy.Convert(value, schemaType), var.Type)));
        }

        static Expression NewContainer(RuntimeSchema schema, TypeDef typeDef, params Expression[] arguments)
        {
            var schemaType = GetSchemaType(schema, typeDef, false);
            var ctor = schemaType.GetConstructor(arguments.Select(a => a.Type).ToArray());
            return ctor != null ? Expression.New(ctor, arguments) : Expression.New(schemaType);
        }

        static Expression NewBonded(RuntimeSchema schema, TypeDef typeDef, params Expression[] arguments)
        {
            var schemaType = GetSchemaType(schema, typeDef, false);
            var bondedSchemaType = typeof(RuntimeBonded<>).MakeGenericType(schemaType);
            var ctor = bondedSchemaType.GetConstructor(arguments.Select(a => a.Type).ToArray());
            return ctor != null ? Expression.New(ctor, arguments) : Expression.New(schemaType);
        }

        internal static Type GetSchemaType(RuntimeSchema schema, TypeDef typeDef, bool shouldWrapBondedTypes = false)
        {
            Type schemaType;
            switch (typeDef.id)
            {
                case BondDataType.BT_STRUCT:
                    schemaType = typeof(RuntimeObject);
                    break;
                case BondDataType.BT_LIST:
                case BondDataType.BT_SET:
                    if (typeDef.element.id == BondDataType.BT_UINT8)
                    {
                        return typeof(ArraySegment<byte>);
                    }
                    schemaType = typeof(List<>).MakeGenericType(GetSchemaType(schema, typeDef.element, true));
                    break;
                case BondDataType.BT_MAP:
                    schemaType = typeof(Dictionary<,>).MakeGenericType(
                        GetSchemaType(schema, typeDef.key), GetSchemaType(schema, typeDef.element, true));
                    break;
                case BondDataType.BT_STRING:
                case BondDataType.BT_WSTRING:
                    schemaType = typeof(string);
                    break;
                case BondDataType.BT_BOOL:
                    schemaType = typeof(bool);
                    break;
                case BondDataType.BT_FLOAT:
                    schemaType = typeof(float);
                    break;
                case BondDataType.BT_DOUBLE:
                    schemaType = typeof(double);
                    break;
                case BondDataType.BT_INT8:
                    schemaType = typeof(sbyte);
                    break;
                case BondDataType.BT_INT16:
                    schemaType = typeof(short);
                    break;
                case BondDataType.BT_INT32:
                    schemaType = typeof(int);
                    break;
                case BondDataType.BT_INT64:
                    schemaType = typeof(long);
                    break;
                case BondDataType.BT_UINT8:
                    schemaType = typeof(byte);
                    break;
                case BondDataType.BT_UINT16:
                    schemaType = typeof(ushort);
                    break;
                case BondDataType.BT_UINT32:
                    schemaType = typeof(uint);
                    break;
                case BondDataType.BT_UINT64:
                    schemaType = typeof(ulong);
                    break;
                case BondDataType.BT_UNAVAILABLE:
                default:
                    schemaType = typeof(object);
                    break;
            }

            if (shouldWrapBondedTypes && typeDef.bonded_type)
            {
                // TODO: Try to find a better way to determine whether the RuntimeBonded is for an existing object
                // or for a reader reading from a byte array.
                schemaType = typeof(R) == typeof(object)
                    ? typeof(RuntimeBonded<>).MakeGenericType(schemaType)
                    : typeof(RuntimeBonded<,>).MakeGenericType(schemaType, typeof(R));
            }

            return schemaType;
        }
    }
}
