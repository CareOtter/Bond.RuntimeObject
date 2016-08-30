namespace Bond.RuntimeObject
{
    using System;
    using System.Diagnostics;
    using System.Linq.Expressions;
    using Bond.IO;
    using Expressions;

    /// <summary>
    /// Interface representing bonded payload of unknown type
    /// </summary>
    public interface IRuntimeBonded
    {
        void Serialize<W>(W writer);

        /// <summary>
        /// Deserialize an object from the IRuntimeBonded instance
        /// </summary>
        /// <returns>Deserialized object</returns>
        object Deserialize();
    }

    /// <summary>
    /// Interface representing the schema type bonded&lt;T>
    /// </summary>
    /// <typeparam name="T">Type representing a Bond schema</typeparam>
    public interface IRuntimeBonded<out T> : IRuntimeBonded
    {
        /// <summary>
        /// Deserialize an object of type T from the IRuntimeBonded&lt;T> instance
        /// </summary>
        /// <returns>Deserialized object</returns>
        new T Deserialize();
    }

    public class RuntimeBonded<T> : IRuntimeBonded<T>
    {
        public static readonly RuntimeBonded<T> Empty = new RuntimeBonded<T>(GenericFactory.Create<T>(), RuntimeSchema.Empty, null);
        readonly T instance;
        readonly RuntimeSchema schema;
        readonly TypeDef typeDef;

        public RuntimeBonded(T instance, RuntimeSchema schema, TypeDef typeDef)
        {
            this.instance = instance;
            this.schema = schema;
            this.typeDef = typeDef;
        }

        T IRuntimeBonded<T>.Deserialize()
        {
            return RuntimeClone<T>.From(instance, schema, typeDef);
        }

        object IRuntimeBonded.Deserialize()
        {
            return RuntimeClone<T>.From(instance, schema, typeDef);
        }

        void IRuntimeBonded.Serialize<W>(W writer)
        {
            RuntimeSerialize.To(writer, this, schema, typeDef);
        }
    }

    public sealed class RuntimeBonded<T, R> : IRuntimeBonded<T>
        where R : ICloneable<R>
    {
        static readonly Func<SchemaDef, TypeDef, RuntimeSchema> newRuntimeSchema = GetRuntimeSchemaConstructor();

        internal readonly R reader;
        readonly RuntimeSchema schema;
        readonly TypeDef typeDef;

        public RuntimeBonded(R reader, RuntimeSchema schema, TypeDef typeDef)
        {
            this.reader = reader.Clone();
            this.schema = schema;
            this.typeDef = typeDef;
        }

        T IRuntimeBonded<T>.Deserialize()
        {
            return new RuntimeDeserializer<R>(schema, typeDef).Deserialize<T>(reader.Clone());
        }

        object IRuntimeBonded.Deserialize()
        {
            return new RuntimeDeserializer<R>(schema, typeDef).Deserialize<object>(reader.Clone());
        }

        void IRuntimeBonded.Serialize<W>(W writer)
        {
            //TODO: This is not the fastest performing way of doing this. We actually need to transcode across protocols
            // but will work since we've tied ourself to CompactBinary
            var rto = RuntimeDeserialize.From<R>(reader.Clone(), GetStructSchema(typeDef));
            new RuntimeTranscoder<System.Object, W>(GetStructSchema(typeDef), new RuntimeObjectParser(schema, typeDef), typeDef).Transcode(rto, writer);
        }

        private RuntimeSchema GetStructSchema(TypeDef typeDef)
        {
            Debug.Assert(schema.HasValue);
            var str = schema.SchemaDef.structs[typeDef.struct_def];

            SchemaDef schemaDef = new SchemaDef();

            schemaDef.root = typeDef; //Resetting root to show the subject of bonded (The 'T' in Bonded<T>)
            schemaDef.structs = schema.SchemaDef.structs;

            return newRuntimeSchema(schemaDef, typeDef);
        }

        private static Func<SchemaDef, TypeDef, RuntimeSchema> GetRuntimeSchemaConstructor()
        {
            var constructorInfo = typeof(RuntimeSchema).GetConstructor(typeof(SchemaDef), typeof(TypeDef));

            var schemaDefParam = Expression.Parameter(typeof(SchemaDef));
            var typeDefParam = Expression.Parameter(typeof(TypeDef));

            var newExpression = Expression.New(constructorInfo, schemaDefParam, typeDefParam);

            return Expression
                .Lambda<Func<SchemaDef, TypeDef, RuntimeSchema>>(newExpression, schemaDefParam, typeDefParam)
                .Compile();
        }
    }
}
