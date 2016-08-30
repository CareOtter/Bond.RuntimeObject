// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Bond.RuntimeObject
{
    using System;
    using System.Runtime.CompilerServices;

    internal static class AuditCopy
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void FailArgNull(string paramName)
        {
            throw new ArgumentNullException(paramName);
        }

        public static void ArgNotNull(object value, string paramName)
        {
            if (value == null)
            {
                FailArgNull(paramName);
            }
        }
    }
}
