using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Numerics;

using static RockEngine.Vulkan.ShaderReflectionData;

namespace RockEngine.Core.Rendering.Materials
{

    public class TypeBasedResourceProvider : ITypeBasedResourceProvider
    {
        public Texture GetDefaultTexture(DescriptorSetLayoutBindingReflected binding, VulkanContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(binding);

            // Determine default texture based on descriptor type and binding characteristics
            return binding.DescriptorType switch
            {
                DescriptorType.CombinedImageSampler => CreateDefaultSampledTexture(binding, context),
                DescriptorType.StorageImage => CreateDefaultStorageTexture(binding, context),
                DescriptorType.UniformBuffer => throw new InvalidOperationException("Uniform buffers are not textures"),
                DescriptorType.StorageBuffer => throw new InvalidOperationException("Storage buffers are not textures"),
                _ => Texture2D.GetEmptyTexture(context)
            };
        }

        public object GetDefaultPushConstant(PushConstantInfo pushConstant)
        {
            ArgumentNullException.ThrowIfNull(pushConstant);

            // Determine default value based on size and common type patterns
            return pushConstant.Size switch
            {
                4 => GetDefaultScalarValue(pushConstant),     // float, int, bool
                8 => Vector2.Zero,                           // vec2
                12 => Vector3.Zero,                          // vec3
                16 => Vector4.Zero,                          // vec4
                32 => Matrix3x2.Identity,                    // 3x2 matrix
                64 => Matrix4x4.Identity,                    // 4x4 matrix
                _ => CreateZeroInitializedBuffer(pushConstant.Size)
            };
        }

        private Texture CreateDefaultSampledTexture(DescriptorSetLayoutBindingReflected binding, VulkanContext context)
        {
            // Use descriptor count to determine if it's an array
            if (binding.DescriptorCount > 1)
            {
                // For texture arrays, create an array of empty textures
                return Texture2D.GetEmptyTexture(context);
            }

            if(binding.Name.Contains("albedo", StringComparison.OrdinalIgnoreCase))
            {
                return Texture2D.GetEmptyTexture(context);
            }
            if(binding.Name.Contains("mra", StringComparison.OrdinalIgnoreCase))
            {
                return Texture2D.CreateColorTexture(context, new Silk.NET.Maths.Vector4D<byte>(128, 128,128,255), "mra");
            }
            return Texture2D.GetEmptyTexture(context);
        }

        private Texture CreateDefaultStorageTexture(DescriptorSetLayoutBindingReflected binding, VulkanContext context)
        {
            // For storage images, use a black texture
            return Texture2D.GetEmptyTexture(context);
        }

        private object GetDefaultScalarValue(PushConstantInfo pushConstant)
        {
            // For scalar types, default to 0.0f for floats, 0 for ints, false for bools
            // We'll default to float since it's the most common in graphics
            return 0.0f;
        }

        private static object CreateZeroInitializedBuffer(uint size)
        {
            return new byte[size];
        }
    }
}