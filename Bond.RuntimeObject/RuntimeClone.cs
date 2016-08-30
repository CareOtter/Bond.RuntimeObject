namespace Bond.RuntimeObject
{
    using System;
    using System.Linq;
    using Bond.Expressions;
    using Expressions;

    /// <summary>
    /// Clone API for creating objects of type T by deep cloning properties/fields of a compatible object.
    /// </summary>
    public static class RuntimeClone<T>
    {
        /// <summary>
        /// Create an instance of type <typeparamref name="T"/> by deep cloning properties/fields of a source object.
        /// </summary>
        /// <param name="source">source object to create a clone from</param>
        /// <param name="schema"></param>
        /// <param name="typeDef"></param>
        /// <returns></returns>
        public static T From(object source, RuntimeSchema schema, TypeDef typeDef)
        {
            return new RuntimeCloner(schema, typeDef).Clone<T>(source);
        }
    }

    /// <summary>
    /// Utility for cloning objects.
    /// </summary>
    public class RuntimeCloner
    {
        readonly Func<object, object>[] clone;

        /// <summary>
        /// Create a cloner that makes clones of the specified type.
        /// </summary>
        /// <param name="schema"></param>
        /// <param name="typeDef"></param>
        public RuntimeCloner(RuntimeSchema schema, TypeDef typeDef)
            : this(schema, typeDef, (IParser)null)
        {}

        /// <summary>
        /// Create a cloner that makes clones of the specified type.
        /// </summary>
        /// <param name="schema"></param>
        /// <param name="typeDef"></param>
        /// <param name="parser">Custom <see cref="IParser"/> instance</param>
        public RuntimeCloner(RuntimeSchema schema, TypeDef typeDef, IParser parser)
        {
            clone = Generate(schema,
                typeDef,
                new RuntimeDeserializerTransform<object>((o, i) => clone[i](o)),
                parser);
        }

        /// <summary>
        /// Create a cloner that makes clones of the specified type.
        /// </summary>
        /// <param name="schema"></param>
        /// <param name="typeDef"></param>
        /// <param name="factory">factory implementing <see cref="IFactory"/> interface</param>
        public RuntimeCloner(RuntimeSchema schema, TypeDef typeDef, IRuntimeFactory factory)
            : this(schema, typeDef, null, factory)
        {}

        /// <summary>
        /// Create a cloner that uses specified factory and makes clones of the specified type.
        /// </summary>
        /// <param name="schema"></param>
        /// <param name="typeDef"></param>
        /// <param name="parser">Custom <see cref="IParser"/> instance</param>
        /// <param name="factory">factory implementing <see cref="IFactory"/> interface</param>
        public RuntimeCloner(RuntimeSchema schema, TypeDef typeDef, IParser parser, IRuntimeFactory factory)
        {
            clone = Generate(schema,
                typeDef,
                new RuntimeDeserializerTransform<object>(
                    (o, i) => clone[i](o),
                    true,
                    (t1, t2, count) => factory.CreateContainer(t1, t2, count)),
                parser);
        }

        /// <summary>
        /// Create a cloner that uses specified factory and makes clones of the specified type.
        /// </summary>
        /// <param name="schema"></param>
        /// <param name="typeDef"></param>
        /// <param name="factory">factory delegate returning expressions to create objects</param>
        public RuntimeCloner(RuntimeSchema schema, TypeDef typeDef, RuntimeFactory factory)
            : this(schema, typeDef, null, factory)
        {}

        /// <summary>
        /// Create a cloner that uses specified factory and makes clones of the specified type.
        /// </summary>
        /// <param name="schema"></param>
        /// <param name="typeDef"></param>
        /// <param name="parser">Custom <see cref="IParser"/> instance</param>
        /// <param name="factory">factory delegate returning expressions to create objects</param>
        public RuntimeCloner(RuntimeSchema schema, TypeDef typeDef, IParser parser, RuntimeFactory factory)
        {
            clone = Generate(schema,
                typeDef,
                new RuntimeDeserializerTransform<object>(
                    (o, i) => clone[i](o),
                    factory),
                parser);
        }

        /// <summary>
        /// Clone the source object into an object of type <typeparamref name="T"/>.
        /// </summary>
        /// <param name="source">source object to be cloned</param>
        /// <returns>clone of the source object projected on type <typeparamref name="T"/></returns>
        public T Clone<T>(object source)
        {
            return (T)clone[0](source);
        }

        static Func<object, object>[] Generate(RuntimeSchema schema, TypeDef typeDef,
            RuntimeDeserializerTransform<object> transform, IParser parser)
        {
            parser = parser ?? new RuntimeObjectParser(schema, typeDef);

            return transform.Generate(parser, schema, typeDef)
                .Select(lambda => lambda.Compile())
                .ToArray();
        }
    }
}
