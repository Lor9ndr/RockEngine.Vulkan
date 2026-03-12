using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    public class InputAttachmentBinding : ResourceBinding, IDisposable
    {
        private VkImageView[] _attachments;

        public VkImageView[] Attachments => _attachments;

        public override DescriptorType DescriptorType =>  DescriptorType.InputAttachment;

        public InputAttachmentBinding(uint setLocation, uint bindingLocation, params VkImageView[] attachments)
            : base(setLocation, new Internal.UIntRange(bindingLocation, (uint)(bindingLocation + attachments.Length -1)))
        {
            _attachments = attachments;
            foreach (var attachment in _attachments)
            {
                attachment.WasUpdated += Attachment_WasUpdated;
            }
        }

        private void Attachment_WasUpdated()
        {
            foreach (var descriptorSetkvp in _descriptorSetsByLayout)
            {
                foreach(var vkDescriptorSet in descriptorSetkvp.Value)
                {
                    if (vkDescriptorSet is null)
                    {
                        continue;
                    }

                    vkDescriptorSet.IsDirty = true;
                }
            }
        }

        public override unsafe void UpdateDescriptorSet(VulkanContext context, uint frameIndex, VkDescriptorSetLayout layout)
        {
            var descriptor = GetDescriptorSetForLayout(layout,frameIndex);
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
                    DstSet = descriptor,
                    DstBinding = BindingLocation.Start + (uint)i,
                    DstArrayElement = 0,
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType,
                    PImageInfo = &imageInfos[i]
                };
            }

            VulkanContext.Vk.UpdateDescriptorSets(context.Device, (uint)Attachments.Length, writes, 0, null);
        }

        public void Dispose()
        {
            foreach (var item in Attachments)
            {
                item.WasUpdated -= Attachment_WasUpdated;
            }
            _attachments = [];
            GC.SuppressFinalize(this);
        }

        public override InputAttachmentBinding Clone()
        {
            return new InputAttachmentBinding(SetLocation, BindingLocation.Start, Attachments);
        }
    }
}
