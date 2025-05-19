
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Internal;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Managers
{
    public partial class BindingManager
    {
        private readonly VulkanContext _context;
        private readonly DescriptorPoolManager _descriptorPoolManager;
        private readonly GraphicsEngine _graphicsEngine;

        public BindingManager(VulkanContext context, DescriptorPoolManager descriptorPool, GraphicsEngine graphicsEngine)
        {
            _context = context;
            _descriptorPoolManager = descriptorPool;
            _graphicsEngine = graphicsEngine;
        }

        public void BindResourcesForMaterial(
             Material material,
             VkCommandBuffer commandBuffer,
             bool isCompute = false,
             Span<uint> skipSets = default)
        {
            var setsToBind = new DescriptorSet[material.Bindings.Count];
            int index = 0;

            foreach(var(setLocation, perSetBindings) in material.Bindings)
            {
                if (skipSets.Contains(setLocation) || material.Pipeline.Layout.GetSetLayout(setLocation) == default)
                {
                    continue;
                }
                ProcessSet(material.Pipeline.Layout, setLocation, perSetBindings, setsToBind, ref index);
            }
            if (index == 0)
            {
                return;
            }
            BindDescriptorSetsToCommandBuffer(
                commandBuffer,
                material.Pipeline.Layout,
                setsToBind,
                CollectionsMarshal.AsSpan(material.Bindings.DynamicOffsets),
                material.Bindings.MinSetLocation,
                isCompute
            );
        }
        public void BindResource(
          ResourceBinding binding,
          VkCommandBuffer commandBuffer,
          VkPipelineLayout pipelineLayout,
          bool isCompute = false)
        {
            // 1. Проверка существования сета в pipeline layout
            var setLocation = binding.SetLocation;
            var setLayout = pipelineLayout.GetSetLayout(setLocation);
            if (setLayout == default)
            {
                throw new InvalidOperationException(
                    $"Set {setLocation} not found in pipeline layout");
            }

            // 2. Создаем временную коллекцию и собираем смещения
            var perSetBindings = new PerSetBindings(setLocation);
            var dynamicOffsets = new List<uint>();

            perSetBindings.Add(binding);
            if (binding is UniformBufferBinding uboBinding && uboBinding.Buffer.IsDynamic)
            {
                dynamicOffsets.Add((uint)uboBinding.Offset);
            }

            // 3. Получаем или создаем дескрипторный набор
            var descriptorSet = GetOrCreateDescriptorSet(pipelineLayout, setLocation, perSetBindings);

            // 4. Биндим с учетом динамических смещений
            BindDescriptorSetsToCommandBuffer(commandBuffer, pipelineLayout, [descriptorSet], CollectionsMarshal.AsSpan(dynamicOffsets), perSetBindings.Set, isCompute);
          
        }

        private void ProcessSet(VkPipelineLayout pipelineLayout, uint setLocation, PerSetBindings perSetBindings, DescriptorSet[] setsToBind, ref int index)
        {
            // Get or allocate descriptor set
            var descriptorSet = GetOrCreateDescriptorSet(pipelineLayout, setLocation, perSetBindings);

            setsToBind[index++] = descriptorSet;
        }

        private VkDescriptorSet GetOrCreateDescriptorSet(VkPipelineLayout pipelineLayout, uint setLocation, PerSetBindings perSetBindings)
        {
            // Проверяем, есть ли актуальный набор дескрипторов
            var existingSet = perSetBindings.FirstOrDefault(b => b.DescriptorSet != null)?.DescriptorSet;
            bool needsUpdate = perSetBindings.NeedToUpdate;

            if (existingSet is not null && !needsUpdate)
            {
                return existingSet;
            }

            // Выделяем новый набор или обновляем существующий
            var setLayout = pipelineLayout.GetSetLayout(setLocation);
            var descriptorSet = existingSet ?? _descriptorPoolManager.AllocateDescriptorSet(setLayout);

            foreach (var binding in perSetBindings)
            {
                binding.DescriptorSet = descriptorSet;
                if (binding.IsDirty)
                {
                    binding.UpdateDescriptorSet(_context);
                    binding.IsDirty = false;
                }
            }
            return descriptorSet;
        }

        private BindingFingerprint CreateFingerprint(uint setLocation, PerSetBindings bindings)
        {
            var hash = new HashCode();
            foreach (var binding in bindings.OrderBy(b => b.BindingLocation))
            {
                hash.Add(binding.GetResourceHash());
                if (binding is UniformBufferBinding ubo && ubo.Buffer.IsDynamic)
                {
                    hash.Add(ubo.Offset);
                }
            }
            return new BindingFingerprint(setLocation, hash.ToHashCode());
        }


        private void ProcessBinding(
            ResourceBinding binding,
            VkPipelineLayout pipelineLayout,
            DescriptorSet[] descriptorSets,
            ref int index)
        {
            if (binding is UniformBufferBinding uboBinding)
            {
                HandleUniformBufferBinding(uboBinding, pipelineLayout);
            }

            if (binding.DescriptorSet is null)
            {
                AllocateAndUpdateDescriptorSet(binding, pipelineLayout);
            }

            descriptorSets[index++] = binding.DescriptorSet!;
        }



        private static void HandleUniformBufferBinding(UniformBufferBinding uboBinding, VkPipelineLayout pipelineLayout)
        {
            // Check if the descriptor set is already cached for this pipeline layout
            if (!uboBinding.Buffer.DescriptorSets.TryGetValue(pipelineLayout, out var cachedDescriptorSet) || cachedDescriptorSet is not null)
            {
                // If not, cache it
                uboBinding.Buffer.DescriptorSets[pipelineLayout] = uboBinding.DescriptorSet;
            }
            else
            {
                // If it is, reuse the cached descriptor set
                uboBinding.DescriptorSet = cachedDescriptorSet;
            }
        }

        public void AllocateAndUpdateDescriptorSet(ResourceBinding binding, VkPipelineLayout pipelineLayout)
        {
            var setLayout = pipelineLayout.GetSetLayout(binding.SetLocation);
            var set = _descriptorPoolManager.AllocateDescriptorSet(setLayout);
            binding.DescriptorSet = set;
            binding.UpdateDescriptorSet(_context);
        }

        private unsafe void BindDescriptorSetsToCommandBuffer(
                VkCommandBuffer commandBuffer,
                VkPipelineLayout pipelineLayout,
                Span<DescriptorSet> descriptorSets,
                Span<uint> dynamicOffsets,
                uint minSetIndex,
                bool isCompute)
        {
            fixed (DescriptorSet* descriptorSetsPtr = descriptorSets)
            fixed (uint* dynamicOffsetsPtr = dynamicOffsets)
            {
                VulkanContext.Vk.CmdBindDescriptorSets(
                    commandBuffer,
                    isCompute ? PipelineBindPoint.Compute : PipelineBindPoint.Graphics,
                    pipelineLayout,
                    minSetIndex,
                    (uint)descriptorSets.Length,
                    descriptorSetsPtr,
                    (uint)dynamicOffsets.Length,
                    dynamicOffsetsPtr);
            }
        }

    }
}
