using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public class BindingManager
    {
        private readonly RenderingContext _context;
        private readonly VkDescriptorPool _descriptorPool;
        private readonly GraphicsEngine _graphicsEngine;

        public BindingManager(RenderingContext context, VkDescriptorPool descriptorPool, GraphicsEngine graphicsEngine)
        {
            _context = context;
            _descriptorPool = descriptorPool;
            _graphicsEngine = graphicsEngine;
        }

        public void BindResourcesForMaterial(Material material, VkCommandBuffer commandBuffer)
        {
            var descriptorSets = new DescriptorSet[material.Bindings.Count];
            var dynamicOffsets = new List<uint>();

            int index = 0;

            foreach (var binding in material.Bindings)
            {
                ProcessBinding(binding, material.Pipeline.Layout, descriptorSets, dynamicOffsets, ref index);
            }

            BindDescriptorSetsToCommandBuffer(commandBuffer, material.Pipeline.Layout, descriptorSets, dynamicOffsets, material.Bindings.MinSetLocation);
        }

        private void ProcessBinding(
            ResourceBinding binding,
            VkPipelineLayout pipelineLayout,
            DescriptorSet[] descriptorSets,
            List<uint> dynamicOffsets,
            ref int index)
        {
            if (binding is UniformBufferBinding uboBinding)
            {
                HandleUniformBufferBinding(uboBinding, pipelineLayout, dynamicOffsets);
            }

            if (binding.DescriptorSet.Handle == default)
            {
                AllocateAndBindDescriptorSet(binding, pipelineLayout);
            }

            descriptorSets[index++] = binding.DescriptorSet;
        }

        private static void HandleUniformBufferBinding(UniformBufferBinding uboBinding, VkPipelineLayout pipelineLayout, List<uint> dynamicOffsets)
        {
            if (uboBinding.Buffer.IsDynamic)
            {
                dynamicOffsets.Add((uint)uboBinding.Offset);
            }

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

        private void AllocateAndBindDescriptorSet(ResourceBinding binding, VkPipelineLayout pipelineLayout)
        {
            var setLayout = pipelineLayout.GetSetLayout(binding.SetLocation);
            var set = _descriptorPool.AllocateDescriptorSet(setLayout);
            binding.DescriptorSet = set;
            binding.UpdateDescriptorSet(_context);
        }

        private unsafe void BindDescriptorSetsToCommandBuffer(
                VkCommandBuffer commandBuffer,
                VkPipelineLayout pipelineLayout,
                DescriptorSet[] descriptorSets,
                List<uint> dynamicOffsets,
                uint minSetIndex)
        {
            fixed (DescriptorSet* descriptorSetsPtr = descriptorSets)
            fixed (uint* dynamicOffsetsPtr = dynamicOffsets.ToArray())
            {
                RenderingContext.Vk.CmdBindDescriptorSets(
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
