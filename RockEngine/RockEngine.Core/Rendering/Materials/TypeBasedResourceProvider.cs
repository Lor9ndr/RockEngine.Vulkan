using System.Numerics;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;
using static RockEngine.Vulkan.ShaderReflectionData;

namespace RockEngine.Core.Rendering.Materials
{
    public class TypeBasedResourceProvider : ITypeBasedResourceProvider
    {
        private readonly VulkanContext _context;

        public TypeBasedResourceProvider(VulkanContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        
        public Texture GetDefaultTexture(BindingInfo binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            return binding.Reflection.DescriptorType switch
            {
                DescriptorType.CombinedImageSampler => CreateDefaultSampledTexture(binding),
                DescriptorType.StorageImage => CreateDefaultStorageTexture(binding),
                _ => Texture2D.GetEmptyTexture(_context)
            };
        }

        public object GetDefaultPushConstant(PushConstantInfo pushConstant)
        {
            ArgumentNullException.ThrowIfNull(pushConstant);
            return pushConstant.Size switch
            {
                4 => 0.0f,
                8 => Vector2.Zero,
                12 => Vector3.Zero,
                16 => Vector4.Zero,
                32 => Matrix3x2.Identity,
                64 => Matrix4x4.Identity,
                _ => new byte[pushConstant.Size]
            };
        }

        
        private Texture CreateDefaultSampledTexture(BindingInfo binding)
        {
            if (binding.Reflection.DescriptorCount > 1)
            {
                return Texture2D.GetEmptyTexture(_context);
            }

            if (binding.Image.HasValue && binding.Image.Value.Dimension == Silk.NET.SPIRV.Dim.DimCube)
            {
                return Texture3D.GetDefaultCubemapTexture(_context);
            }

            var name = binding.Reflection.Name;
            if (name.Contains("albedo", StringComparison.OrdinalIgnoreCase))
            {
                return Texture2D.GetEmptyTexture(_context);
            }

            return name.Contains("mra", StringComparison.OrdinalIgnoreCase)
                ? Texture2D.CreateColorTexture(_context, new Silk.NET.Maths.Vector4D<byte>(128, 128, 128, 255), "mra")
                : Texture2D.GetEmptyTexture(_context);
        }

        
        private Texture CreateDefaultStorageTexture(BindingInfo binding)
        {
            return Texture2D.GetEmptyTexture(_context);
        }

        
        public object? GetDefaultResource(BindingInfo info)
        {
            return info.Reflection.DescriptorType switch
            {
                DescriptorType.CombinedImageSampler => CreateDefaultSampledTexture(info),
                DescriptorType.UniformBuffer => new UniformBuffer(_context, 1),
                DescriptorType.StorageBuffer => new StorageBuffer<byte>(_context, 1),
                DescriptorType.UniformBufferDynamic => new UniformBuffer(_context, 1, true),
                _ => throw new NotImplementedException(),
            };
        }
    }
}