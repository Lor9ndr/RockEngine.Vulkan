
using System.Collections.ObjectModel;
using NLog;
using RockEngine.Vulkan.DeviceFeatures;
using Silk.NET.Vulkan;
using ZLinq;

namespace RockEngine.Vulkan
{
    public class VkPipelineLayout : VkObject<PipelineLayout>
    {
        public readonly ShaderReflectionData.PushConstantInfo[] PushConstantRanges;
        public readonly ReadOnlyDictionary<uint, VkDescriptorSetLayout> DescriptorSetLayouts;
        public readonly MergedShaderReflectionData MergedReflectionData;

        private readonly VulkanContext _context;
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private VkPipelineLayout(VulkanContext context, PipelineLayout layout,
            ShaderReflectionData.PushConstantInfo[] pushConstantRanges,
            Dictionary<uint, VkDescriptorSetLayout> descriptorSetLayouts,
            MergedShaderReflectionData mergedReflectionData)
            : base(layout)
        {
            PushConstantRanges = pushConstantRanges;
            DescriptorSetLayouts = descriptorSetLayouts.AsReadOnly();
            MergedReflectionData = mergedReflectionData;
            _context = context;
        }

        public static unsafe VkPipelineLayout Create(VulkanContext context, params VkShaderModule[] shaders)
        {
            bool bindlessSupported = context.FeatureRegistry?.IsFeatureEnabled<DescriptorIndexingFeature>() ?? false;

            // Merge descriptor set layouts across all shaders
            var mergedSetLayouts = CreateDescriptorSetLayouts(context, shaders, bindlessSupported);

            // Merge all shader reflection data (including push constants)
            var mergedReflectionData = MergeShaderReflectionData(shaders.Select(s => s.ReflectedData).ToArray());

            // Use merged push constants (already combined and stage‑flag‑safe)
            var pushConstantRanges = mergedReflectionData.PushConstants.ToArray();

            // Get native layouts in order
            var descriptorSetLayouts = mergedSetLayouts
                .OrderBy(kv => kv.Key)
                .Select(kv => kv.Value.DescriptorSetLayout)
                .ToArray();

            fixed (DescriptorSetLayout* setLayoutsPtr = descriptorSetLayouts)
            fixed (PushConstantRange* pushConstantsPtr = pushConstantRanges.Select(pc => (PushConstantRange)pc).ToArray())
            {
                var layoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = (uint)descriptorSetLayouts.Length,
                    PSetLayouts = setLayoutsPtr,
                    PushConstantRangeCount = (uint)pushConstantRanges.Length,
                    PPushConstantRanges = pushConstantsPtr
                };

                VK.CreatePipelineLayout(context.Device, &layoutInfo,
                    in CustomAllocator<VkPipelineLayout>(), out var pipelineLayout)
                    .VkAssertResult("Failed to create pipeline layout");

                return new VkPipelineLayout(context, pipelineLayout, pushConstantRanges, mergedSetLayouts, mergedReflectionData);
            }
        }

        // Private helper that merges multiple ShaderReflectionData objects into one
        private static MergedShaderReflectionData MergeShaderReflectionData(ShaderReflectionData[] datas)
        {
            // Dictionaries for merging
            var setDict = new Dictionary<uint, DescriptorSetInfo>();
            var bindingLookup = new Dictionary<(uint Set, uint Binding), BindingInfo>();
            var inputVariables = new List<ShaderReflectionData.InputVariable>();
            var rawPushConstants = new Dictionary<(uint Offset, uint Size), ShaderReflectionData.PushConstantInfo>();

            foreach (var data in datas)
            {
                // Merge descriptor sets and bindings
                foreach (var set in data.DescriptorSets)
                {
                    if (!setDict.TryGetValue(set.Set, out var setInfo))
                    {
                        setInfo = new DescriptorSetInfo { Set = set.Set };
                        setDict[set.Set] = setInfo;
                    }

                    foreach (var bindingRef in set.Bindings)
                    {
                        var key = (set.Set, bindingRef.Binding);
                        if (!bindingLookup.TryGetValue(key, out var bindingInfo))
                        {
                            bindingInfo = new BindingInfo { Reflection = bindingRef, Set = set.Set };
                            bindingLookup[key] = bindingInfo;
                            setInfo[bindingRef.Binding] = bindingInfo;
                        }
                        else
                        {
                            // Merge stage flags
                            bindingInfo.Reflection.StageFlags |= bindingRef.StageFlags;
                        }

                        // Attach specific resource info if not already present
                        // (We assume a binding has only one type across all shaders)
                        switch (bindingRef.DescriptorType)
                        {
                            case DescriptorType.UniformBuffer:
                            case DescriptorType.UniformBufferDynamic:
                                if (bindingInfo.UniformBuffer == null)
                                {
                                    var ubo = data.UniformBuffers.FirstOrDefault(u => u.Set == set.Set && u.Binding == bindingRef.Binding);
                                    if (ubo.Name != null) // found
                                    {
                                        bindingInfo.UniformBuffer = ubo;
                                    }
                                }
                                break;
                            case DescriptorType.StorageBuffer:
                                if (bindingInfo.Sampler == null)
                                {
                                    var storageBuffer = data.StorageBuffers.FirstOrDefault(s => s.Set == set.Set && s.Binding == bindingRef.Binding);
                                    if (storageBuffer.Name != null)
                                    {
                                        bindingInfo.StorageBuffer = storageBuffer;
                                    }
                                }
                                break;

                            case DescriptorType.Sampler:
                                if (bindingInfo.Sampler == null)
                                {
                                    var sampler = data.Samplers.FirstOrDefault(s => s.Set == set.Set && s.Binding == bindingRef.Binding);
                                    if (sampler.Name != null)
                                    {
                                        bindingInfo.Sampler = sampler;
                                    }
                                }
                                break;
                            case DescriptorType.CombinedImageSampler:
                                if (bindingInfo.Image == null)
                                {
                                    var image = data.Images.FirstOrDefault(i => i.Set == set.Set && i.Binding == bindingRef.Binding);
                                    if (image.Name != null)
                                    {
                                        bindingInfo.Image = image;
                                    }
                                }
                                break;
                        }
                    }
                }

                // Merge push constants


                // Collect input variables (no merging needed, just add all)
                inputVariables.AddRange(data.InputVariables);
            }
            foreach (var data in datas)
            {
                foreach (var pc in data.PushConstants)
                {
                    var key = (pc.Offset, pc.Size);
                    if (rawPushConstants.TryGetValue(key, out var existing))
                    {
                        existing.StageFlags |= pc.StageFlags;
                    }
                    else
                    {
                        rawPushConstants[key] = pc;
                    }
                }
            }
            // Build final read-only dictionaries and lists
            var descriptorSets = setDict.ToDictionary(kv => kv.Key, kv => kv.Value);
            var bindings = bindingLookup.ToDictionary(kv => kv.Key, kv => kv.Value);
            var combinedPushConstants = CombinePushConstantRanges(rawPushConstants.Values.ToList());
            var inputVariablesList = inputVariables;

            return new MergedShaderReflectionData
            {
                DescriptorSets = descriptorSets.AsReadOnly(),
                Bindings = bindings.AsReadOnly(),
                PushConstants = combinedPushConstants.AsReadOnly(),
                InputVariables = inputVariablesList.AsReadOnly()
            };
        }

        /// <summary>
        /// Merges push constant ranges that share any stage flag.
        /// </summary>
        private static List<ShaderReflectionData.PushConstantInfo> CombinePushConstantRanges(List<ShaderReflectionData.PushConstantInfo> ranges)
        {
            if (ranges.Count <= 1)
            {
                return ranges;
            }

            // Work on a mutable list
            var list = new List<ShaderReflectionData.PushConstantInfo>(ranges);
            bool changed;
            do
            {
                changed = false;
                for (int i = 0; i < list.Count; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var a = list[i];
                        var b = list[j];
                        if ((a.StageFlags & b.StageFlags) != 0) // overlapping stages
                        {
                            // Merge into a new range
                            uint newOffset = Math.Min(a.Offset, b.Offset);
                            uint newEnd = Math.Max(a.Offset + a.Size, b.Offset + b.Size);
                            uint newSize = newEnd - newOffset;
                            var merged = new ShaderReflectionData.PushConstantInfo
                            {
                                Name = a.Name,
                                StageFlags = a.StageFlags | b.StageFlags,
                                Offset = newOffset,
                                Size = newSize
                            };
                            // Replace i and remove j
                            list[i] = merged;
                            list.RemoveAt(j);
                            changed = true;
                            break; // restart outer loop
                        }
                    }
                    if (changed)
                    {
                        break;
                    }
                }
            } while (changed);

            // Optionally sort by offset for consistency
            list.Sort((x, y) => x.Offset.CompareTo(y.Offset));
            return list;
        }


        private static Dictionary<uint, VkDescriptorSetLayout> CreateDescriptorSetLayouts(
            VulkanContext context, VkShaderModule[] shaders, bool bindlessSupported)
        {
            var mergedSets = new Dictionary<uint, List<DescriptorSetLayoutBindingReflected>>();

            // Merge bindings across all shaders by set number
            foreach (var shader in shaders)
            {
                foreach (var setLayout in shader.ReflectedData.DescriptorSets)
                {
                    if (!mergedSets.TryGetValue(setLayout.Set, out var bindings))
                    {
                        bindings = new List<DescriptorSetLayoutBindingReflected>();
                        mergedSets.Add(setLayout.Set, bindings);
                    }

                    foreach (var binding in setLayout.Bindings)
                    {
                        var existing = bindings.FirstOrDefault(b => b.Binding == binding.Binding);
                        if (existing != null)
                        {
                            // Merge stage flags if binding exists in multiple shaders
                            existing.StageFlags |= binding.StageFlags;
                        }
                        else
                        {
                            bindings.Add(binding);
                        }
                    }
                }
            }
            uint maxSet = mergedSets.Keys.Count != 0 ? mergedSets.Keys.Max() : 0;

            // Create empty layouts for missing sets
            for (uint setNumber = 0; setNumber <= maxSet; setNumber++)
            {
                if (!mergedSets.ContainsKey(setNumber))
                {
                    mergedSets[setNumber] = new List<DescriptorSetLayoutBindingReflected>();
                }
            }
            // Create descriptor set layouts for merged sets
            var result = new Dictionary<uint, VkDescriptorSetLayout>();
            foreach (var (setNumber, bindings) in mergedSets)
            {
                var layout = CreateDescriptorSetLayout(context, setNumber, bindings.ToArray(), bindlessSupported);
                result.Add(setNumber, layout);
            }
#if DEBUG
            _logger.Trace("Merged descriptor sets for pipeline layout:");
            foreach (var (setNumber, bindings) in mergedSets)
            {
                _logger.Trace($"  Set {setNumber} has {bindings.Count} bindings");
                foreach (var binding in bindings)
                {
                    _logger.Trace($"    Binding {binding.Binding}: {binding.DescriptorType} ({binding.StageFlags} {binding.Name})");
                }
            }
#endif

            return result;
        }
        private static unsafe VkDescriptorSetLayout CreateDescriptorSetLayout(
       VulkanContext context, uint setNumber, DescriptorSetLayoutBindingReflected[] bindings, bool bindlessSupported)
        {
            var vkBindings = new DescriptorSetLayoutBinding[bindings.Length];
            bool hasRuntimeArray = false;

            for (int i = 0; i < bindings.Length; i++)
            {
                uint actualCount = bindings[i].DescriptorCount;
                if (bindlessSupported && actualCount == 0)
                {
                    // Runtime array: keep 0 and set variable flag later
                    hasRuntimeArray = true;
                    actualCount = 4096;
                }
                else if (actualCount == 0)
                {
                    // In legacy mode, a count of 0 is invalid – fallback to 1
                    actualCount = 1;
                }

                vkBindings[i] = new DescriptorSetLayoutBinding(
                    binding: bindings[i].Binding,
                    descriptorType: bindings[i].DescriptorType,
                    descriptorCount: actualCount,
                    stageFlags: bindings[i].StageFlags,
                    pImmutableSamplers: null);
            }

            DescriptorSetLayoutCreateInfo layoutInfo = new()
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = (uint)vkBindings.Length
            };

            fixed (DescriptorSetLayoutBinding* bindingsPtr = vkBindings)
            {
                layoutInfo.PBindings = bindingsPtr;

                if (hasRuntimeArray)
                {
                    // Build binding flags: set VARIABLE_DESCRIPTOR_COUNT_BIT for bindings with count 0
                    var bindingFlags = new DescriptorBindingFlags[bindings.Length];
                    for (int i = 0; i < bindings.Length; i++)
                    {
                        bindingFlags[i] = (bindings[i].DescriptorCount == 0 && bindlessSupported)
                            ? DescriptorBindingFlags.VariableDescriptorCountBit
                            : 0;
                    }

                    fixed (DescriptorBindingFlags* flagsPtr = bindingFlags)
                    {
                        var bindingFlagsInfo = new DescriptorSetLayoutBindingFlagsCreateInfo
                        {
                            SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
                            BindingCount = (uint)bindings.Length,
                            PBindingFlags = flagsPtr
                        };
                        layoutInfo.PNext = &bindingFlagsInfo;
                    }
                }

                VK.CreateDescriptorSetLayout(context.Device, in layoutInfo,
                    in CustomAllocator<VkDescriptorSetLayout>(),
                    out var descriptorSetLayout)
                    .VkAssertResult("Failed to create descriptor set layout");

                return new VkDescriptorSetLayout(descriptorSetLayout, setNumber, bindings);
            }
        }

        public VkDescriptorSetLayout GetSetLayout(uint location)
        {
            if (DescriptorSetLayouts.TryGetValue(location, out VkDescriptorSetLayout value))
            {
                return value;
            }
            else
            {
                return default;
            }

        }
        public override void LabelObject(string name) => _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.PipelineLayout, name);


        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects) if any.
                }

                // Free unmanaged resources (unmanaged objects) and override a finalizer below.
                // Set large fields to null.
                if (_vkObject.Handle != 0)
                {
                    unsafe
                    {
                        foreach (var item in DescriptorSetLayouts)
                        {
                            VK.DestroyDescriptorSetLayout(_context.Device, item.Value.DescriptorSetLayout, in CustomAllocator<VkDescriptorSetLayout>());
                        }

                        VK.DestroyPipelineLayout(_context.Device, _vkObject, in CustomAllocator<VkPipelineLayout>());
                    }
                    _vkObject = default;
                }

                _disposed = true;
            }
        }
    }
}