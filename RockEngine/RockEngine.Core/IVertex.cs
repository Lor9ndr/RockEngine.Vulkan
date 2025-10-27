using Silk.NET.Vulkan;

namespace RockEngine.Core
{
    public interface IVertex
    {
        public abstract static VertexInputBindingDescription GetBindingDescription();

        public abstract static VertexInputAttributeDescription[] GetAttributeDescriptions();
    }
}