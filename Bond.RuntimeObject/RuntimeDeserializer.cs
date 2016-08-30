namespace Bond.RuntimeObject
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Bond.Expressions;
    using Bond.IO;
    using Bond.IO.Safe;
    using Bond.Protocols;
    using Expressions;

    /// <summary>
    /// Deserializes <see cref="IRuntimeObject"/>s using only a runtime schema.
    /// </summary>
    public static class RuntimeDeserialize
    {
        /// <summary>
        /// Deserializes a <see cref="RuntimeObject"/> from a using only a runtime schema.
        /// </summary>
        /// <param name="reader">The tagged protocol reader to read the object data from.</param>
        /// <param name="schema">The schema to use when reading the object data.</param>
        /// <returns>A <see cref="RuntimeObject"/> created from <paramref name="reader"/> and <paramref name="schema"/>.</returns>
        public static IRuntimeObject From<R>(R reader, RuntimeSchema schema)
        {
            return new RuntimeDeserializer<R>(schema).Deserialize<IRuntimeObject>(reader);
        }
    }

    /// <summary>
    /// Deserializes <see cref="IRuntimeObject"/>s from a protocol reader using only a <see cref="RuntimeSchema"/>.
    /// </summary>
    public class RuntimeDeserializer<R>
    {
        internal readonly Func<R, object>[] deserialize;

        /// <summary>
        /// Initializes a new instance of the <see cref="RuntimeDeserializer{R}"/> class with a given schema.
        /// </summary>
        /// <param name="schema">The schema to use when deserializing objects.</param>
        public RuntimeDeserializer(RuntimeSchema schema)
            : this(schema, schema.SchemaDef.root)
        {
        }

        public RuntimeDeserializer(RuntimeSchema schema, TypeDef typeDef)
        {
            if (schema.SchemaDef.root.id != BondDataType.BT_STRUCT)
            {
                throw new ArgumentException("The root of the schema must be a struct (id == BT_STRUCT).",
                    "schema");
            }

            var transform = new RuntimeDeserializerTransform<R>((r, i) => deserialize[i](r));
            deserialize = transform.Generate(ParserFactory<R>.Create(schema), schema, typeDef)
                .Select(lambda => lambda.Compile())
                .ToArray();
        }

        /// <summary>
        /// Deserializes a <see cref="IRuntimeObject"/> using only a <see cref="RuntimeSchema"/>.
        /// </summary>
        /// <param name="reader">The reader to read object data from.</param>
        /// <returns>A <see cref="IRuntimeObject"/> created from <paramref name="reader"/>.</returns>
        public T Deserialize<T>(R reader)
        {
            return (T)deserialize[0](reader);
        }
    }

    public static class RuntimeDeserializer
    {
        public static T Deserialize<T, R>(this RuntimeDeserializer<R> deserializer, IRuntimeBonded<T> bonded)
            where R : ICloneable<R>
        {
            var b = bonded as RuntimeBonded<T, R>;
            if (b == null)
                throw new InvalidOperationException(string.Format("Expected RuntimeBonded<{0}, {1}>", typeof(T), typeof(R)));

            return (T)deserializer.deserialize[0](b.reader.Clone());
        }
    }
}
