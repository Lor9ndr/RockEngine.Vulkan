using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    public class InputAttachmentBinding : ResourceBinding, IDisposable
    {
        private VkImageView[]? _attachments;

        public VkImageView[]? Attachments => _attachments;

        public InputAttachmentBinding(uint setLocation, uint bindingLocation, params VkImageView[] attachments)
            : base(setLocation, bindingLocation)
        {
            _attachments = attachments;
            foreach (var attachment in Attachments)
            {
                attachment.WasUpdated += Attachment_WasUpdated;
            }
        }

        private void Attachment_WasUpdated()
        {
            IsDirty = true;
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
            IsDirty = false;
        }

        public void Dispose()
        {
            foreach (var item in Attachments)
            {
                item.WasUpdated -= Attachment_WasUpdated;
            }
            _attachments = null;
            GC.SuppressFinalize(this);
        }
    }
}
