using RockEngine.Core.Internal;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;
using RockEngine.Vulkan.DeviceFeatures;
using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public partial class BindingManager
    {
        private readonly VulkanContext _context;
        private readonly DescriptorPoolManager _descriptorPoolManager;
        private readonly ITypeBasedResourceProvider _typeBasedResourceProvider;
        private readonly bool _bindlessEnabled;
        private readonly Lock _updateLocker = new Lock();

        public BindingManager(VulkanContext context, DescriptorPoolManager descriptorPool, ITypeBasedResourceProvider typeBasedResourceProvider, FeatureRegistry featureRegistry)
        {
            _context = context;
            _descriptorPoolManager = descriptorPool;
            _typeBasedResourceProvider = typeBasedResourceProvider;
        }

        public void BindResourcesForMaterial(
             uint frameIndex,
             Material material,
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
                        materialPass.Pipeline.Layout.VkPipelineLayout.GetSetLayout(setLocation) == default)
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
                    materialPass.Pipeline.Layout.VkPipelineLayout.GetSetLayout(setLocation) == default)
                    {
                        continue;
                    }
                    ProcessSet(frameIndex, materialPass.Pipeline.Layout, setLocation, perSetBindings, setsToBind, ref index);
                }
                if (index == 0)
                {
                    return;
                }

                BindDescriptorSetsToCommandBuffer(
                    batch,
                    materialPass.Pipeline.Layout,
                    setsToBind,
                    materialPass.Bindings.DynamicOffsets,
                    materialPass.Bindings.MinSetLocation,
                    isCompute
                );
            }
        }

        public void BindResource(
          uint frameIndex,
          ResourceBinding binding,
          UploadBatch batch,
          CoreObjects.PipelineLayout pipelineLayout,
          bool isCompute = false)
        {
            var setLocation = binding.SetLocation;
            var setLayout = pipelineLayout.VkPipelineLayout.GetSetLayout(setLocation);
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
         VkDescriptorSet set,
         UploadBatch batch,
         VkDescriptorSetLayout setLayout,
         CoreObjects.PipelineLayout pipelineLayout,
         bool isCompute = false)
        {

            BindDescriptorSetsToCommandBuffer(batch, pipelineLayout, [set], [], setLayout.SetLocation, isCompute);
        }
        public void BindResource(
         uint frameIndex,
         UploadBatch batch,
         RckPipeline pipeline,
         bool isCompute,
         params Span<ResourceBinding> bindings)
        {
            using Material material = new Material("tmp");
            using MaterialPass materialPass = new MaterialPass(pipeline);
            material.AddPass(pipeline.SubpassMetadata.Name, materialPass);
            foreach (var binding in bindings)
            {
                materialPass.BindResource(binding);
            }
            BindResourcesForMaterial(frameIndex, material, materialPass, batch, isCompute);
        }

        private void ProcessSet(uint frameIndex, CoreObjects.PipelineLayout pipelineLayout, uint setLocation,
            PerSetBindings perSetBindings, Span<DescriptorSet> setsToBind, ref int index)
        {
            var descriptorSet = GetOrCreateDescriptorSet(frameIndex, pipelineLayout, setLocation, perSetBindings);
            setsToBind[index++] = descriptorSet;
        }

        private VkDescriptorSet GetOrCreateDescriptorSet(uint frameIndex, CoreObjects.PipelineLayout pipelineLayout, uint setLocation, PerSetBindings perSetBindings)
        {
            lock (_updateLocker)
            {
                var setLayout = pipelineLayout.VkPipelineLayout.GetSetLayout(setLocation);
                if (setLayout == default)
                {
                    throw new InvalidOperationException("Failed to find set layout");
                }

                // Find existing set
                VkDescriptorSet? existingSet = null;
                foreach (var binding in perSetBindings)
                {
                    existingSet = binding.GetDescriptorSetForLayout(setLayout, frameIndex);
                    if (existingSet != null)
                    {
                        break;
                    }
                }

                if (existingSet != null && !existingSet.IsDirty)
                {
                    return existingSet;
                }

                // Build variable descriptor counts if the layout has variable bindings
                uint[]? variableCounts = null;
                if (setLayout.VariableBindingIndices.Count > 0)
                {
                    variableCounts = new uint[setLayout.VariableBindingIndices.Count];
                    for (int i = 0; i < setLayout.VariableBindingIndices.Count; i++)
                    {
                        uint bindingIdx = (uint)setLayout.VariableBindingIndices[i];
                        var binding = perSetBindings.GetBinding(bindingIdx);
                        variableCounts[i] = binding?.DescriptorCount ?? 0;
                    }
                }

                // Allocate new set (or reuse dirty one)
                var descriptorSet = existingSet ??
                    _descriptorPoolManager.AllocateDescriptorSet(setLayout, variableCounts);

                // Update all bindings with this set
                foreach (var binding in perSetBindings)
                {
                    if (existingSet == null)
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

        private static void BindDescriptorSetsToCommandBuffer(
                UploadBatch batch,
                CoreObjects.PipelineLayout pipelineLayout,
                ReadOnlySpan<DescriptorSet> descriptorSets,
                ReadOnlySpan<uint> dynamicOffsets,
                uint minSetIndex,
                bool isCompute)
        {
            batch.BindDescriptorSets(
                isCompute ? PipelineBindPoint.Compute : PipelineBindPoint.Graphics,
                pipelineLayout.VkPipelineLayout,
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