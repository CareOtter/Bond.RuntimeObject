namespace Bond.RuntimeObject
{
    using System;
    using System.Linq.Expressions;
    using Bond.Expressions;
    using Expressions;

    /// <summary>
    /// Generic object factory
    /// </summary>
    public static class RuntimeGenericFactory
    {
        /// <summary>
        /// Create an instance of T
        /// </summary>
        /// <typeparam name="T">Type of object to create</typeparam>
        /// <returns>Object of type T initialized to the default value</returns>
        public static T Create<T>()
        {
            return Cache<T>.Create();
        }

        static class Cache<T>
        {
            public static readonly Func<T> Create;

            static Cache()
            {
                Expression create;

                if (typeof(T) == typeof(string))
                {
                    create = StringExpressionCopy.Empty();
                }
                else if (typeof(T).IsRuntimeBonded())
                {
                    create = Expression.Field(null, 
                        typeof(RuntimeBonded<>).MakeGenericType(typeof(T).GetValueType()).GetField("Empty"));
                }
                else if (typeof(T).IsClass())
                {
                    create = Expression.New(typeof(T));
                }
                else
                {
                    create = Expression.Default(typeof(T));
                }

                Create = Expression.Lambda<Func<T>>(create).Compile();
            }
        }
    }
}
