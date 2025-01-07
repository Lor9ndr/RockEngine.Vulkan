using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    namespace RockEngine.Core.Rendering.Managers
    {
        public class BindingManager
        {
            private readonly RenderingContext _context;
            private readonly DescriptorSetManager _descriptorSetManager;
            private readonly GraphicsEngine _graphicsEngine;
            private readonly Dictionary<uint, DescriptorSet>[] _descriptorSets; // Per-frame descriptor sets
            private uint _dynamicOffset = 0;

            public BindingManager(RenderingContext context, DescriptorSetManager descriptorSetManager, GraphicsEngine graphicsEngine)
            {
                _context = context;
                _descriptorSetManager = descriptorSetManager;
                _graphicsEngine = graphicsEngine;
                _descriptorSets = new Dictionary<uint, DescriptorSet>[context.MaxFramesPerFlight];
                for (int i = 0; i < _descriptorSets.Length; i++)
                {
                    _descriptorSets[i] = new Dictionary<uint, DescriptorSet>();
                }
            }

            public void BindResourcesForMaterial(Material material, VkCommandBuffer commandBuffer, VkPipelineLayout pipelineLayout)
            {
                _dynamicOffset = 0; // Reset dynamic offset for each material

                var descriptorSets = new List<DescriptorSet>();
                var dynamicOffsets = new List<uint>();

                // Determine the maximum set index used by the material
                uint maxSetIndex = 0;
                uint minSetIndex = 0;
                foreach (var binding in material.Bindings)
                {
                    // Assume ResourceBinding class has a Set property
                    if (binding.SetLocation > maxSetIndex)
                    {
                        maxSetIndex = binding.SetLocation;
                    }
                    if (binding.SetLocation < minSetIndex)
                    {
                        minSetIndex = binding.SetLocation;
                    }
                }

                // Create and bind descriptor sets for each set index
                for (uint setIndex = 0; setIndex <= maxSetIndex; setIndex++)
                {
                    BindDescriptorSetForSetIndex(material, pipelineLayout, setIndex, descriptorSets, dynamicOffsets);
                }

                // Bind the array of descriptor sets
                unsafe
                {
                    fixed (DescriptorSet* descriptorSetsPtr = descriptorSets.ToArray())
                    fixed (uint* dynamicOffsetsPtr = dynamicOffsets.ToArray())
                    {
                        RenderingContext.Vk.CmdBindDescriptorSets(
                            commandBuffer,
                            PipelineBindPoint.Graphics,
                            pipelineLayout,
                            minSetIndex, // Assuming you are starting from set 0
                            (uint)descriptorSets.Count,
                            descriptorSetsPtr,
                            (uint)dynamicOffsets.Count,
                            dynamicOffsetsPtr
                        );
                    }
                }
            }
            private void BindDescriptorSetForSetIndex(
               Material material,
               VkPipelineLayout pipelineLayout,
               uint setIndex,
               List<DescriptorSet> descriptorSets,
               List<uint> dynamicOffsets)
            {
                List<ResourceBinding> bindingsForSet = material.Bindings
                    .Where(b => b.SetLocation == setIndex)
                    .ToList();

                if (bindingsForSet.Count > 0)
                {
                    var descriptorSet = GetDescriptorSetForBinding(bindingsForSet, pipelineLayout, setIndex);

                    // Update dynamic offset for dynamic uniform buffers
                    foreach (var binding in bindingsForSet)
                    {
                        if (binding is UniformBufferBinding uboBinding && uboBinding.Buffer.IsDynamic)
                        {
                            dynamicOffsets.Add(_dynamicOffset);
                            _dynamicOffset += (uint)uboBinding.Buffer.Size; // Assuming Size is already aligned
                        }
                    }

                    descriptorSets.Add(descriptorSet);
                }
            }

            private DescriptorSet GetDescriptorSetForBinding(List<ResourceBinding> bindingsForSet, VkPipelineLayout pipelineLayout, uint setIndex)
            {
                var currentFrameIndex = _graphicsEngine.CurrentImageIndex;

                if (!_descriptorSets[currentFrameIndex].TryGetValue(setIndex, out var descriptorSet))
                {
                    descriptorSet = _descriptorSetManager.AllocateDescriptorSet(pipelineLayout.GetSetLayout(setIndex));
                    _descriptorSets[currentFrameIndex][setIndex] = descriptorSet;
                    foreach (var binding in bindingsForSet)
                    {
                        UpdateDescriptorSet(descriptorSet, binding);
                    }
                }

                return descriptorSet;
            }

            private unsafe void UpdateDescriptorSet(DescriptorSet descriptorSet, ResourceBinding binding)
            {
                if (binding is UniformBufferBinding uboBinding)
                {
                    uint dynamicOffset = uboBinding.Buffer.IsDynamic ? uboBinding.Buffer.DynamicOffset : 0;

                    var bufferInfo = new DescriptorBufferInfo
                    {
                        Buffer = uboBinding.Buffer.Buffer,
                        Offset = dynamicOffset,
                        Range = uboBinding.Buffer.Size
                    };

                    var writeDescriptorSet = new WriteDescriptorSet
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = descriptorSet,
                        DstBinding = uboBinding.BindingLocation,
                        DstArrayElement = 0,
                        DescriptorType = DescriptorType.UniformBuffer,
                        DescriptorCount = 1,
                        PBufferInfo = &bufferInfo
                    };

                    RenderingContext.Vk.UpdateDescriptorSets(_context.Device, 1, in writeDescriptorSet, 0, null);
                }

                // Add similar logic for other binding types (TextureBinding, etc.)
            }
        }
    }

}
