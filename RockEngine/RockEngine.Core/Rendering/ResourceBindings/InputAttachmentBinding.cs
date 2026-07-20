using RockEngine.Vulkan;
using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    public class InputAttachmentBinding : ResourceBinding, IDisposable
    {
        private VkImageView[] _attachments;
        private readonly List<WeakReference<IDisposable>> _subscriptionTokens = new();

        public VkImageView[] Attachments => _attachments;

        public override DescriptorType DescriptorType => DescriptorType.InputAttachment;

        public InputAttachmentBinding(uint setLocation, uint bindingLocation, params VkImageView[] attachments)
            : base(setLocation, new Internal.UIntRange(bindingLocation, (uint)(bindingLocation + attachments.Length - 1)))
        {
            _attachments = attachments;
            for (int i = 0; i < attachments.Length; i++)
            {
                if (attachments[i] != null)
                {
                    var observer = new AttachmentObserver(this);
                    var token = attachments[i].Subscribe(observer);
                    _subscriptionTokens.Add(new WeakReference<IDisposable>(token));
                }
            }
        }

        private sealed class AttachmentObserver : IResourceObserver
        {
            private readonly InputAttachmentBinding _binding;
            public AttachmentObserver(InputAttachmentBinding binding) => _binding = binding;
            public void OnResourceChanged(ulong resourceId, ResourceChangeType changeType)
            {
                // При любом изменении вью (пересоздание, обновление) помечаем биндинг как грязный
                _binding.MarkDirty();
            }
        }

        private void MarkDirty()
        {
            foreach (var descriptorSetkvp in _descriptorSetsByLayout)
            {
                foreach (var vkDescriptorSet in descriptorSetkvp.Value)
                {
                    vkDescriptorSet?.IsDirty = true;
                }
            }
        }

        public override unsafe void UpdateDescriptorSet(VulkanContext context, uint frameIndex, VkDescriptorSetLayout layout)
        {
            var descriptor = GetDescriptorSetForLayout(layout, frameIndex);
            var imageInfos = stackalloc DescriptorImageInfo[Attachments.Length];
            var writes = stackalloc WriteDescriptorSet[Attachments.Length];
            for (int i = 0; i < Attachments.Length; i++)
            {
                var attachment = Attachments[i];
                imageInfos[i] = new DescriptorImageInfo
                {
                    ImageLayout = attachment.AspectFlags.HasFlag(ImageAspectFlags.DepthBit) ? ImageLayout.DepthStencilReadOnlyOptimal : ImageLayout.ShaderReadOnlyOptimal,
                    ImageView = attachment,
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

            VK.UpdateDescriptorSets(context.Device, (uint)Attachments.Length, writes, 0, null);
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }
            foreach (var token in _subscriptionTokens)
            {
                if (token.TryGetTarget(out var disposable))
                {
                    disposable.Dispose();
                }
            }

            _subscriptionTokens.Clear();
            _attachments = [];
        }

        public override InputAttachmentBinding Clone()
        {
            return new InputAttachmentBinding(SetLocation, BindingLocation.Start, Attachments);
        }
    }
}