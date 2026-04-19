using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RockEngine.Core.Internal;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    public sealed class GlobalTextureArrayBinding : ResourceBinding
    {
        private struct TextureSlot
        {
            public Texture Texture;
            public IDisposable Subscription;
            public readonly bool IsValid => Texture != null && !Texture.IsDisposed;
        }

        private readonly VulkanContext _context;
        private readonly List<TextureSlot> _slots = new();
        private readonly ConcurrentStack<uint> _freeIndices = new();
        private uint _nextIndex = 0;

        // Конструктор, который требует ваш код
        public GlobalTextureArrayBinding(uint setLocation, uint bindingLocation, VulkanContext context)
            : base(setLocation, new UIntRange(bindingLocation, bindingLocation)) // один binding, который будет массивом
        {
            _context = context;
        }

        public override DescriptorType DescriptorType => DescriptorType.CombinedImageSampler;

        public override uint DescriptorCount => 1024;

        public uint Allocate(Texture texture)
        {
            if (!_freeIndices.TryPop(out uint index))
            {
                index = Interlocked.Increment(ref _nextIndex) - 1;
                if (index >= DescriptorCount)
                {
                    // Revert increment (best effort) and throw
                    Interlocked.Decrement(ref _nextIndex);
                    throw new InvalidOperationException(
                        $"Global texture array binding {BindingLocation.Start} is full. " +
                        $"Maximum {DescriptorCount} textures already allocated.");
                }
            }

            // Ensure _slots list is large enough
            while (_slots.Count <= index)
            {
                _slots.Add(default);
            }

            var subscription = texture.Subscribe(new TextureObserver(this, index));
            _slots[(int)index] = new TextureSlot { Texture = texture, Subscription = subscription };
            MarkDirty();
            return index;
        }

        public void Free(uint index)
        {
            if (index >= _slots.Count)
            {
                return;
            }

            var span = CollectionsMarshal.AsSpan(_slots);
            ref var slot = ref span[(int)index];

            // Guard against double free
            if (slot.Texture == null && slot.Subscription == null)
            {
                return;
            }

            slot.Subscription?.Dispose();
            slot = default;

            _freeIndices.Push(index);
            MarkDirty();
        }

        private void MarkDirty()
        {
            foreach (var sets in _descriptorSetsByLayout.Values)
            {
                foreach (var set in sets)
                {
                    set?.IsDirty = true;
                }
            }
        }

        private class TextureObserver : IResourceObserver
        {
            private readonly GlobalTextureArrayBinding _binding;
            private readonly uint _index;
            public TextureObserver(GlobalTextureArrayBinding binding, uint index) => (_binding, _index) = (binding, index);
            public void OnResourceChanged(ulong resourceId, ResourceChangeType changeType)
            {
                switch (changeType)
                {
                    case ResourceChangeType.Disposed:
                        _binding.Free(_index);
                        break;
                    default:
                        _binding.MarkDirty();
                        break;
                }
            }
        }

        public override unsafe void UpdateDescriptorSet(VulkanContext context, uint frameIndex, VkDescriptorSetLayout layout)
        {
            // Count valid slots within the descriptor count limit
            int validCount = 0;
            int limit = Math.Min(_slots.Count, (int)DescriptorCount);
            for (int i = 0; i < limit; i++)
            {
                if (_slots[i].IsValid)
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                return;
            }

            var writes = ArrayPool<WriteDescriptorSet>.Shared.Rent(validCount);
            var imageInfos = ArrayPool<DescriptorImageInfo>.Shared.Rent(validCount);

            try
            {
                // Fill imageInfos first
                int idx = 0;
                for (uint i = 0; i < limit; i++)
                {
                    if (!_slots[(int)i].IsValid)
                    {
                        continue;
                    }

                    imageInfos[idx] = _slots[(int)i].Texture.GetDescriptorInfo();
                    imageInfos[idx].ImageLayout = ImageLayout.ShaderReadOnlyOptimal;
                    idx++;
                }

                // Pin both arrays and write
                fixed (WriteDescriptorSet* pWrites = writes)
                fixed (DescriptorImageInfo* pImageInfos = imageInfos)
                {
                    idx = 0;
                    for (uint i = 0; i < limit; i++)
                    {
                        if (!_slots[(int)i].IsValid)
                        {
                            continue;
                        }

                        writes[idx] = new WriteDescriptorSet
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = GetDescriptorSetForLayout(layout, frameIndex),
                            DstBinding = BindingLocation.Start,
                            DstArrayElement = i,
                            DescriptorCount = 1,
                            DescriptorType = DescriptorType,
                            PImageInfo = pImageInfos + idx
                        };
                        idx++;
                    }

                    VulkanContext.Vk.UpdateDescriptorSets(context.Device, (uint)validCount, pWrites, 0, null);
                }
            }
            finally
            {
                ArrayPool<WriteDescriptorSet>.Shared.Return(writes);
                ArrayPool<DescriptorImageInfo>.Shared.Return(imageInfos);
            }
        }

        public override object Clone()
        {
            return new GlobalTextureArrayBinding(SetLocation, BindingLocation.Start, _context);
        }
    }
}