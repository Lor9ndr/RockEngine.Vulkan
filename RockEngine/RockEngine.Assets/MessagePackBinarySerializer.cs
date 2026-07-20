/*using MessagePack;
using MessagePack.Resolvers;

namespace RockEngine.Assets
{
    /// <summary>
    /// Example binary serializer using MessagePack
    /// </summary>
    public class MessagePackBinarySerializer : IBinarySerializer
    {

        public MessagePackBinarySerializer()
        {
            StaticCompositeResolver.Instance.Register(
                   StandardResolver.Instance,
                   NativeGuidResolver.Instance,*//**//*
                   PrimitiveObjectResolver.Instance,
                   DynamicObjectResolverAllowPrivate.Instance,
                   PolymorphicResolver.Instance,
                   TypelessContractlessStandardResolver.Instance
                );

            var options = MessagePackSerializerOptions.Standard.WithResolver(StaticCompositeResolver.Instance);
            MessagePackSerializer.DefaultOptions = options;

        }

        public async Task SerializeAsync<T>(T data, Stream stream)
        {
            await MessagePackSerializer.SerializeAsync(stream, data).ConfigureAwait(false);
        }

        public async Task<object> DeserializeAsync(Stream stream, Type type)
        {
            return await MessagePackSerializer.DeserializeAsync(type, stream).ConfigureAwait(false);
        }

        public async Task SerializeAsync(object data, Type type, Stream stream)
        {
            await MessagePackSerializer.SerializeAsync(type, stream, data).ConfigureAwait(false);
        }
    }
}
*/