using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;
using RockEngine.Vulkan.Rendering;
using RockEngine.Vulkan.Rendering.MaterialRendering;
using RockEngine.Vulkan.VkObjects.Infos.Texture;
using RockEngine.Vulkan.VulkanInitilizers;
using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.Rendering.ComponentRenderers;
using Silk.NET.Core.Native;
using Silk.NET.Input;
using static System.Net.WebRequestMethods;
using System.Net.NetworkInformation;
using System.Reflection.Metadata;
using System;

namespace RockEngine.Vulkan.VkObjects
{
    public sealed class PipelineManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly Dictionary<string, EffectTemplate> _templateCache;

        public PipelineManager(VulkanContext context)
        {
            _context = context;
            _templateCache = new Dictionary<string, EffectTemplate>();
        }

        // We can use that attribute, but JIT has to do it by itself, has to figure out by looking into JIT code
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PipelineWrapper CreatePipeline(string name, ref GraphicsPipelineCreateInfo ci, RenderPassWrapper renderPass, PipelineLayoutWrapper layout)
            => PipelineWrapper.Create(_context, name, ref ci, renderPass, layout);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddEffectTemplate(string name, EffectTemplate effectTemplate)
            => _templateCache[name] = effectTemplate;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EffectTemplate? GetEffect(string effectName)
            => _templateCache.TryGetValue(effectName, out var effect) ? effect : null;

        public void Use(Material material, FrameInfo frameInfo, PipelineBindPoint bindPoint = PipelineBindPoint.Graphics)
        {
            if (frameInfo.CommandBuffer is null)
            {
                throw new ArgumentNullException(nameof(frameInfo.CommandBuffer), "Command buffer is null");
            }

            TrySetEffect(frameInfo, material.Original, frameInfo.PassType);

            var descriptorSet = material.PassSets[frameInfo.PassType];
            var pipeline = material.Original.PassShaders[frameInfo.PassType].Pipeline;

            if (descriptorSet.Handle != default && pipeline != null)
            {
                frameInfo.DescriptorSetQueue.Enqueue((Constants.MATERIAL_SET, descriptorSet));
            }
        }

        public void Use(UniformBufferObject ubo, FrameInfo frameInfo, PipelineBindPoint bindPoint = PipelineBindPoint.Graphics)
        {
            if (frameInfo.CurrentEffect is null)
            {
                frameInfo.PendingUBOs.Add(ubo);
                return;
            }

            ProcessPendingUBOs(frameInfo);

            var pipelineLayout = frameInfo.CurrentEffect.PassShaders[frameInfo.PassType].Layout;
            if (ubo.PerPipelineDescriptorSet.TryGetValue(pipelineLayout, out var descriptorSetInfo))
            {
                frameInfo.DescriptorSetQueue.Enqueue((descriptorSetInfo.setIndex, descriptorSetInfo.set));
            }
        }

        private void ProcessPendingUBOs(FrameInfo frameInfo)
        {
            if (frameInfo.CurrentEffect is null)
            {
                return;
            }

            var pipelineLayout = frameInfo.CurrentEffect.PassShaders[frameInfo.PassType].Layout;
            for (int i = 0; i < frameInfo.PendingUBOs.Count; i++)
            {
                UniformBufferObject? ubo = frameInfo.PendingUBOs[i];
                if (ubo.PerPipelineDescriptorSet.TryGetValue(pipelineLayout, out var descriptorSetInfo))
                {
                    frameInfo.DescriptorSetQueue.Enqueue((descriptorSetInfo.setIndex, descriptorSetInfo.set));
                }
            }
            frameInfo.PendingUBOs.RemoveAll(s => s.PerPipelineDescriptorSet.ContainsKey(pipelineLayout));
        }

        public unsafe void BindQueuedDescriptorSets(FrameInfo frameInfo, PipelineBindPoint bindPoint = PipelineBindPoint.Graphics)
        {
            if (frameInfo.CommandBuffer is null || frameInfo.CurrentEffect is null)
            {
                return;
            }

            var queue = frameInfo.DescriptorSetQueue;
            int count = queue.Count;
            if (count == 0)
            {
                return;
            }

            const int MaxStackAlloc = 32;
            Span<DescriptorSet> descriptorSets = count <= MaxStackAlloc ? stackalloc DescriptorSet[MaxStackAlloc] : new DescriptorSet[count];
            Span<uint> setIndices = count <= MaxStackAlloc ? stackalloc uint[MaxStackAlloc] : new uint[count];

            uint minSetIndex = uint.MaxValue;
            uint maxSetIndex = 0;

            for (int i = 0; i < count; i++)
            {
                var (setIndex, descriptorSet) = queue.Dequeue();
                descriptorSets[i] = descriptorSet;
                setIndices[i] = setIndex;
                minSetIndex = Math.Min(minSetIndex, setIndex);
                maxSetIndex = Math.Max(maxSetIndex, setIndex);
            }

            var pipelineLayout = frameInfo.CurrentEffect.PassShaders[frameInfo.PassType].Layout;
            uint setCount = maxSetIndex - minSetIndex + 1;
            Span<DescriptorSet> finalDescriptorSets = stackalloc DescriptorSet[(int)setCount];

            for (int i = 0; i < count; i++)
                finalDescriptorSets[(int)(setIndices[i] - minSetIndex)] = descriptorSets[i];

            _context.Api.CmdBindDescriptorSets(
                frameInfo.CommandBuffer.VkObjectNative,
                bindPoint,
                pipelineLayout,
                minSetIndex,
                setCount,
                finalDescriptorSets,
                0,
                ReadOnlySpan<uint>.Empty
            );
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TrySetEffect(FrameInfo frameInfo, EffectTemplate effect, MeshpassType type)
        {
            if (frameInfo.CurrentEffect is null || frameInfo.CurrentEffect.PassShaders[type].Pipeline != effect.PassShaders[type].Pipeline)
            {
                _context.Api.CmdBindPipeline(frameInfo.CommandBuffer!.VkObjectNative, PipelineBindPoint.Graphics, effect.PassShaders[type].Pipeline);
                frameInfo.CurrentEffect = effect;
                frameInfo.PassType = type;
            }
        }

        public void SetMaterialDescriptors(Material material)
        {
            foreach (var (passType, shader) in material.Original.PassShaders)
            {
                if (shader != null)
                {
                    var layout = FindMaterialSetLayout(shader);
                    if (layout.DescriptorSetLayout.Handle != default)
                    {
                        var descriptorSet = shader.Pipeline.GetOrCreateDescriptorSet(layout.DescriptorSetLayout);
                        UpdateDescriptorSetWithTextures(material, descriptorSet, layout);
                        material.PassSets[passType] = descriptorSet;
                    }
                }
            }
        }

        public unsafe void SetBuffer(UniformBufferObject ubo, uint setIndex, uint bindingIndex)
        {
            var bufferInfo = new DescriptorBufferInfo
            {
                Buffer = ubo.UniformBuffer,
                Offset = 0,
                Range = ubo.Size
            };

            foreach (var (pipeline, layout) in FindMatchingPipelinesForBuffer(ubo, setIndex, bindingIndex))
            {
                var descriptorSet = UpdateDescriptorSetForBuffer(pipeline, layout, bindingIndex, bufferInfo);
                ubo.PerPipelineDescriptorSet[pipeline.Layout] = (setIndex, descriptorSet);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private DescriptorSetLayoutWrapper FindMaterialSetLayout(ShaderPass shaderPass)
            => shaderPass.Layout.DescriptorSetLayouts.FirstOrDefault(l => l.SetLocation == Constants.MATERIAL_SET);

        private IEnumerable<(PipelineWrapper Pipeline, DescriptorSetLayoutWrapper Layout)> FindMatchingPipelinesForBuffer(UniformBufferObject ubo, uint setIndex, uint bindingIndex)
        {
            return _templateCache.Values
                .SelectMany(t => t.PassShaders)
                .Select(v => (v.value.Pipeline, Layout: v.value.Layout.DescriptorSetLayouts.FirstOrDefault(l =>
                    l.SetLocation == setIndex &&
                    l.Bindings.Any(b => b.Name == ubo.Name && b.Binding == bindingIndex && b.DescriptorType == DescriptorType.UniformBuffer))))
                .Where(x => x.Pipeline != null && x.Layout.DescriptorSetLayout.Handle != default);
        }

        private unsafe DescriptorSet UpdateDescriptorSetForBuffer(PipelineWrapper pipeline, DescriptorSetLayoutWrapper layout, uint bindingIndex, DescriptorBufferInfo bufferInfo)
        {
            var descriptorSet = pipeline.GetOrCreateDescriptorSet(layout.DescriptorSetLayout);
            var writeDescriptorSet = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSet,
                DstBinding = bindingIndex,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                PBufferInfo = &bufferInfo
            };
            _context.Api.UpdateDescriptorSets(_context.Device, 1, &writeDescriptorSet, 0, null);
            return descriptorSet;
        }

        private unsafe void UpdateDescriptorSetWithTextures(Material material, DescriptorSet descriptorSet, DescriptorSetLayoutWrapper layout)
        {
            var writeDescriptorSets = new List<WriteDescriptorSet>();
            var imageInfos = new List<DescriptorImageInfo>();

            for (int bindingIndex = 0; bindingIndex < layout.Bindings.Length; bindingIndex++)
            {
                var binding = layout.Bindings[bindingIndex];
                if (binding.DescriptorType != DescriptorType.CombinedImageSampler || binding.Binding != bindingIndex)
                {
                    continue;
                }
                Texture? texture = material.Textures.ElementAtOrDefault(bindingIndex);

                LoadedTextureInfo textureInfo;
                if (texture is null || texture?.TextureInfo is not LoadedTextureInfo loadedInfo)
                {
                    textureInfo = (LoadedTextureInfo)Texture.GetEmptyTexture(_context).TextureInfo;
                }
                else
                {
                    textureInfo = loadedInfo;
                }

                imageInfos.Add(new DescriptorImageInfo
                {
                    ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                    ImageView = textureInfo.ImageView,
                    Sampler = textureInfo.Sampler,
                });

                writeDescriptorSets.Add(new WriteDescriptorSet()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = descriptorSet,
                    DstBinding = binding.Binding,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = null  // We'll set this later
                });
            }

            fixed (DescriptorImageInfo* pImageInfos = imageInfos.ToArray())
            fixed (WriteDescriptorSet* pWriteDescriptorSets = writeDescriptorSets.ToArray())
            {
                for (int i = 0; i < writeDescriptorSets.Count; i++)
                {
                    pWriteDescriptorSets[i].PImageInfo = &pImageInfos[i];
                }

                _context.Api.UpdateDescriptorSets(_context.Device, (uint)writeDescriptorSets.Count, pWriteDescriptorSets, 0, null);
            }
        }


        public void Dispose()
        {
            foreach (var template in _templateCache.Values)
            {
                foreach (var (_, value) in template.PassShaders)
                {
                    value.Pipeline?.Dispose();
                }
            }
            _templateCache.Clear();
        }
    }
}
