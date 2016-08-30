// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Bond.RuntimeObject.Expressions
{
    using System.Linq.Expressions;
    using System.Reflection;

    internal static class StringExpressionCopy
    {
        static readonly FieldInfo stringEmpty = ReflectionCopy.FieldInfoOf((string s) => string.Empty);

        public static Expression Empty()
        {
            return Expression.Field(null, stringEmpty);
        }
    }
}
