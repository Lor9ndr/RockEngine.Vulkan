using Silk.NET.Vulkan;

namespace RockEngine.Core.Extensions
{
    internal static class DescriptorTypeExtensions
    {
        public static bool IsTextureDescriptorType(this DescriptorType type)
        {
            return type is DescriptorType.CombinedImageSampler
               or DescriptorType.SampledImage
               or DescriptorType.StorageImage;
        }
        public static bool IsTextureDescriptorType(this Silk.NET.SPIRV.Reflect.DescriptorType type)
        {
            return type is Silk.NET.SPIRV.Reflect.DescriptorType.CombinedImageSampler
               or Silk.NET.SPIRV.Reflect.DescriptorType.SampledImage
               or Silk.NET.SPIRV.Reflect.DescriptorType.StorageImage;
        }
    }
}
