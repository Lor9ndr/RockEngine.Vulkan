using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public interface IDescriptorResource
    {
        public void GetWriteDescriptorSet(in DescriptorSet descriptorSet, out WriteDescriptorSet writeDescriptorSet);
    }
}
