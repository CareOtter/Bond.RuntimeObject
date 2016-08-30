namespace Bond.RuntimeObject.Expressions
{
    using System;
    using System.Globalization;
    using System.Linq.Expressions;
    using Bond.Expressions;

    internal static class RuntimeSerializerGeneratorFactory<R, W>
    {
        public static ISerializerGenerator<R, W> Create(
            Expression<Action<R, W, int>> deferredSerialize, RuntimeSchema schema, TypeDef typeDef,
            bool inlineNested = true)
        {
            return Cache.Create(deferredSerialize, schema, typeDef, inlineNested);
        }

        static class Cache
        {
            public static readonly Func<Expression<Action<R, W, int>>, RuntimeSchema, TypeDef, bool, ISerializerGenerator<R, W>> Create;

            [System.Diagnostics.CodeAnalysis.SuppressMessage(
                "Microsoft.Design", "CA1065:DoNotRaiseExceptionsInUnexpectedLocations")]
            static Cache()
            {
                Type generator;

                var attribute = typeof(W).GetAttribute<SerializerAttribute>();
                if (attribute == null)
                {
                    generator = typeof(RuntimeSerializerTransform<R, W>);
                }
                else
                {
                    var serializerType = ReflectionHelper.GetSerializerAttributeType(attribute);
                    if (!serializerType.IsGenericType() || serializerType.GetGenericParameters().Length != 2)
                    {
                        throw new InvalidOperationException(
                            "Serializer generator is expected to be a generic type with two type parameters.");
                    }

                    generator = serializerType.MakeGenericType(typeof(R), typeof(W));

                    if (!typeof(ISerializerGenerator<R, W>).IsAssignableFrom(generator))
                    {
                        throw new InvalidOperationException(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Serializer generator {0} specified for writer {1} is not an ISerializerGenerator.",
                                generator, typeof(W)));
                    }
                }

                var ctor =
                    generator.GetConstructor(typeof(Expression<Action<R, W, int>>), typeof(RuntimeSchema), typeof(bool)) ??
                    generator.GetConstructor(typeof(Expression<Action<R, W, int>>), typeof(RuntimeSchema));

                if (ctor == null)
                {
                    throw new InvalidOperationException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Constructor {0}(Expression<Action<R, W, int>>, RuntimeSchema, TypeDef) not defined.",
                            generator));
                }

                var deferredSerialize = Expression.Parameter(typeof(Expression<Action<R, W, int>>));
                var schema = Expression.Parameter(typeof(RuntimeSchema));
                var typeDef = Expression.Parameter(typeof(TypeDef));
                var inlineNested = Expression.Parameter(typeof(bool));

                var newExpression = 
                    ctor.GetParameters().Length == 3
                        ? Expression.New(ctor, deferredSerialize, schema, inlineNested)
                        : Expression.New(ctor, deferredSerialize, schema);

                Create =
                    Expression.Lambda<Func<Expression<Action<R, W, int>>, RuntimeSchema, TypeDef, bool, ISerializerGenerator<R, W>>>(
                        newExpression,
                        deferredSerialize,
                        schema,
                        typeDef,
                        inlineNested).Compile();
            }
        }
    }
}
