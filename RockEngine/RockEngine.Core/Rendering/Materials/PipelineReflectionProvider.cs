using System.Collections.Concurrent;
using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering.Materials
{
    public class PipelineReflectionProvider : IShaderReflectionProvider
    {
        private readonly ConcurrentDictionary<string, MergedShaderReflectionData> _reflectionCache = new();

        public MergedShaderReflectionData GetPipelineReflection(VkPipeline pipeline)
        {
            return _reflectionCache.GetOrAdd(pipeline.Name, _ => ExtractPipelineReflection(pipeline));
        }

        private MergedShaderReflectionData ExtractPipelineReflection(VkPipeline pipeline)
        {

            return pipeline.Layout.MergedReflectionData;
        }


        public ShaderReflectionData CombineShaderReflections(IEnumerable<ShaderReflectionData> reflections)
        {
            var combined = new ShaderReflectionData();
            var descriptorSets = new Dictionary<uint, ShaderReflectionData.DescriptorSetInfo>();
            var pushConstants = new Dictionary<string, ShaderReflectionData.PushConstantInfo>();

            foreach (var reflection in reflections)
            {
                // Combine descriptor sets
                foreach (var set in reflection.DescriptorSets)
                {
                    if (!descriptorSets.TryGetValue(set.Set, out var existingSet))
                    {
                        existingSet = new ShaderReflectionData.DescriptorSetInfo { Set = set.Set };
                        descriptorSets[set.Set] = existingSet;
                    }

                    // Merge bindings
                    var bindingsDict = existingSet.Bindings?.ToDictionary(b => b.Binding) ?? new Dictionary<uint, DescriptorSetLayoutBindingReflected>();
                    foreach (var binding in set.Bindings)
                    {
                        if (bindingsDict.TryGetValue(binding.Binding, out var existingBinding))
                        {
                            // Merge stage flags
                            existingBinding = existingBinding with { StageFlags = existingBinding.StageFlags | binding.StageFlags };
                        }
                        else
                        {
                            bindingsDict[binding.Binding] = binding;
                        }
                    }
                    existingSet.Bindings = bindingsDict.Values.ToArray();
                }

                // Combine push constants
                foreach (var pushConst in reflection.PushConstants)
                {
                    if (pushConstants.TryGetValue(pushConst.Name, out var existing))
                    {
                        existing.StageFlags |= pushConst.StageFlags;
                        existing.Size = Math.Max(existing.Size, pushConst.Size);
                    }
                    else
                    {
                        pushConstants[pushConst.Name] = pushConst;
                    }
                }
            }

            combined.DescriptorSets.AddRange(descriptorSets.Values);
            combined.PushConstants.AddRange(pushConstants.Values);

            return combined;
        }
    }
}