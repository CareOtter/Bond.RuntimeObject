namespace Bond.RuntimeObject
{
    using System;
    using System.Linq;
    using System.Threading;
    using Bond.Expressions;
    using Bond.IO;
    using Bond.Protocols;
    using Expressions;

    /// <summary>
    /// Transcoder from protocol reader <typeparamref name="R"/> to protocol writer <typeparamref name="W"/>
    /// </summary>
    /// <typeparam name="R">Protocol reader</typeparam>
    /// <typeparam name="W">Protocol writer</typeparam>
    public class RuntimeTranscoder<R, W>
    {
        static readonly Type helperType;
        readonly RuntimeTranscoderHelper helper;

        static RuntimeTranscoder()
        {
            var firstPassAttribute = typeof(W).GetAttribute<FirstPassWriterAttribute>();
            if (firstPassAttribute != null)
            {
                if (!typeof(ITwoPassProtocolWriter).IsAssignableFrom(typeof(W)))
                {
                    throw new ArgumentException("Writers with FirstPassWriterAttribute must implement ITwoPassProtocolWriter");
                }

                helperType = typeof(TwoPassRuntimeTranscoderHelper<>).MakeGenericType(typeof(R), typeof(W), ReflectionHelper.GetFirstPassWriterAttributeType(firstPassAttribute));
            }
            else
            {
                helperType = typeof(RuntimeTranscoderHelper);
            }
        }

        /// <summary>
        /// Create a transcoder for payloads with specified runtime schema
        /// </summary>
        /// <param name="schema">Payload schema, required for transcoding from untagged protocols</param>
        /// <param name="typeDef"></param>
        public RuntimeTranscoder(RuntimeSchema schema, TypeDef typeDef)
            : this(schema, null, typeDef)
        {}

        /// <summary>
        /// Create a transcoder for payloads with specified compile-time schema
        /// </summary>
        /// <param name="type">Type representing a Bond schema</param>
        /// <param name="typeDef"></param>
        public RuntimeTranscoder(Type type, TypeDef typeDef)
            : this(type, null, typeDef)
        {}

        /// <summary>
        /// Create a transcoder for payloads with specified runtime schema
        /// </summary>
        /// <param name="schema">Payload schema, required for transcoding from untagged protocols</param>
        /// <param name="parser">Custom <see cref="IParser"/> instance</param>
        /// <param name="typeDef"></param>
        public RuntimeTranscoder(RuntimeSchema schema, IParser parser, TypeDef typeDef)
        {
            helper = (RuntimeTranscoderHelper)Activator.CreateInstance(helperType, schema, parser, typeDef);
        }

        /// <summary>
        /// Create a transcoder for payloads with specified compile-time schema
        /// </summary>
        /// <param name="type">Type representing a Bond schema</param>
        /// <param name="parser">Custom <see cref="IParser"/> instance</param>
        /// <param name="typeDef"></param>
        public RuntimeTranscoder(Type type, IParser parser, TypeDef typeDef)
        {
            helper = (RuntimeTranscoderHelper)Activator.CreateInstance(helperType, type, parser, typeDef);
        }

        /// <summary>
        /// Transcode payload
        /// </summary>
        /// <param name="reader">Reader instance representing source payload</param>
        /// <param name="writer">Writer instance</param>
        public void Transcode(R reader, W writer)
        {
            helper.Transcode(reader, writer);
        }

        class RuntimeTranscoderHelper
        {
            readonly Action<R, W>[] transcode;

            public RuntimeTranscoderHelper(RuntimeSchema schema, IParser parser, TypeDef typeDef)
            {
                transcode = Generate(schema, parser, typeDef);
            }

            public virtual void Transcode(R reader, W writer)
            {
                transcode[0](reader, writer);
            }

            Action<R, W>[] Generate(RuntimeSchema schema, IParser parser, TypeDef typeDef)
            {
                return RuntimeSerializerGeneratorFactory<R, W>.Create(
                        (r, w, i) => transcode[i](r, w), schema, typeDef)
                    .Generate(parser)
                    .Select(lambda => lambda.Compile()).ToArray();
            }
        }

        class TwoPassRuntimeTranscoderHelper<FPW> : RuntimeTranscoderHelper
        {
            readonly Lazy<Action<R, FPW>[]> firstPassTranscode;

            public TwoPassRuntimeTranscoderHelper(RuntimeSchema schema, IParser parser, TypeDef typeDef):
                base(schema, parser, typeDef)
            {
                firstPassTranscode = new Lazy<Action<R, FPW>[]>(() => GenerateFirstPass(schema, parser, typeDef), LazyThreadSafetyMode.PublicationOnly);
            }

            public override void Transcode(R reader, W writer)
            {
                var firstPassWriter = ((ITwoPassProtocolWriter)writer).GetFirstPassWriter();
                if (firstPassWriter != null)
                {
                    if (!typeof(ICloneable<R>).IsAssignableFrom(typeof(R)))
                    {
                        throw new ArgumentException("Two-pass transcoding requires a reader that implements ICloneable");
                    }

                    R clonedReader = ((ICloneable<R>)reader).Clone();
                    firstPassTranscode.Value[0](clonedReader, (FPW)firstPassWriter);
                }

                base.Transcode(reader, writer);
            }

            Action<R, FPW>[] GenerateFirstPass(RuntimeSchema schema, IParser parser, TypeDef typeDef)
            {
                parser = parser ?? ParserFactory<R>.Create(schema);
                return RuntimeSerializerGeneratorFactory<R, FPW>.Create(
                        (r, w, i) => firstPassTranscode.Value[i](r, w), schema, typeDef)
                    .Generate(parser)
                    .Select(lambda => lambda.Compile()).ToArray();
            }
        }
    }
}
