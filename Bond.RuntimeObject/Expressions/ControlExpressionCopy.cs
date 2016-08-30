// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Bond.RuntimeObject.Expressions
{
    using System.Linq.Expressions;

    internal static class ControlExpressionCopy
    {
        public static Expression While(Expression whileCondition, Expression body)
        {
            return While(whileCondition, body, Expression.Label("end"));
        }

        public static Expression While(Expression whileCondition, Expression body, LabelTarget breakLabel)
        {
            return Expression.Loop(
                PrunedExpressionCopy.IfThenElse(
                    whileCondition,
                    body,
                    Expression.Break(breakLabel)),
                breakLabel);
        }
    }
}
