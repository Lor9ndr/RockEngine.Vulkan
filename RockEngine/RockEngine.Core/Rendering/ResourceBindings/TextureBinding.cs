using RockEngine.Core.Internal;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    public class TextureBinding : ResourceBinding, IDisposable
    {
        private readonly uint _arrayLayer = 0;
        private readonly uint _layerCount = Vk.RemainingArrayLayers;

        public Texture[] Textures { get; private set; }
        public uint BaseMipLevel { get; }
        public uint LevelCount { get; }
        public override DescriptorType DescriptorType => DescriptorType.CombinedImageSampler;
        public ImageLayout ImageLayout { get; }

        public TextureBinding(
            uint setLocation,
            uint bindingLocation,
            uint baseMipLevel,
            uint levelCount,
            ImageLayout imageLayout,
            uint arrayLayer,
            uint layerCount,
            params Texture[] textures)
            : base(setLocation, new UIntRange(bindingLocation, (uint)(bindingLocation + textures.Length - 1)))
        {
            BaseMipLevel = baseMipLevel;
            LevelCount = levelCount;
            ImageLayout = imageLayout;
            Textures = textures;
            _arrayLayer = arrayLayer;
            _layerCount = layerCount;

            foreach (var texture in textures)
            {
                texture?.OnTextureUpdated += MarkAsDirty;
            }
        }
        public TextureBinding(
            uint setLocation,
            uint bindingLocation,
            uint baseMipLevel,
            uint levelCount,
            ImageLayout imageLayout,
            params Texture[] textures)
            : this(setLocation, bindingLocation, baseMipLevel, levelCount, imageLayout, 0, Vk.RemainingArrayLayers, textures)
        {
        }
        public TextureBinding(
            uint setLocation,
            uint bindingLocation,
            uint baseMipLevel,
            uint levelCount,
            ImageLayout imageLayout,
            Texture texture)
            : this(setLocation, bindingLocation, baseMipLevel, levelCount, imageLayout, 0, texture.Image.ArrayLayers, texture)
        {
        }

        private void MarkAsDirty(object? sender, TextureUpdate e)
        {
            foreach (var setList in _descriptorSetsByLayout.Values)
            {
                foreach (var set in setList)
                {
                    set?.IsDirty = true;
                }
            }
        }

        public override unsafe void UpdateDescriptorSet(VulkanContext context, uint frameIndex, VkDescriptorSetLayout descriptorSetLayout)
        {
            var descriptor = GetDescriptorSetForLayout(descriptorSetLayout, frameIndex);
            WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[Textures.Length];
            DescriptorImageInfo* imageInfos = stackalloc DescriptorImageInfo[Textures.Length];

            for (int i = 0; i < Textures.Length; i++)
            {
                var texture = Textures[i];

                // Obtain view for the required layer range and mip range
                var imageView = texture.Image.GetView(
                    baseMipLevel: BaseMipLevel,
                    levelCount: LevelCount,
                    baseArrayLayer: _arrayLayer ,
                    layerCount: _layerCount
                );

                imageInfos[i] = new DescriptorImageInfo
                {
                    ImageLayout = ImageLayout,
                    ImageView = imageView,
                    Sampler = Texture.CreateSampler(context, BaseMipLevel) 
                };

                writes[i] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = descriptor,
                    DstBinding = (uint)(BindingLocation.Start + i),
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType,
                    DescriptorCount = 1,
                    PImageInfo = &imageInfos[i]
                };
            }

            VulkanContext.Vk.UpdateDescriptorSets(context.Device, (uint)Textures.Length, writes, 0, null);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var texture in Textures)
                {
                    texture?.OnTextureUpdated -= MarkAsDirty;
                }
                Textures = Array.Empty<Texture>();
                _descriptorSetsByLayout.Clear();
            }
        }

        public override TextureBinding Clone()
        {
            return new TextureBinding(
                SetLocation,
                BindingLocation.Start,
                BaseMipLevel,
                LevelCount,
                ImageLayout,
                _arrayLayer,
                _layerCount,
                (Texture[])Textures.Clone()
            );
        }

        ~TextureBinding()
        {
            Dispose(false);
        }
    }
}