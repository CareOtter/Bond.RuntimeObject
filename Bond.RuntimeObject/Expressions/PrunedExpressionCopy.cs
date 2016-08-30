// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Bond.RuntimeObject.Expressions
{
    using System;
    using System.Linq.Expressions;

    internal static class PrunedExpressionCopy
    {
        public static Expression IfThenElse(Expression test, Expression ifTrue, Expression ifFalse)
        {
            if (test.IsEqual(true)) return ifTrue;
            if (test.IsEqual(false)) return ifFalse;
            return Expression.IfThenElse(test, ifTrue, ifFalse);
        }

        static bool IsEqual<T>(this Expression ex, T value)
        {
            if (ex is BinaryExpression)
                return (ex as BinaryExpression).IsEqual(value);

            if (ex is ConstantExpression)
                return (ex as ConstantExpression).IsEqual(value);

            return false;
        }

        static bool IsEqual<T>(this ConstantExpression ex, T value)
        {
            return ex.Value.Equals(value);
        }

        static bool IsEqual<T>(this BinaryExpression ex, T value)
        {
            if (!(ex.Left is ConstantExpression && ex.Right is ConstantExpression)) return false;

            if (ex.NodeType == ExpressionType.Equal)
                return value.Equals(AreEqual(ex.Left as ConstantExpression, ex.Right));

            if (ex.NodeType == ExpressionType.NotEqual)
                return value.Equals(!AreEqual(ex.Left as ConstantExpression, ex.Right));

            return false;
        }

        static bool AreEqual(ConstantExpression left, Expression right)
        {
            return right.IsEqual(left.Value);
        }

        public static Expression Convert(Expression value, Type type)
        {
            if (type == typeof(Tag.wstring))
                type = typeof(string);

            if (value.Type == type)
                return value;

            return Expression.Convert(value, type);
        }
    }
}
