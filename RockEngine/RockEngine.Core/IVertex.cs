using RockEngine.Assets;

using Silk.NET.Vulkan;

namespace RockEngine.Core
{
    public interface IVertex:IPolymorphicSerializable
    {
        public abstract static VertexInputBindingDescription GetBindingDescription();

        public abstract static VertexInputAttributeDescription[] GetAttributeDescriptions();
    }
}