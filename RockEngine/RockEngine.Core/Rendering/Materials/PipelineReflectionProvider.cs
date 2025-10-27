using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace RockEngine.Core.Rendering.Materials
{
    public class PipelineReflectionProvider : IShaderReflectionProvider
    {
        private readonly ConcurrentDictionary<string, ShaderReflectionData> _reflectionCache = new();

        public ShaderReflectionData GetPipelineReflection(VkPipeline pipeline)
        {
            return _reflectionCache.GetOrAdd(pipeline.Name, _ => ExtractPipelineReflection(pipeline));
        }

        private ShaderReflectionData ExtractPipelineReflection(VkPipeline pipeline)
        {
            var reflection = new ShaderReflectionData();

            // Extract from pipeline layout and shader modules
            if (pipeline.Layout != null)
            {
                ExtractLayoutReflection(pipeline, reflection);
            }

            return reflection;
        }

        private void ExtractLayoutReflection(VkPipeline pipeline, ShaderReflectionData reflection)
        {
            // Extract push constants from layout
            foreach (var pushConstant in pipeline.Layout.PushConstantRanges)
            {
                reflection.PushConstants.Add(new ShaderReflectionData.PushConstantInfo
                {
                    Name = pushConstant.Name,
                    StageFlags = pushConstant.StageFlags,
                    Offset = pushConstant.Offset,
                    Size = pushConstant.Size
                });
            }

            // Extract descriptor sets from layout (you'll need to extend VkPipelineLayout to store this)
            // This is a simplified version - in practice, you'd extract from the actual layout
            ExtractDescriptorSetsFromLayout(pipeline, reflection);
        }

        private void ExtractDescriptorSetsFromLayout(VkPipeline pipeline, ShaderReflectionData reflection)
        {
            // This would extract descriptor set info from your pipeline layout
            // For now, using heuristics based on your shader patterns
            HeuristicDescriptorExtraction(pipeline.Name, reflection);
        }

        private void HeuristicDescriptorExtraction(string pipelineName, ShaderReflectionData reflection)
        {
            // Based on your actual shader structures
            switch (pipelineName)
            {
                case "Geometry":
                    AddGeometryDescriptors(reflection);
                    break;
                case "Solid":
                    AddSolidDescriptors(reflection);
                    break;
                case "Skybox":
                    AddSkyboxDescriptors(reflection);
                    break;
                case "DeferredLighting":
                    AddDeferredLightingDescriptors(reflection);
                    break;
                case "Screen":
                    AddScreenDescriptors(reflection);
                    break;
            }
        }

        private void AddGeometryDescriptors(ShaderReflectionData reflection)
        {
            reflection.DescriptorSets.Add(new ShaderReflectionData.DescriptorSetInfo
            {
                Set = 0,
                Bindings = [
                    CreateBinding(0, DescriptorType.UniformBuffer, "GlobalUbo_Dynamic", ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit)
                ]
            });

            reflection.DescriptorSets.Add(new ShaderReflectionData.DescriptorSetInfo
            {
                Set = 1,
                Bindings = [
                    CreateBinding(0, DescriptorType.StorageBuffer, "ModelData", ShaderStageFlags.VertexBit)
                ]
            });

            reflection.DescriptorSets.Add(new ShaderReflectionData.DescriptorSetInfo
            {
                Set = 2,
                Bindings = [
                    CreateBinding(0, DescriptorType.CombinedImageSampler, "uAlbedo", ShaderStageFlags.FragmentBit),
                    CreateBinding(1, DescriptorType.CombinedImageSampler, "uNormalMap", ShaderStageFlags.FragmentBit),
                    CreateBinding(2, DescriptorType.CombinedImageSampler, "uMRA", ShaderStageFlags.FragmentBit)
                ]
            });
        }

        private void AddSolidDescriptors(ShaderReflectionData reflection)
        {
            reflection.DescriptorSets.Add(new ShaderReflectionData.DescriptorSetInfo
            {
                Set = 0,
                Bindings = [
                    CreateBinding(0, DescriptorType.UniformBuffer, "GlobalUbo_Dynamic", ShaderStageFlags.VertexBit)
                ]
            });

            reflection.DescriptorSets.Add(new ShaderReflectionData.DescriptorSetInfo
            {
                Set = 1,
                Bindings = [
                    CreateBinding(0, DescriptorType.StorageBuffer, "ModelData", ShaderStageFlags.VertexBit)
                ]
            });

            reflection.PushConstants.Add(new ShaderReflectionData.PushConstantInfo
            {
                Name = "color",
                StageFlags = ShaderStageFlags.FragmentBit,
                Size = (uint)Unsafe.SizeOf<Vector3>()
            });
        }

        private void AddSkyboxDescriptors(ShaderReflectionData reflection)
        {
            reflection.DescriptorSets.Add(new ShaderReflectionData.DescriptorSetInfo
            {
                Set = 0,
                Bindings = [
                    CreateBinding(0, DescriptorType.UniformBuffer, "GlobalUbo_Dynamic", ShaderStageFlags.VertexBit)
                ]
            });

            reflection.DescriptorSets.Add(new ShaderReflectionData.DescriptorSetInfo
            {
                Set = 1,
                Bindings = [
                    CreateBinding(0, DescriptorType.StorageBuffer, "ModelData", ShaderStageFlags.VertexBit)
                ]
            });

            reflection.DescriptorSets.Add(new ShaderReflectionData.DescriptorSetInfo
            {
                Set = 2,
                Bindings = [
                    CreateBinding(0, DescriptorType.CombinedImageSampler, "cubemapTex", ShaderStageFlags.FragmentBit)
                ]
            });
        }

        private void AddDeferredLightingDescriptors(ShaderReflectionData reflection)
        {
            // Input attachments
            reflection.DescriptorSets.Add(new ShaderReflectionData.DescriptorSetInfo
            {
                Set = 2,
                Bindings = [
                    CreateBinding(0, DescriptorType.InputAttachment, "gPosition", ShaderStageFlags.FragmentBit),
                    CreateBinding(1, DescriptorType.InputAttachment, "gNormal", ShaderStageFlags.FragmentBit),
                    CreateBinding(2, DescriptorType.InputAttachment, "gAlbedo", ShaderStageFlags.FragmentBit),
                    CreateBinding(3, DescriptorType.InputAttachment, "gMRA", ShaderStageFlags.FragmentBit),
                    CreateBinding(4, DescriptorType.InputAttachment, "gObjectID", ShaderStageFlags.FragmentBit)
                ]
            });

            // IBL textures
            reflection.DescriptorSets.Add(new ShaderReflectionData.DescriptorSetInfo
            {
                Set = 3,
                Bindings = [
                    CreateBinding(0, DescriptorType.CombinedImageSampler, "irradianceMap", ShaderStageFlags.FragmentBit),
                    CreateBinding(1, DescriptorType.CombinedImageSampler, "prefilterMap", ShaderStageFlags.FragmentBit),
                    CreateBinding(2, DescriptorType.CombinedImageSampler, "brdfLUT", ShaderStageFlags.FragmentBit)
                ]
            });

            // Light data
            reflection.DescriptorSets.Add(new ShaderReflectionData.DescriptorSetInfo
            {
                Set = 1,
                Bindings = [
                    CreateBinding(0, DescriptorType.StorageBuffer, "LightBuffer", ShaderStageFlags.FragmentBit),
                    CreateBinding(1, DescriptorType.UniformBuffer, "LightCount", ShaderStageFlags.FragmentBit)
                ]
            });

            // IBL push constants
            reflection.PushConstants.Add(new ShaderReflectionData.PushConstantInfo
            {
                Name = "exposure",
                StageFlags = ShaderStageFlags.FragmentBit,
                Size = sizeof(float)
            });
            reflection.PushConstants.Add(new ShaderReflectionData.PushConstantInfo
            {
                Name = "envIntensity",
                StageFlags = ShaderStageFlags.FragmentBit,
                Size = sizeof(float)
            });
            reflection.PushConstants.Add(new ShaderReflectionData.PushConstantInfo
            {
                Name = "aoStrength",
                StageFlags = ShaderStageFlags.FragmentBit,
                Size = sizeof(float)
            });
        }

        private void AddScreenDescriptors(ShaderReflectionData reflection)
        {
            reflection.DescriptorSets.Add(new ShaderReflectionData.DescriptorSetInfo
            {
                Set = 0,
                Bindings = [
                    CreateBinding(0, DescriptorType.CombinedImageSampler, "inputTexture", ShaderStageFlags.FragmentBit)
                ]
            });
        }

        private DescriptorSetLayoutBindingReflected CreateBinding(uint binding, DescriptorType type, string name, ShaderStageFlags stages)
        {
            return new DescriptorSetLayoutBindingReflected(
                name: name,
                binding: binding,
                descriptorType: type,
                descriptorCount: 1,
                stageFlags: stages);
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