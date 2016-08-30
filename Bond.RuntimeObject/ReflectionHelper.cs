namespace Bond.RuntimeObject
{
    using System;
    using System.Linq.Expressions;

    internal static class ReflectionHelper
    {
        public static readonly Func<ReaderAttribute, Type> GetReaderAttributeType = CreateGetPropertyFunc<ReaderAttribute, Type>("ReaderType");
        public static readonly Func<SerializerAttribute, Type> GetSerializerAttributeType
            = CreateGetPropertyFunc<SerializerAttribute, Type>("Type");
        public static readonly Func<FirstPassWriterAttribute, Type> GetFirstPassWriterAttributeType
            = CreateGetPropertyFunc<FirstPassWriterAttribute, Type>("Type");

        private static Func<T, TProp> CreateGetPropertyFunc<T, TProp>(string propertyName)
        {
            var parameter = Expression.Parameter(typeof(T));

            var property = Expression.Property(parameter, propertyName);

            return Expression.Lambda<Func<T, TProp>>(property, parameter).Compile();
        }
    }
}
