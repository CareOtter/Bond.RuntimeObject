namespace Bond.RuntimeObject
{
    using System;
    using System.Linq;
    using Bond.Expressions;
    using Expressions;

    /// <summary>
    /// Serializes <see cref="IRuntimeObject"/>s to a protocol writer using only a runtime schema.
    /// </summary>
    public static class RuntimeSerialize
    {
        /// <summary>
        /// Serializes a <see cref="IRuntimeObject"/> to a protocol writer using only a runtime schema.
        /// </summary>
        /// <param name="writer">The protocol writer to write the object data to.</param>
        /// <param name="obj">The object to serialize.</param>
        /// <param name="schema">The schema to use when writing the object data.</param>
        public static void To<W>(W writer, IRuntimeObject obj, RuntimeSchema schema)
        {
            new RuntimeSerializer<W>(schema).Serialize(writer, obj);
        }

        /// <summary>
        /// Serializes a <see cref="IRuntimeObject"/> to a protocol writer using only a runtime schema.
        /// </summary>
        /// <param name="writer">The protocol writer to write the object data to.</param>
        /// <param name="bonded">The bonded object to serialize.</param>
        /// <param name="schema">The schema to use when writing the object data.</param>
        /// <param name="typeDef"></param>
        public static void To<W, T>(W writer, IRuntimeBonded<T> bonded, RuntimeSchema schema, TypeDef typeDef)
        {
            bonded.Serialize(writer);
        }
    }

    /// <summary>
    /// Serializes <see cref="IRuntimeObject"/>s to a protocol writer using only a runtime schema.
    /// </summary>
    public class RuntimeSerializer<W>
    {
        readonly Action<object, W>[] serialize;

        public RuntimeSerializer(RuntimeSchema schema)
            : this(schema, schema.SchemaDef.root, null, inlineNested: true)
        {
        }

        public RuntimeSerializer(RuntimeSchema schema, TypeDef typeDef)
            : this(schema, typeDef, null, inlineNested: true)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RuntimeSerializer{W}"/> class with a given runtime schema.
        /// </summary>
        /// <param name="schema">The schema to use when serializing objects.</param>
        /// <param name="typeDef"></param>
        /// <param name="parser"></param>
        /// <param name="inlineNested"></param>
        public RuntimeSerializer(RuntimeSchema schema, TypeDef typeDef, IParser parser, bool inlineNested)
        {
            parser = parser ?? new RuntimeObjectParser(schema, typeDef);
            var tree = RuntimeSerializerGeneratorFactory<object, W>.Create(
                    (o, w, i) => serialize[i](o, w), schema, typeDef, inlineNested)
                .Generate(parser);

            serialize = tree.Select(lambda => lambda.Compile()).ToArray();
        }

        /// <summary>
        /// Serializes an object to a protocol writer.
        /// </summary>
        /// <param name="writer">The protocol writer to write object data to.</param>
        /// <param name="obj">The object to serialize.</param>
        public void Serialize(W writer, object obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException("obj");
            }

            serialize[0](obj, writer);
        }
    }
}
