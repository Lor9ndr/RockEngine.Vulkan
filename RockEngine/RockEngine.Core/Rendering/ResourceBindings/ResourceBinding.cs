using RockEngine.Core.Internal;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;


namespace RockEngine.Core.Rendering.ResourceBindings
{
    public abstract class ResourceBinding(uint setLocation, UIntRange bindingLocation) : ICloneable, IDisposable
    {
        // Store descriptor sets per layout and per frame
        protected readonly Dictionary<VkDescriptorSetLayout, VkDescriptorSet[]> _descriptorSetsByLayout
            = new Dictionary<VkDescriptorSetLayout, VkDescriptorSet[]>();
        protected bool _isDisposed;

        public uint SetLocation { get; set; } = setLocation;
        public UIntRange BindingLocation { get; } = bindingLocation;
        public abstract DescriptorType DescriptorType { get; }
        public IReadOnlyDictionary<VkDescriptorSetLayout, VkDescriptorSet[]> DescriptorSets => _descriptorSetsByLayout;

        public virtual uint DescriptorCount => 1;

        public VkDescriptorSet GetDescriptorSetForLayout(VkDescriptorSetLayout layout, uint frameIndex)
        {

            if (!_descriptorSetsByLayout.TryGetValue(layout, out var sets))
            {
                sets = new VkDescriptorSet[VulkanContext.GetCurrent().MaxFramesPerFlight];
                _descriptorSetsByLayout[layout] = sets;
            }
            return sets[frameIndex];

        }

        public void SetDescriptorSetForLayout(VkDescriptorSetLayout layout, uint frameIndex, VkDescriptorSet set)
        {
            if (!_descriptorSetsByLayout.TryGetValue(layout, out var sets))
            {
                sets = new VkDescriptorSet[VulkanContext.GetCurrent().MaxFramesPerFlight];
                _descriptorSetsByLayout.TryAdd(layout, sets);
            }

            sets[frameIndex] = set;
        }

        public abstract object Clone();

        public abstract void UpdateDescriptorSet(VulkanContext context, uint frameIndex, VkDescriptorSetLayout layout);

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    // TODO: освободить управляемое состояние (управляемые объекты)
                }

                _descriptorSetsByLayout.Clear();
                _isDisposed = true;
            }
        }

        // // TODO: переопределить метод завершения, только если "Dispose(bool disposing)" содержит код для освобождения неуправляемых ресурсов
        // ~ResourceBinding()
        // {
        //     // Не изменяйте этот код. Разместите код очистки в методе "Dispose(bool disposing)".
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Не изменяйте этот код. Разместите код очистки в методе "Dispose(bool disposing)".
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}