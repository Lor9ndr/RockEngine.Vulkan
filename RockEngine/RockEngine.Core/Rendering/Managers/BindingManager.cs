
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public class BindingManager
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

        public void BindResourcesForMaterial(Material material, VkCommandBuffer commandBuffer)
        {
            var setsToBind = new DescriptorSet[material.Bindings.Count];
            int index = 0;

            foreach (var (setLocation, perSetBindings) in material.Bindings)
            {
                ProcessSet(material.Pipeline.Layout, setLocation, perSetBindings, setsToBind, ref index);
            }

            BindDescriptorSetsToCommandBuffer(
                commandBuffer,
                material.Pipeline.Layout,
                setsToBind,
                material.Bindings.DynamicOffsets,
                material.Bindings.MinSetLocation
            );
        }
        private void ProcessSet(VkPipelineLayout pipelineLayout, uint setLocation, PerSetBindings perSetBindings, DescriptorSet[] setsToBind, ref int index)
        {
            // Get or allocate descriptor set
            var descriptorSet = GetOrCreateDescriptorSet(pipelineLayout, setLocation, perSetBindings);

            setsToBind[index++] = descriptorSet;
        }

        private DescriptorSet GetOrCreateDescriptorSet(VkPipelineLayout pipelineLayout, uint setLocation, PerSetBindings perSetBindings)
        {
            // Проверяем, есть ли актуальный набор дескрипторов
            var existingSet = perSetBindings.FirstOrDefault(b => b.DescriptorSet.Handle != default)?.DescriptorSet;
            bool needsUpdate = perSetBindings.Any(b => b.IsDirty);

            if (existingSet.HasValue && !needsUpdate)
            {
                return existingSet.Value;
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

            if (binding.DescriptorSet.Handle == default)
            {
                AllocateAndUpdateDescriptorSet(binding, pipelineLayout);
            }

            descriptorSets[index++] = binding.DescriptorSet;
        }

        private static void HandleUniformBufferBinding(UniformBufferBinding uboBinding, VkPipelineLayout pipelineLayout)
        {
            // Check if the descriptor set is already cached for this pipeline layout
            if (!uboBinding.Buffer.DescriptorSets.TryGetValue(pipelineLayout, out var cachedDescriptorSet) || cachedDescriptorSet.Handle == default)
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
                DescriptorSet[] descriptorSets,
                IReadOnlyList<uint> dynamicOffsets,
                uint minSetIndex)
        {
            fixed (DescriptorSet* descriptorSetsPtr = descriptorSets)
            fixed (uint* dynamicOffsetsPtr = dynamicOffsets.ToArray())
            {
                VulkanContext.Vk.CmdBindDescriptorSets(
                    commandBuffer,
                    PipelineBindPoint.Graphics,
                    pipelineLayout,
                    minSetIndex,
                    (uint)descriptorSets.Length,
                    descriptorSetsPtr,
                    (uint)dynamicOffsets.Count,
                    dynamicOffsetsPtr);
            }
        }

    }
}
