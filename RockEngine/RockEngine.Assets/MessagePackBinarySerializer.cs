using MessagePack;
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
   MessagePack.Unity.UnityResolver.Instance,
   MessagePack.Unity.Extension.UnityBlitWithPrimitiveArrayResolver.Instance,
   MessagePack.Resolvers.StandardResolver.Instance,
   NativeGuidResolver.Instance,
   PrimitiveObjectResolver.Instance,
   DynamicObjectResolverAllowPrivate.Instance,
   PolymorphicResolver.Instance
   


);

            var options = MessagePackSerializerOptions.Standard.WithResolver(StaticCompositeResolver.Instance);
            MessagePackSerializer.DefaultOptions = options;

        }

        public async Task SerializeAsync<T>(T data, Stream stream)
        {
            await MessagePackSerializer.SerializeAsync(stream, data);
        }

        public async Task<object> DeserializeAsync(Stream stream, Type type)
        {
            return await MessagePackSerializer.DeserializeAsync(type, stream);
        }

        public async Task SerializeAsync(object data, Type type, Stream stream)
        {
            await MessagePackSerializer.SerializeAsync(type, stream, data);
        }
    }
}
