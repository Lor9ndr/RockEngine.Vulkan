using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Net.Mail;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    internal class StorageImageBinding : ResourceBinding
    {
        private readonly Texture[] _textures;
        private readonly uint _mipLevel;

        // Конструктор для массива текстур
        public StorageImageBinding(Texture[] textures, uint setLocation, uint bindingLocation)
            : base(setLocation, new Internal.UIntRange(bindingLocation, (uint)(bindingLocation + textures.Length - 1)))
        {
            _textures = textures;
            _mipLevel = 0;
        }

        // Новый конструктор для одиночной текстуры с уровнем mip
        public StorageImageBinding(Texture texture, uint setLocation, uint bindingLocation, uint mipLevel = 0)
            : base(setLocation, new Internal.UIntRange(bindingLocation, bindingLocation))
        {
            _textures = new[] { texture };
            _mipLevel = mipLevel;
        }

        public override DescriptorType DescriptorType => DescriptorType.StorageImage;

        public unsafe override void UpdateDescriptorSet(VulkanContext context, uint frameIndex)
        {
            var imageInfos = stackalloc DescriptorImageInfo[_textures.Length];
            var writeDescriptorSets = stackalloc WriteDescriptorSet[_textures.Length];
            var descriptor = DescriptorSets[frameIndex];
            for (int i = 0; i < _textures.Length; i++)
            {
                var texture = _textures[i];
                if (texture.Image.GetMipLayout(_mipLevel) != ImageLayout.General)
                {
                    throw new InvalidOperationException("Image must be in General layout");
                }

                imageInfos[i] = new DescriptorImageInfo
                {
                    ImageView = texture.Image.GetMipView(_mipLevel), 
                    ImageLayout = ImageLayout.General,
                    Sampler = default
                };

                writeDescriptorSets[i] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = DescriptorSets[frameIndex],
                    DstBinding = BindingLocation.Start + (uint)i,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType,
                    DescriptorCount = 1,
                    PImageInfo = &imageInfos[i]
                };
            }

            VulkanContext.Vk.UpdateDescriptorSets(
                context.Device,
                (uint)_textures.Length,
                writeDescriptorSets,
                0,
                null
            );
        }
        public override StorageImageBinding Clone()
        {
            if(_textures.Length > 1)
            {
                return new StorageImageBinding((Texture[])_textures.Clone(), SetLocation, BindingLocation.Start);
            }
            else
            {
                return new StorageImageBinding(_textures[0], SetLocation, BindingLocation.Start, _mipLevel);
            }
        }

    }
}