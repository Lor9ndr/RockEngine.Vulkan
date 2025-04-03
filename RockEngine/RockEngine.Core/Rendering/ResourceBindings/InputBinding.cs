using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    public class InputAttachmentBinding : ResourceBinding
    {
        public VkImageView[] Attachments { get; }

        public InputAttachmentBinding(uint setLocation, uint bindingLocation, params VkImageView[] attachments)
            : base(setLocation, bindingLocation)
        {
            Attachments = attachments;
        }

        public override unsafe void UpdateDescriptorSet(VulkanContext context)
        {
            var imageInfos = stackalloc DescriptorImageInfo[Attachments.Length];
            var writes = stackalloc WriteDescriptorSet[Attachments.Length];

            for (int i = 0; i < Attachments.Length; i++)
            {
                imageInfos[i] = new DescriptorImageInfo
                {
                    ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                    ImageView = Attachments[i],
                    Sampler = default
                };

                writes[i] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = DescriptorSet,
                    DstBinding = BindingLocation + (uint)i,
                    DstArrayElement = 0,
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType.InputAttachment,
                    PImageInfo = &imageInfos[i]
                };
            }

            VulkanContext.Vk.UpdateDescriptorSets(context.Device, (uint)Attachments.Length, writes, 0, null);
        }
    }
}
