// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Bond.RuntimeObject
{
    using System;
    using IO.Safe;
    using Protocols;

    internal static class MarshalerCopy
    {
        /// <summary>
        /// Marshal object of type T using protocol writer W and a specific Serializer instance.
        /// </summary>
        /// <typeparam name="W">Protocol writer</typeparam>
        /// <param name="serializer">Serializer instance</param>
        /// <param name="writer">Writer instance</param>
        /// <param name="obj">Object to be marshaled</param>
        public static void Marshal<W>(this Serializer<W> serializer, object obj, W writer)
            where W : IProtocolWriter
        {
            writer.WriteVersion();
            serializer.Serialize(obj, writer);
        }

        internal static ArraySegment<byte> Marshal(IBonded bonded)
        {
            var output = new OutputBuffer(4096);
            var writer = new CompactBinaryWriter<OutputBuffer>(output);

            writer.WriteVersion();
            bonded.Serialize(writer);
            return output.Data;
        }
    }
}
