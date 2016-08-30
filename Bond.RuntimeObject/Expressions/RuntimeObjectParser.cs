namespace Bond.RuntimeObject.Expressions
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using Bond.Expressions;

    /// <summary>
    /// Creates expression of type <see cref="IRuntimeBonded{T}"/> given a object type and value.
    /// </summary>
    /// <param name="objectType">Type of object to be stored in <see cref="IBonded"/></param>
    /// <param name="value">Expression representing the value to be stored in the bonded instance.</param>
    /// <returns>Expression representing creation of bonded with the specified value.</returns>
    public delegate Expression RuntimeObjectBondedFactory(Type objectType, Expression value);

    public class RuntimeObjectParser : IParser
    {
        static readonly MethodInfo moveNext = ReflectionCopy.MethodInfoOf((IEnumerator e) => e.MoveNext());
        static readonly ConstructorInfo arraySegmentCtor = typeof(ArraySegment<byte>).GetConstructor(typeof(byte[]));
        delegate Expression ContainerItemHandler(Expression value, Expression next, Expression count);
        readonly ParameterExpression objParam;
        readonly Expression value;
        readonly Type objectType;
        readonly RuntimeSchema schema;
        readonly TypeDef typeDef;
        readonly StructDef structDef;
        readonly int hierarchyDepth;
        readonly RuntimeObjectBondedFactory bondedFactory;

        public RuntimeObjectParser(RuntimeSchema schema, TypeDef typeDef)
            : this(schema, typeDef, null)
        {}

        public RuntimeObjectParser(RuntimeSchema schema, TypeDef typeDef, RuntimeObjectBondedFactory bondedFactory)
        {
            value = objParam = Expression.Parameter(typeof(object), "obj");
            objectType = GetSchemaType(schema, typeDef);
            this.schema = schema;
            this.typeDef = typeDef;
            structDef = schema.SchemaDef.structs[typeDef.struct_def];
            hierarchyDepth = GetHierarchyDepth(schema, structDef);
            this.bondedFactory = bondedFactory ?? NewBonded;
        }

        RuntimeObjectParser(RuntimeObjectParser that, Expression value, TypeDef typeDef)
        {
            objParam = that.objParam;
            bondedFactory = that.bondedFactory;
            this.value = value;
            objectType = GetSchemaType(schema, typeDef);
            schema = that.schema;
            this.typeDef = typeDef;
            structDef = that.schema.SchemaDef.structs[typeDef.struct_def];
            hierarchyDepth = GetHierarchyDepth(schema, structDef);
        }

        public ParameterExpression ReaderParam { get { return objParam; } }
        public Expression ReaderValue { get { return value; } }
        public int HierarchyDepth { get { return hierarchyDepth; } }
        public bool IsBonded { get { return typeDef.bonded_type; } }

        public Expression Apply(ITransform transform)
        {
            var structVar = Expression.Variable(objectType, objectType.Name);
            var body = new List<Expression>
            {
                Expression.Assign(structVar, Expression.Convert(objParam, objectType)),
                transform.Begin
            };

            var baseType = structDef.base_def;
            if (baseType != null)
            {
                body.Add(transform.Base(new RuntimeObjectParser(this, structVar, baseType)));
            }

            // Performs left outer join of object fields with transform fields.
            // The result contains entry for each schema field. For fields not handled 
            // by the transform default to Skip.
            body.AddRange(
                from objectField in structDef.fields
                join transformField in transform.Fields on objectField.id equals transformField.Id into fields
                from knownField in fields.DefaultIfEmpty()
                select Field(transform, structVar, objectField.id, objectField, knownField));

            body.Add(transform.End);
            
            return Expression.Block(
                new [] { structVar },
                body);
        }

        Expression Field(ITransform transform, Expression structVar, UInt16 id, FieldDef schemaField, IField field)
        {
            var fieldSchemaType = GetSchemaType(schema, schemaField.type, shouldWrapBondedTypes: false, shouldWrapNullableTypes: true);
            var fieldSchemaTypeWrapped = GetSchemaType(schema, schemaField.type, shouldWrapBondedTypes: true, shouldWrapNullableTypes: true);
            var fieldId = Expression.Constant(id);
            var fieldType = Expression.Constant(schemaField.type.id);

            object defaultValue = GetDefaultValue(schema, schemaField);

            var propertiesProperty = Expression.Property(structVar, "Properties");
            var containsKey = Expression.Call(propertiesProperty, "ContainsKey", new Type[] { },
                Expression.Constant(schemaField.metadata.name));

            Expression property = null;
            TypeDef elementTypeDef = schemaField.type;
            // These two expressions need to be tweaked in the case of BTLIST with subtype of NULLABLE_LIST
            if (schemaField.type.id == BondDataType.BT_LIST && schemaField.type.list_sub_type == ListSubType.NULLABLE_SUBTYPE)
            {
                //Create a new list<T> with the value from the runtimeobject
                var elementType = GetSchemaType(schema, schemaField.type.element, shouldWrapBondedTypes: true);
                System.Reflection.MethodInfo addMethod = fieldSchemaType.GetMethod(typeof(ICollection<>), "Add", elementType);
                var constructorInfo = fieldSchemaType.GetConstructor(typeof(int));
                property = Expression.ListInit(Expression.New(constructorInfo, Expression.Constant(1)),
                    Expression.ElementInit(addMethod, RuntimeDataExpression.RuntimePropertyGetter(structVar, schemaField.metadata.name, elementType)));
            }
            else if (fieldSchemaTypeWrapped.ContainsRuntimeBonded())
            {
                property = Expression.Property(
                        Expression.Property(
                            Expression.Convert(structVar, typeof(RuntimeObject)),
                            "Properties"),
                        "Item",
                        Expression.Constant(schemaField.metadata.name));

                if (schemaField.type.id == BondDataType.BT_LIST || schemaField.type.id == BondDataType.BT_SET)
                {

                    var constructorInfo = typeof(List<IRuntimeBonded>).GetConstructor(typeof(IEnumerable<IRuntimeBonded>));
                    property = Expression.New(constructorInfo, Expression.Convert(property, typeof(IEnumerable<IRuntimeBonded>)));
                }
                else if (schemaField.type.id == BondDataType.BT_MAP)
                {
                    var keyType = fieldSchemaTypeWrapped.GetGenericArguments().First();
                    var valueType = fieldSchemaTypeWrapped.GetGenericArguments().Last();

                    var castMethod = typeof(Enumerable).GetDeclaredMethods("Cast").Single();
                    var rangeMethod = typeof(Enumerable).GetDeclaredMethods("Range").Single();
                    var toDictMethod = typeof(Enumerable).GetDeclaredMethods("ToDictionary").Where(x => x.GetParameters().Count() == 3).Last(); //TODO: Get this by param list instead.

                    var iDictValues = Expression.Property(Expression.Convert(property, typeof(IDictionary)), "Values");
                    var values = Expression.Call(typeof(Enumerable), "Cast", new[] { valueType }, new[] { iDictValues });

                    var iDictKeys = Expression.Property(Expression.Convert(property, typeof(IDictionary)), "Keys");
                    var keys = Expression.Call(typeof(Enumerable), "Cast", new[] { keyType }, new[] { iDictKeys });

                    var keyCount = Expression.Call(typeof(Enumerable), "Count", new[] { keyType }, new[] { keys });
                    var range = Expression.Call(typeof(Enumerable), "Range", new Type[] { }, new Expression[] { Expression.Constant(0, typeof(int)), keyCount });

                    var index = Expression.Parameter(typeof(int));
                    var keyElement = Expression.Call(typeof(Enumerable), "ElementAt", new[] { keyType }, new Expression[] { keys, index });
                    var valueElement = Expression.Call(typeof(Enumerable), "ElementAt", new[] { valueType }, new Expression[] { values, index });

                    Expression keySelector = Expression.Lambda(keyElement, new[] { index });
                    Expression valueSelector = Expression.Lambda(valueElement, new[] { index });
                    
                    property = Expression.Call(typeof(Enumerable), "ToDictionary", new[] { typeof(int), keyType, valueType }, new Expression[] { range, keySelector, valueSelector });
                }
                else
                    throw new ArgumentOutOfRangeException(nameof(fieldSchemaTypeWrapped));
            }
            else
            {
                property = RuntimeDataExpression.RuntimePropertyGetter(structVar, schemaField.metadata.name, fieldSchemaTypeWrapped);
            }

            var fieldValue = Expression.Condition(containsKey,
                Expression.Convert(property, fieldSchemaTypeWrapped),
                Expression.Constant(defaultValue, fieldSchemaTypeWrapped));

            var parser = new RuntimeObjectParser(this, fieldValue, schemaField.type);

            var processField = field != null
                ? field.Value(parser, fieldType)
                : transform.UnknownField(parser, fieldType, fieldId) ?? Expression.Empty();

            var omitField = field != null
                ? field.Omitted : Expression.Empty();

            Expression cannotOmit;

            if (schemaField.type.id == BondDataType.BT_STRUCT || schemaField.type.bonded_type || schemaField.metadata.modifier != Modifier.Optional)
            {
                cannotOmit = Expression.Constant(true);
            }
            else
            {
                if (fieldSchemaType.IsBondBlob())
                {
                    cannotOmit = Expression.NotEqual(fieldValue, Expression.Default(typeof(ArraySegment<byte>)));
                }
                else if (fieldSchemaType.IsBondContainer())
                {
                    if (defaultValue == null)
                        cannotOmit = Expression.NotEqual(fieldValue, Expression.Constant(null));
                    else if (schemaField.type.id == BondDataType.BT_LIST && schemaField.type.list_sub_type == ListSubType.NULLABLE_SUBTYPE) //We have a nullable type, instead of an actual container
                        cannotOmit = Expression.NotEqual(ContainerCount(fieldValue), Expression.Constant(0));
                    else
                        cannotOmit = Expression.Constant(true);
                }
                else
                {
                    if(defaultValue == null)
                        cannotOmit = Expression.NotEqual(fieldValue, Expression.Constant(null));
                    else
                        cannotOmit = Expression.AndAlso(
                            containsKey,
                            Expression.NotEqual(fieldValue, Expression.Convert(Expression.Constant(defaultValue), fieldSchemaType)));
                }
            }

            return PrunedExpressionCopy.IfThenElse(cannotOmit, processField, omitField);
        }
        
        public Expression Container(BondDataType? expectedType, ContainerHandler handler)
        {
            if (typeDef.list_sub_type ==  ListSubType.BLOB_SUBTYPE
                && typeDef.element.id == BondDataType.BT_INT8)
                return BlobContainer(handler);

            ContainerItemHandler itemHandler = (item, next, count) => handler(
                new RuntimeObjectParser(this, item, typeDef.element),
                Expression.Constant(typeDef.element.id),
                next,
                count);

            return ListContainer(itemHandler);
        }

        public Expression Map(BondDataType? expectedKeyType, BondDataType? expectedValueType, MapHandler handler)
        {
            Debug.Assert(typeDef.id == BondDataType.BT_MAP);

            return EnumerableContainer((item, next, count) => handler(
                new RuntimeObjectParser(this, Expression.Property(item, "Key"), typeDef.key),
                new RuntimeObjectParser(this, Expression.Property(item, "Value"), typeDef.element),
                Expression.Constant(typeDef.key.id),
                Expression.Constant(typeDef.element.id),
                next,
                Expression.Empty(),
                count));

            
        }

        public Expression Scalar(Expression valueType, BondDataType expectedType, ValueHandler handler)
        {
            Debug.Assert(expectedType == typeDef.id);
            //return handler(typeAlias.Convert(value, schemaType));
            return handler(value);
        }

        public Expression Bonded(ValueHandler handler)
        {
            if (typeDef.bonded_type)
            {
                return handler(value);
            }

            var newBonded = bondedFactory(objectType, value);
            return handler(newBonded);
        }
        
        public Expression Blob(Expression count)
        {
            //if ((typeDef.id == BondDataType.BT_SET || typeDef.id == BondDataType.BT_LIST)
            if (typeDef.list_sub_type == ListSubType.BLOB_SUBTYPE && typeDef.element.id == BondDataType.BT_INT8)
            {
                //BT_INT8 is signed. Flipping to unsigned.
                //return Expression.New(typeof(List<byte>).GetConstructor(typeof(IEnumerable<byte>)), Expression.Convert(Expression.Call(value, typeof(List<sbyte>).FindMethod("ToArray")), typeof(byte[])));
                return Expression.New(typeof(ArraySegment<byte>).GetConstructor(typeof(byte[])), Expression.Convert(Expression.Call(value, typeof(List<sbyte>).FindMethod("ToArray")), typeof(byte[])));
                //return Expression.New(typeof(ArraySegment<byte>).GetConstructor(typeof(byte[])), Expression.Call(value, typeof(List<byte>).FindMethod("ToArray")));
            }

            if (objectType == typeof(byte[]))
                return Expression.New(arraySegmentCtor, value);

            return null;
        }

        public Expression Skip(Expression valueType)
        {
            return Expression.Empty();
        }

        public override bool Equals(object that)
        {
            Debug.Assert(that is RuntimeObjectParser);
            return typeDef.id == BondDataType.BT_STRUCT && typeDef == (that as RuntimeObjectParser).typeDef;
        }

        public override int GetHashCode()
        {
            return typeDef.GetHashCode();
        }

        static Expression NewBonded(Type objectType, Expression value)
        {
            var ctor = typeof(RuntimeBonded<>).MakeGenericType(objectType).GetConstructor(objectType);
            return Expression.New(ctor, value);
        }

        static Expression ContainerCount(Expression container)
        {
            if (container.Type.IsArray)
                return Expression.ArrayLength(container);

            if (container.Type.IsBondBlob())
                return Expression.Property(container, "Count");

            return Expression.Property(container, container.Type.GetDeclaredProperty(typeof(ICollection<>), "Count", typeof(int)));
        }

        Expression EnumerableContainer(ContainerItemHandler handler)
        {
            Debug.Assert(typeDef.id == BondDataType.BT_LIST
                || typeDef.id == BondDataType.BT_SET
                || typeDef.id == BondDataType.BT_MAP);

            var methodGetEnumerator = value.Type.GetMethod(typeof(IEnumerable<>), "GetEnumerator");
            Debug.Assert(methodGetEnumerator != null, "Container must provide GetEnumerator method");

            var enumerator = Expression.Variable(methodGetEnumerator.ReturnType, "enumerator");
            var item = Expression.Property(enumerator, "Current");
            var next = Expression.Call(enumerator, moveNext);

            //TODP: Remove
            var t = Expression.Block(
                new[] { enumerator },
                Expression.Assign(enumerator, Expression.Call(value, methodGetEnumerator)),
                handler(item, next, ContainerCount(value)));
            return Expression.Block(
                new[] { enumerator },
                Expression.Assign(enumerator, Expression.Call(value, methodGetEnumerator)),
                handler(item, next, ContainerCount(value)));
        }

        Expression ListContainer(ContainerItemHandler handler)
        {
            Debug.Assert(typeDef.id == BondDataType.BT_LIST || typeDef.id == BondDataType.BT_SET);

            var count = Expression.Variable(typeof(int), "count");
            var index = Expression.Variable(typeof(int), "index");
            var item = Expression.Property(value, "Item", new Expression[] { index });
            var next = Expression.LessThan(Expression.PreIncrementAssign(index), count);

            return Expression.Block(
                new[] { index, count },
                Expression.Assign(index, Expression.Constant(-1)),
                Expression.Assign(count, ContainerCount(value)),
                handler(item, next, count));
        }

        Expression BlobContainer(ContainerHandler handler)
        {
            Debug.Assert(typeDef.element?.id == BondDataType.BT_INT8
                 && (typeDef.list_sub_type == ListSubType.BLOB_SUBTYPE));

            var arraySegment = Expression.Variable(typeof(ArraySegment<byte>), "arraySegment");
            var count = Expression.Variable(typeof(int), "count");
            var index = Expression.Variable(typeof(int), "index");
            var end = Expression.Variable(typeof(int), "end");
            //var blob = typeAlias.Convert(value, schemaType);
            var item = Expression.ArrayIndex(Expression.Property(arraySegment, "Array"), Expression.PostIncrementAssign(index));

            var loop = handler(
                new RuntimeObjectParser(this, item, typeDef.element),
                Expression.Constant(BondDataType.BT_INT8),
                Expression.LessThan(index, end),
                count);

            var data = Expression.New(typeof(ArraySegment<byte>).GetConstructor(typeof(byte[])), Expression.Convert(Expression.Call(value, typeof(List<sbyte>).FindMethod("ToArray")), typeof(byte[])));

            return Expression.Block(
                new[] { arraySegment, count, index, end },
                Expression.Assign(arraySegment, data),
                Expression.Assign(index, Expression.Property(arraySegment, "Offset")),
                Expression.Assign(count, Expression.Property(arraySegment, "Count")),
                Expression.Assign(end, Expression.Add(index, count)),
                loop);
        }

        static Type GetSchemaType(RuntimeSchema schema, TypeDef typeDef, bool shouldWrapBondedTypes = false, bool shouldWrapNullableTypes = false)
        {
            //The issue is nullable vs list. All nullable show up as a list with a list sub type
            Type schemaType;
            switch (typeDef.id)
            {
                case BondDataType.BT_STRUCT:
                    schemaType = typeof(RuntimeObject);
                    break;
                case BondDataType.BT_LIST:
                case BondDataType.BT_SET:
                    if(typeDef.list_sub_type == ListSubType.BLOB_SUBTYPE)
                        schemaType = typeof(List<>).MakeGenericType(typeof(sbyte));
                    else
                        schemaType = typeof(List<>).MakeGenericType(GetSchemaType(schema, typeDef.element, true, false));
                    break;
                case BondDataType.BT_MAP:
                    schemaType = typeof(Dictionary<,>).MakeGenericType(
                        GetSchemaType(schema, typeDef.key), GetSchemaType(schema, typeDef.element, true, false));
                    break;
                case BondDataType.BT_STRING:
                case BondDataType.BT_WSTRING:
                    schemaType = typeof(string); //String is already nullable
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

            if (shouldWrapBondedTypes)
            {
                if (typeDef.bonded_type)
                    schemaType = typeof(IRuntimeBonded);
            }

            if (shouldWrapNullableTypes && typeDef.id != BondDataType.BT_STRUCT && typeDef.id != BondDataType.BT_LIST && typeDef.id != BondDataType.BT_MAP && typeDef.id != BondDataType.BT_SET && typeDef.id != BondDataType.BT_STRING && typeDef.id != BondDataType.BT_WSTRING)
            {
                schemaType = typeof(Nullable<>).MakeGenericType(schemaType);
            }

            return schemaType;
        }

        static object GetDefaultValue(RuntimeSchema schema, FieldDef schemaField)
        {
            if (schemaField.metadata.default_value.nothing)
                return null;

            switch (schemaField.type.id)
            {
                case BondDataType.BT_BOOL:
                    return 0 != schemaField.metadata.default_value.uint_value;
                case BondDataType.BT_UINT8:
                    return (byte)schemaField.metadata.default_value.uint_value;
                case BondDataType.BT_UINT16:
                    return (UInt16)schemaField.metadata.default_value.uint_value;
                case BondDataType.BT_UINT32:
                    return (UInt32)schemaField.metadata.default_value.uint_value;
                case BondDataType.BT_UINT64:
                    return schemaField.metadata.default_value.uint_value;
                case BondDataType.BT_FLOAT:
                    return (float)schemaField.metadata.default_value.double_value;
                case BondDataType.BT_DOUBLE:
                    return schemaField.metadata.default_value.double_value;
                case BondDataType.BT_STRING:
                    return schemaField.metadata.default_value.string_value;
                case BondDataType.BT_LIST:
                case BondDataType.BT_SET:
                case BondDataType.BT_MAP:
                    if (schemaField.type.bonded_type)
                    {
                        // TODO: Return RuntimeBonded<T>(null), where T is the correct list type
                        throw new NotImplementedException();
                    }
                    return null; //if this is a nullable denoted by the list subtype, return null
                case BondDataType.BT_INT8:
                    return (sbyte)schemaField.metadata.default_value.int_value;
                case BondDataType.BT_INT16:
                    return (Int16)schemaField.metadata.default_value.int_value;
                case BondDataType.BT_INT32:
                    return (Int32)schemaField.metadata.default_value.int_value;
                case BondDataType.BT_INT64:
                    return schemaField.metadata.default_value.int_value;
                case BondDataType.BT_WSTRING:
                    return schemaField.metadata.default_value.wstring_value;
                case BondDataType.BT_STRUCT:
                    if (schemaField.type.bonded_type)
                    {
                        return new RuntimeBonded<RuntimeObject>(new RuntimeObject(), schema, schemaField.type);
                    }
                    return new RuntimeObject();
                default:
                    throw new InvalidDataException(string.Format("Invalid BondDataType {0}", schemaField.type.id));
            }
        }

        static int GetHierarchyDepth(RuntimeSchema schema, StructDef structDef)
        {
            int depth = 0;

            while (structDef != null && structDef.base_def != null)
            {
                depth += 1;

                structDef = schema.SchemaDef.structs[structDef.base_def.struct_def];
            }

            return depth;
        }
    }
}
