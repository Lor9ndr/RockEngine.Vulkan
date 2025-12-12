using RockEngine.Core.Internal;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    public class TextureBinding : ResourceBinding, IDisposable
    {
        public Texture[] Textures { get; private set; }
        public uint BaseMipLevel { get; private set; }
        public uint LevelCount { get; private set; }

        public override DescriptorType DescriptorType => DescriptorType.CombinedImageSampler;

        public TextureBinding(uint setLocation, uint bindingLocation,
                            uint baseMipLevel = 0, uint levelCount = 1,
                            params Texture[] textures)
            : base(setLocation, new UIntRange(bindingLocation, (uint)(bindingLocation + textures.Length -1)))
        {
            BaseMipLevel = baseMipLevel;
            LevelCount = levelCount;
            Textures = textures;

            foreach (var texture in textures)
            {
                texture.OnTextureUpdated += MarkAsDirty;
            }
        }

        private void MarkAsDirty(Texture _)
        {
            foreach (var item in DescriptorSets)
            {
                if (item is null)
                {
                    continue;
                }

                item.IsDirty = true;
            }
        }

        public override unsafe void UpdateDescriptorSet(VulkanContext context, uint frameIndex)
        {
            WriteDescriptorSet* writeDescriptorSets = stackalloc WriteDescriptorSet[Textures.Length];
            DescriptorImageInfo* imageInfos = stackalloc DescriptorImageInfo[Textures.Length];

            var descriptor = DescriptorSets[frameIndex];
            for (int i = 0; i < Textures.Length; i++)
            {
                writeDescriptorSets[i] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet
                };
                var texture = Textures[i];

                // Get the appropriate image view for the requested mip range
                var imageView = texture.Image.GetMipView(BaseMipLevel);
                var layout = texture.Image.GetMipLayout(BaseMipLevel);



                // Validate layout
                bool needsLayoutTransition = false;
                for (uint layer = 0; layer < texture.Image.ArrayLayers; layer++)
                {
                    layout = texture.Image.GetMipLayout(BaseMipLevel, layer);
                    if (layout != ImageLayout.ShaderReadOnlyOptimal && layout != ImageLayout.General)
                    {
                        needsLayoutTransition = true;
                        break;
                    }
                }

                // Validate layout for ALL layers
                if (needsLayoutTransition)
                {
                    var batch = context.GraphicsSubmitContext.CreateBatch();
                    texture.PrepareForFragmentShader(batch);
                    batch.Submit();
                    
                }

                imageInfos[i] = new DescriptorImageInfo
                {
                    ImageLayout = layout,
                    ImageView = imageView,
                    Sampler = Texture.CreateSampler(context, BaseMipLevel),
                };

                writeDescriptorSets[i] = new WriteDescriptorSet
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

            VulkanContext.Vk.UpdateDescriptorSets(context.Device, (uint)Textures.Length, writeDescriptorSets, 0, null);
        }

        public void Dispose()
        {
            foreach (var texture in Textures)
            {
                texture.OnTextureUpdated -= MarkAsDirty;
            }
            DescriptorSets = Array.Empty<VkDescriptorSet>();
            Textures = Array.Empty<Texture>();
            GC.SuppressFinalize(this);
        }

        public override TextureBinding Clone()
        {
            return new TextureBinding(SetLocation, BindingLocation.Start, BaseMipLevel, LevelCount, (Texture[])Textures.Clone());
        }

        ~TextureBinding()
        {
            Dispose();
        }
    }
}