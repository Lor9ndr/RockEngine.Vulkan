using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="setLocation">Descripto Set location</param>
    /// <param name="bindingLocation">Starting index</param>
    /// <param name="textures">textures</param>
    public class TextureBinding(uint setLocation, uint bindingLocation, params Texture[] textures) 
        : ResourceBinding(setLocation, bindingLocation)
    {
        public Texture[] Textures { get; } = textures;

        public unsafe override void UpdateDescriptorSet(RenderingContext renderingContext)
        {
            WriteDescriptorSet* writeDescriptorSets = stackalloc WriteDescriptorSet[Textures.Length];
            DescriptorImageInfo* imageInfos = stackalloc DescriptorImageInfo[Textures.Length];

            for (int i = 0; i < Textures.Length; i++)
            {
                var item = Textures[i];
                imageInfos[i] = new DescriptorImageInfo
                {
                    ImageLayout = item.Image.CurrentLayout,
                    ImageView = item.ImageView,
                    Sampler = item.Sampler,
                };

                writeDescriptorSets[i] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = DescriptorSet,
                    DstBinding = (uint)(BindingLocation + i),
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &imageInfos[i]
                };
            }

            RenderingContext.Vk.UpdateDescriptorSets(renderingContext.Device, (uint)Textures.Length, writeDescriptorSets, 0, null);
            IsDirty = false;
        }
    }
}
