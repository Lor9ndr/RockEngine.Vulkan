using RockEngine.Core.ECS.Components;
using RockEngine.Core.Internal;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.Objects;
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
        private readonly GraphicsContext _graphicsEngine;
        private readonly object _updateLocker = new object();

        public BindingManager(VulkanContext context, DescriptorPoolManager descriptorPool, GraphicsContext graphicsEngine)
        {
            _context = context;
            _descriptorPoolManager = descriptorPool;
            _graphicsEngine = graphicsEngine;
        }

        public void BindResourcesForMaterial(
             uint frameIndex,
             MaterialPass materialPass,
             UploadBatch batch,
             bool isCompute = false,
             Span<uint> skipSets = default)
        {
            int maxSets = materialPass.Bindings.Count;
            if (maxSets <= 8)
            {
                Span<DescriptorSet> setsToBind = stackalloc DescriptorSet[maxSets];
                Span<uint> dynamicOffsets = stackalloc uint[16];

                int setIndex = 0;
                int dynamicIndex = 0;

                foreach (var (setLocation, perSetBindings) in materialPass.Bindings)
                {
                    if (skipSets.Contains(setLocation) ||
                        materialPass.Pipeline.Layout.GetSetLayout(setLocation) == default)
                    {
                        continue;
                    }

                    var descriptorSet = GetOrCreateDescriptorSet(
                        frameIndex,
                        materialPass.Pipeline.Layout,
                        setLocation,
                        perSetBindings);

                    setsToBind[setIndex++] = descriptorSet;

                    // Collect dynamic offsets
                    foreach (var binding in perSetBindings)
                    {
                        if (binding is UniformBufferBinding ubo && ubo.Buffer.IsDynamic)
                        {
                            dynamicOffsets[dynamicIndex++] = (uint)ubo.Offset;
                        }
                    }
                }

                if (setIndex > 0)
                {
                    materialPass.PrepareToRender(batch);
                    BindDescriptorSetsToCommandBuffer(
                        batch,
                        materialPass.Pipeline.Layout,
                        setsToBind[..setIndex],
                        dynamicOffsets[..dynamicIndex],
                        materialPass.Bindings.MinSetLocation,
                        isCompute
                    );
                }
            }
            else
            {
                Span<DescriptorSet> setsToBind = stackalloc DescriptorSet[materialPass.Bindings.Count];
                int index = 0;

                foreach (var (setLocation, perSetBindings) in materialPass.Bindings)
                {
                    if (skipSets.Contains(setLocation) ||
                    materialPass.Pipeline.Layout.GetSetLayout(setLocation) == default)
                    {
                        continue;
                    }
                    ProcessSet(frameIndex, materialPass.Pipeline.Layout, setLocation, perSetBindings, setsToBind, ref index);
                }
                if (index == 0)
                {
                    return;
                }
                materialPass.PrepareToRender(batch);

                BindDescriptorSetsToCommandBuffer(
                    batch,
                    materialPass.Pipeline.Layout,
                    setsToBind,
                    CollectionsMarshal.AsSpan(materialPass.Bindings.DynamicOffsets),
                    materialPass.Bindings.MinSetLocation,
                    isCompute
                );
            }
        }

        public void BindResource(
          uint frameIndex,
          ResourceBinding binding,
          UploadBatch batch,
          VkPipelineLayout pipelineLayout,
          bool isCompute = false)
        {
            var setLocation = binding.SetLocation;
            var setLayout = pipelineLayout.GetSetLayout(setLocation);
            if (setLayout == default || setLayout.Bindings.Length == 0 ||
                setLayout.Bindings.Any(s => s.DescriptorType != binding.DescriptorType))
            {
                return;
            }

            var perSetBindings = new PerSetBindings(setLocation);
            Span<uint> dynamicOffsets = [];

            perSetBindings.Add(binding);
            if (binding is UniformBufferBinding uboBinding && uboBinding.Buffer.IsDynamic)
            {
                dynamicOffsets = new Span<uint>([(uint)uboBinding.Offset]);
            }

            var descriptorSet = GetOrCreateDescriptorSet(frameIndex, pipelineLayout, setLocation, perSetBindings);

            BindDescriptorSetsToCommandBuffer(batch, pipelineLayout, [descriptorSet], dynamicOffsets, perSetBindings.Set, isCompute);
        }
        public void BindResource(
         uint frameIndex,
         UploadBatch batch,
         RckPipeline pipeline,
         bool isCompute,
         params Span<ResourceBinding> bindings)
        {
            MaterialPass materialPass = new MaterialPass(pipeline);
            foreach (var binding in bindings)
            {
                materialPass.BindResource(binding);
            }
            BindResourcesForMaterial(frameIndex, materialPass,batch, isCompute);
        }

        private void ProcessSet(uint frameIndex, VkPipelineLayout pipelineLayout, uint setLocation,
            PerSetBindings perSetBindings, Span<DescriptorSet> setsToBind, ref int index)
        {
            var descriptorSet = GetOrCreateDescriptorSet(frameIndex, pipelineLayout, setLocation, perSetBindings);
            setsToBind[index++] = descriptorSet;
        }

        private VkDescriptorSet GetOrCreateDescriptorSet(uint frameIndex, VkPipelineLayout pipelineLayout,
            uint setLocation, PerSetBindings perSetBindings)
        {
            lock (_updateLocker)
            {
                var setLayout = pipelineLayout.GetSetLayout(setLocation);

                // Check if any binding already has a descriptor set for this layout
                VkDescriptorSet existingSet = null;
                foreach (var binding in perSetBindings)
                {
                    existingSet = binding.GetDescriptorSetForLayout(setLayout, frameIndex);
                    if (existingSet != null)
                    {
                        break;
                    }
                }
                if (existingSet is not null && !existingSet.IsDirty)
                {
                    return existingSet;
                }

                var descriptorSet = existingSet ?? _descriptorPoolManager.AllocateDescriptorSet(setLayout);

                // Update all bindings with this new descriptor set
                foreach (var binding in perSetBindings)
                {
                    if(existingSet is null)
                    {
                        binding.SetDescriptorSetForLayout(setLayout, frameIndex, descriptorSet);
                    }
                    if (descriptorSet.IsDirty)
                    {
                        binding.UpdateDescriptorSet(_context, frameIndex, setLayout);
                    }
                }
                descriptorSet.IsDirty = false;

                return descriptorSet;
            }
        }

        private unsafe void BindDescriptorSetsToCommandBuffer(
                UploadBatch batch,
                VkPipelineLayout pipelineLayout,
                Span<DescriptorSet> descriptorSets,
                Span<uint> dynamicOffsets,
                uint minSetIndex,
                bool isCompute)
        {
            batch.BindDescriptorSets(
                isCompute ? PipelineBindPoint.Compute : PipelineBindPoint.Graphics,
                pipelineLayout,
                minSetIndex,
                descriptorSets,
                dynamicOffsets);
        }

        public void AllocateAndUpdateDescriptorSet(uint frameIndex, ResourceBinding binding, VkPipelineLayout pipelineLayout)
        {
            var setLayout = pipelineLayout.GetSetLayout(binding.SetLocation);
            var set = _descriptorPoolManager.AllocateDescriptorSet(setLayout);
            binding.SetDescriptorSetForLayout(setLayout, frameIndex, set);
            binding.UpdateDescriptorSet(_context, frameIndex, setLayout);
        }

        public void AllocateDescriptorSet(uint frameIndex, ResourceBinding binding, VkPipelineLayout pipelineLayout)
        {
            var setLayout = pipelineLayout.GetSetLayout(binding.SetLocation);
            var set = _descriptorPoolManager.AllocateDescriptorSet(setLayout);
            binding.SetDescriptorSetForLayout(setLayout, frameIndex, set);
        }
    }
}