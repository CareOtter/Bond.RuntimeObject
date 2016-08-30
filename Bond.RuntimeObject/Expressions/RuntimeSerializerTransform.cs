namespace Bond.RuntimeObject.Expressions
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Linq.Expressions;
    using Bond.Expressions;
    using Bond.Protocols;

    public abstract class RuntimeSerializerGenerator<R, W> : ISerializerGenerator<R, W>
    {
        protected delegate Expression Serialize(IParser parser);
        readonly Expression<Action<R, W, int>> deferredSerialize;
        readonly List<Expression<Action<R, W>>> serializeActions = new List<Expression<Action<R, W>>>();
        readonly Dictionary<KeyValuePair<IParser, Serialize>, int> serializeIndex =
            new Dictionary<KeyValuePair<IParser, Serialize>, int>();
        readonly Stack<KeyValuePair<IParser, Serialize>> inProgress =
            new Stack<KeyValuePair<IParser, Serialize>>();

        protected RuntimeSerializerGenerator(Expression<Action<R, W, int>> deferredSerialize)
        {
            this.deferredSerialize = deferredSerialize;
        }

        public abstract IEnumerable<Expression<Action<R, W>>> Generate(IParser parser);

        protected IEnumerable<Expression<Action<R, W>>> SerializeActions { get { return serializeActions; } }

        /// <summary>
        /// Generate expression provided by Serialize delegate, either as inline expression or a lambda call
        /// </summary>
        /// <param name="serialize">Delegate to generate serialization expression</param>
        /// <param name="parser">Parser used for the source of serialization</param>
        /// <param name="writer">Writer to use for serialization</param>
        /// <param name="inline">True if the expression can be inlined</param>
        /// <remarks>
        /// Generates lambda calls for recursive schemas to avoid infinitely inlining the same expression.
        /// Expression is considered the same when both serialize delegate and parser are the same. It is
        /// caller's responsibility to assure that the pair of serialize delegate and parser can be used 
        /// to identify generated expression. For object serializer, when the parser is ObjectParser, this 
        /// is generally guaranteed by using parser instance per schema type. Transcoding may use a single 
        /// parser instance but different Serialize delegates for each transcoded schema type (e.g. when 
        /// the delegate captures schema metadata).
        /// </remarks>
        protected Expression GenerateSerialize(Serialize serialize, IParser parser, ParameterExpression writer, bool inline)
        {
            var key = new KeyValuePair<IParser, Serialize>(parser, serialize);
            inline = inline && inProgress.Count != 0 && !inProgress.Contains(key);
            Expression body;

            inProgress.Push(key);

            if (inline)
            {
                body = serialize(parser);

                if (parser.ReaderParam != parser.ReaderValue && parser.ReaderValue.Type.IsBondStruct())
                {
                    body = Expression.Block(
                        new[] { parser.ReaderParam },
                        Expression.Assign(parser.ReaderParam, Expression.Convert(parser.ReaderValue, parser.ReaderParam.Type)),
                        body);
                }
            }
            else
            {
                int index;
                if (!serializeIndex.TryGetValue(key, out index))
                {
                    index = serializeActions.Count;
                    serializeIndex[key] = index;
                    serializeActions.Add(null);
                    var action = serialize(parser);

                    serializeActions[index] = Expression.Lambda<Action<R, W>>(
                        action,
                        parser.ReaderParam,
                        writer);
                }

                body = Expression.Invoke(
                    deferredSerialize,
                    PrunedExpressionCopy.Convert(parser.ReaderValue, parser.ReaderParam.Type),
                    writer,
                    Expression.Constant(index));
            }

            inProgress.Pop();
            return body;
        }
    }

    internal class RuntimeSerializerTransform<R, W> : RuntimeSerializerGenerator<R, W>
    {
        delegate Expression SerializeWithSchema(IParser parser, RuntimeSchema schema);
        
        readonly RuntimeSchema runtimeSchema;
        readonly ProtocolWriter<W> writer = new ProtocolWriter<W>();
        readonly Dictionary<RuntimeSchema, Serialize> serializeDelegates = 
            new Dictionary<RuntimeSchema, Serialize>(newTypeDefComparer());
        readonly bool inlineNested;
        static readonly Func<IEqualityComparer<RuntimeSchema>> newTypeDefComparer = CreateNewTypeDefComparerFunc();

        static readonly bool untaggedWriter =
            typeof (IUntaggedProtocolReader).IsAssignableFrom(ReflectionHelper.GetReaderAttributeType(typeof(W).GetAttribute<ReaderAttribute>()));
        static readonly bool binaryWriter = untaggedWriter
            || typeof(ITaggedProtocolReader).IsAssignableFrom(ReflectionHelper.GetReaderAttributeType(typeof(W).GetAttribute<ReaderAttribute>()));
        
        public RuntimeSerializerTransform(Expression<Action<R, W, int>> deferredSerialize, RuntimeSchema schema, bool inlineNested = true)
            : base(deferredSerialize)
        {
            runtimeSchema = schema;
            this.inlineNested = inlineNested;
        }

        public override IEnumerable<Expression<Action<R, W>>> Generate(IParser parser)
        {
            GenerateSerialize(Struct, parser, runtimeSchema);

            return SerializeActions;
        }

        Expression GenerateSerialize(Serialize serialize, IParser parser)
        {
            return GenerateSerialize(serialize, parser, writer.Param, inline: false);
        }

        Expression GenerateSerialize(SerializeWithSchema serializeWithSchema, IParser parser, RuntimeSchema schema)
        {
            Debug.Assert(schema.HasValue);

            Serialize serialize;
            if (!serializeDelegates.TryGetValue(schema, out serialize))
            {
                serialize = serializeDelegates[schema] = p => serializeWithSchema(p, schema);
            }
            // Transcoding from tagged protocol with runtime schema generates enormous expression tree
            // and for large schemas JIT fails to compile resulting lambda (InvalidProgramException).
            // As a workaround we don't inline nested serialize expressions in this case.
            var inline = !typeof(ITaggedProtocolReader).IsAssignableFrom(parser.ReaderParam.Type);

            inline = inline && (this.inlineNested || !schema.IsStruct);

            return GenerateSerialize(serialize, parser, writer.Param, inline);
        }

        Expression Struct(IParser parser, RuntimeSchema schema)
        {
            return Struct(parser, schema, false);
        }

        Expression Struct(IParser parser, RuntimeSchema schema, bool isBase)
        {
            var metadata = Expression.Constant(schema.StructDef.metadata);
            var baseSchema = schema.HasBase ? schema.GetBaseSchema() : RuntimeSchema.Empty;

            return parser.Apply(new Transform(
                Begin: () => isBase ? writer.WriteBaseBegin(metadata) : writer.WriteStructBegin(metadata),
                End: () => isBase ? writer.WriteBaseEnd() : writer.WriteStructEnd(),
                Fields:
                    from field in schema.StructDef.fields
                    select new Field(
                        Id: field.id,
                        Value: (fieldParser, fieldType) => 
                            Expression.Block(
                                writer.WriteFieldBegin(fieldType, field.id, field.metadata),
                                Value(fieldParser, fieldType, schema.GetFieldSchema(field)),
                                writer.WriteFieldEnd()),
                        Omitted: () => writer.WriteFieldOmitted(field.type.id, field.id, field.metadata)),
                UnknownField: (fieldParser, fieldType, fieldId) =>
                    Expression.Throw(Expression.New(typeof(NotImplementedException))),
                    // TODO: Should UnknownField just be null?
                    //Expression.Block(
                    //    writer.WriteFieldBegin(fieldType, fieldId, metadata),
                    //    Value(fieldParser, fieldType, schema),
                    //    writer.WriteFieldEnd()),
                Base: baseParser => 
                    baseSchema.HasValue ? Struct(baseParser, baseSchema, isBase: true) : Expression.Empty(),
                UnknownEnd: () => writer.WriteBaseEnd()));
        }

        Expression Container(IParser parser)
        {
            return Container(parser, RuntimeSchema.Empty);
        }

        Expression Container(IParser parser, RuntimeSchema schema)
        {
            var expectedValueType = schema.HasValue ? schema.TypeDef.element.id : (BondDataType?)null;

            return parser.Container(expectedValueType,
                (valueParser, elementType, next, count) =>
                {
                    var body = ControlExpressionCopy.While(next,
                        Expression.Block(
                            writer.WriteItemBegin(),
                            Value(valueParser, elementType, schema.GetElementSchema()),
                            writer.WriteItemEnd()));

                    var blob = parser.Blob(count);
                    if (blob != null)
                    {
                        body = PrunedExpressionCopy.IfThenElse(
                            Expression.Equal(elementType, Expression.Constant(BondDataType.BT_INT8)),
                            writer.WriteBytes(
                               blob),
                            body);

                        // For binary protocols we can write blob directly using protocols's WriteBytes
                        // even if the container is not a blob (blob is BT_LIST of BT_INT8).
                        if (binaryWriter)
                            body = PrunedExpressionCopy.IfThenElse(
                                Expression.Equal(elementType, Expression.Constant(BondDataType.BT_UINT8)),
                                writer.WriteBytes(blob),
                                body);
                    }

                    return Expression.Block(
                        writer.WriteContainerBegin(count, elementType),
                        body,
                        writer.WriteContainerEnd());
                });
        }

        Expression Map(IParser parser, RuntimeSchema schema)
        {
            var expectedValueType = schema.HasValue ? schema.TypeDef.element.id : (BondDataType?)null;
            var expectedKeyType = schema.HasValue ? schema.TypeDef.key.id : (BondDataType?)null;
            
            //TODO: Replace this with return statement
            var m = parser.Map(expectedKeyType, expectedValueType, 
                (keyParser, valueParser, keyType, valueType, nextKey, nextValue, count) =>
                Expression.Block(
                    writer.WriteContainerBegin(count, keyType, valueType),
                        ControlExpressionCopy.While(nextKey,
                            Expression.Block(
                                writer.WriteItemBegin(),
                                Value(keyParser, keyType, schema.GetKeySchema()),
                                writer.WriteItemEnd(),
                                nextValue,
                                writer.WriteItemBegin(),
                                Value(valueParser, valueType, schema.GetElementSchema()),
                                writer.WriteItemEnd())),
                    writer.WriteContainerEnd()));
            return m;
        }

        Expression Value(IParser parser, Expression valueType, RuntimeSchema schema)
        {
            Debug.Assert(schema.HasValue);

            if (parser.IsBonded || (untaggedWriter && schema.IsBonded))
                return parser.Bonded(value =>
                    writer.WriteBonded(PrunedExpressionCopy.Convert(value, typeof(IRuntimeBonded)), true));


            if (schema.IsStruct)
                return GenerateSerialize(Struct, parser, schema);

            if (schema.IsMap)
                return GenerateSerialize(Map, parser, schema);

            if (schema.IsContainer)
                return GenerateSerialize(Container, parser, schema);

            return parser.Scalar(valueType, schema.TypeDef.id,
                value => writer.Write(value, schema.TypeDef.id));
        }

        private static Func<IEqualityComparer<RuntimeSchema>> CreateNewTypeDefComparerFunc()
        {
            var typeDefComparerType = Type.GetType("Bond.TypeDefComparer, Bond");
            var newExpression = Expression.New(typeDefComparerType);

            return Expression.Lambda<Func<IEqualityComparer<RuntimeSchema>>>(newExpression).Compile();
        }
    }
}
