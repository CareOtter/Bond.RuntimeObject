// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Bond.RuntimeObject.Expressions
{
    using System;
    using System.IO;
    using System.Linq.Expressions;
    using System.Runtime.CompilerServices;

    internal static class ThrowExpressionCopy
    {
        static readonly Expression<Action<BondDataType, BondDataType>> throwInvalidTypeException2 =
            (e, a) => ThrowInvalidTypeException(e, a);
        static readonly Expression<Action<string, string>> throwRequiredFieldMissingException =
            (s, f) => ThrowRequiredFieldMissingException(s, f);

        public static Expression InvalidTypeException(Expression expectedType, Expression actualType)
        {
            return Expression.Invoke(throwInvalidTypeException2, expectedType, actualType);
        }

        public static Expression RequiredFieldMissingException(string schema, Expression field)
        {
            return Expression.Invoke(throwRequiredFieldMissingException, Expression.Constant(schema), field);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ThrowInvalidTypeException(BondDataType expectedType, BondDataType actualType)
        {
            throw new InvalidDataException(string.Format("Invalid type {0}, expected {1}", actualType, expectedType));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ThrowRequiredFieldMissingException(string schema, string field)
        {
            throw new InvalidDataException(string.Format("Required field {0}.{1} missing", schema, field));
        }
    }
}
