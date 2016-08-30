namespace Bond.RuntimeObject.Expressions
{
    using System;
    using System.Collections.Generic;
    using System.Linq.Expressions;

    internal static class RuntimeDataExpression
    {
        public static IndexExpression RuntimePropertySetter(Expression expression, string fieldName)
        {
            var property = Expression.Property(
                Expression.Property(
                    Expression.Convert(expression, typeof(RuntimeObject)),
                    "Properties"),
                "Item",
                Expression.Constant(fieldName));

            return property;
        }

        public static UnaryExpression RuntimePropertyGetter(Expression expression, string fieldName, Type fieldType)
        {
            var property = Expression.Property(
                Expression.Property(
                    Expression.Convert(expression, typeof(RuntimeObject)),
                    "Properties"),
                "Item",
                Expression.Constant(fieldName));

            return Expression.Convert(property, fieldType);
        }
    }
}
