using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    public class TextureBinding : ResourceBinding, IDisposable
    {
        private readonly ImageLayout _imageLayout;
        public Texture[] Textures { get; private set; }

        protected override DescriptorType DescriptorType => DescriptorType.CombinedImageSampler;

        public TextureBinding(uint setLocation, uint bindingLocation, ImageLayout imageLayout = default, params Texture[] textures) : base(setLocation, bindingLocation)
        {
            _imageLayout = imageLayout;
            Textures = textures;
            foreach (var texture in textures)
            {
                texture.OnTextureUpdated += MarkAsDirty;
            }
        }

        private void MarkAsDirty(Texture _)
        {
            IsDirty = true;
        }


        public override unsafe void UpdateDescriptorSet(VulkanContext renderingContext)
        {
            WriteDescriptorSet* writeDescriptorSets = stackalloc WriteDescriptorSet[Textures.Length];
            DescriptorImageInfo* imageInfos = stackalloc DescriptorImageInfo[Textures.Length];

            for (int i = 0; i < Textures.Length; i++)
            {
                var item = Textures[i];
                if (item is StreamableTexture streamableTexture)
                {
                    imageInfos[i] = new DescriptorImageInfo
                    {
                        ImageLayout = item.Image.GetMipLayout(streamableTexture.LoadedMipLevels),
                        ImageView = item.ImageView,
                        Sampler = item.Sampler,
                    };
                }
                else
                {
                    imageInfos[i] = new DescriptorImageInfo
                    {
                        ImageLayout =  _imageLayout == default ? item.Image.GetMipLayout(0) : _imageLayout,
                        ImageView = item.ImageView,
                        Sampler = item.Sampler,
                    };
                    if (imageInfos[i].ImageLayout != ImageLayout.ShaderReadOnlyOptimal && imageInfos[i].ImageLayout != ImageLayout.General)
                    {
                        throw new InvalidOperationException("Invalid layout");
                    }

                }
                writeDescriptorSets[i] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = DescriptorSet,
                    DstBinding = (uint)(BindingLocation + i),
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType,
                    DescriptorCount = 1,
                    PImageInfo = &imageInfos[i]
                };
            }

            VulkanContext.Vk.UpdateDescriptorSets(renderingContext.Device, (uint)Textures.Length, writeDescriptorSets, 0, null);
            IsDirty = false;
        }
        public override int GetResourceHash()
        {
            HashCode hash = new HashCode();
            hash.Add(base.GetResourceHash());
            foreach (var textures in Textures)
            {
                hash.Add(textures.GetHashCode());
            }
            return hash.ToHashCode();
        }

        public void Dispose()
        {
            foreach (var texture in Textures)
            {
                texture.OnTextureUpdated -= MarkAsDirty;
            }
            Textures = Array.Empty<Texture>();
            GC.SuppressFinalize(this);
        }
        ~TextureBinding()
        {
            Dispose();

        }
    }
}
