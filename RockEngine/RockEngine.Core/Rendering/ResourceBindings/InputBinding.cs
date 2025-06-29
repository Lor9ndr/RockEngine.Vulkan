using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    public class InputAttachmentBinding : ResourceBinding, IDisposable
    {
        private VkImageView[] _attachments;

        public VkImageView[] Attachments => _attachments;

        protected override DescriptorType DescriptorType =>  DescriptorType.InputAttachment;

        public InputAttachmentBinding(uint setLocation, uint bindingLocation, params VkImageView[] attachments)
            : base(setLocation, bindingLocation)
        {
            _attachments = attachments;
            foreach (var attachment in _attachments)
            {
                attachment.WasUpdated += Attachment_WasUpdated;
            }
        }

        private void Attachment_WasUpdated()
        {
            foreach (var descriptorSet in DescriptorSets)
            {
                descriptorSet.IsDirty = true;
            }
        }

        public override unsafe void UpdateDescriptorSet(VulkanContext context, uint frameIndex)
        {
            var imageInfos = stackalloc DescriptorImageInfo[Attachments.Length];
            var writes = stackalloc WriteDescriptorSet[Attachments.Length];
            var descriptor = DescriptorSets[frameIndex];
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
                    DstBinding = BindingLocation + (uint)i,
                    DstArrayElement = 0,
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType,
                    PImageInfo = &imageInfos[i]
                };
            }

            VulkanContext.Vk.UpdateDescriptorSets(context.Device, (uint)Attachments.Length, writes, 0, null);
        }
        public override int GetResourceHash()
        {
            HashCode hash = new HashCode();
            hash.Add(base.GetResourceHash());
            foreach (var attachments in _attachments)
            {
                hash.Add(attachments.GetHashCode());
            }
            return hash.ToHashCode();
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
    }
}
