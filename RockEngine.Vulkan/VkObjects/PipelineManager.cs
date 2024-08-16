using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;
using RockEngine.Vulkan.Rendering;
using RockEngine.Vulkan.Rendering.MaterialRendering;
using RockEngine.Vulkan.VkObjects.Infos.Texture;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.VkObjects
{
    public sealed class PipelineManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly Dictionary<string, EffectTemplate> _templateCache;
        private readonly Queue<UniformBufferObject> _pendingUBOs;
        private readonly ObjectPool<List<WriteDescriptorSet>> _writeDescriptorSetListPool;

        public PipelineManager(VulkanContext context)
        {
            _context = context;
            _templateCache = new Dictionary<string, EffectTemplate>();
            _pendingUBOs = new Queue<UniformBufferObject>();
            _writeDescriptorSetListPool = new ObjectPool<List<WriteDescriptorSet>>(() => new List<WriteDescriptorSet>(), list => list.Clear());
        }

        // We can use that attribute, but JIT has to do it by itself, has to figure out by looking into JIT code
        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PipelineWrapper CreatePipeline(string name, ref GraphicsPipelineCreateInfo ci, RenderPassWrapper renderPass, PipelineLayoutWrapper layout)
            => PipelineWrapper.Create(_context, name, ref ci, renderPass, layout);

        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddEffectTemplate(string name, EffectTemplate effectTemplate)
            => _templateCache[name] = effectTemplate;

        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EffectTemplate GetEffect(string effectName)
            => _templateCache.TryGetValue(effectName, out var effect) ? effect : null;

        public void Use(Material material, FrameInfo frameInfo, PipelineBindPoint bindPoint = PipelineBindPoint.Graphics)
        {
            if (frameInfo.CommandBuffer is null)
                throw new ArgumentNullException(nameof(frameInfo.CommandBuffer), "Command buffer is null");

            TrySetEffect(frameInfo, material.Original, frameInfo.PassType);

            var descriptorSet = material.PassSets[frameInfo.PassType];
            var pipeline = material.Original.PassShaders[frameInfo.PassType].Pipeline;

            if (descriptorSet.Handle != default && pipeline != null)
                frameInfo.DescriptorSetQueue.Enqueue((Constants.MATERIAL_SET, descriptorSet));
        }

        public void Use(UniformBufferObject ubo, FrameInfo frameInfo, PipelineBindPoint bindPoint = PipelineBindPoint.Graphics)
        {
            if (frameInfo.CurrentEffect is null)
            {
                _pendingUBOs.Enqueue(ubo);
                return;
            }

            ProcessPendingUBOs(frameInfo);

            var pipelineLayout = frameInfo.CurrentEffect.PassShaders[frameInfo.PassType].Layout;
            if (ubo.PerPipelineDescriptorSet.TryGetValue(pipelineLayout, out var descriptorSetInfo))
                frameInfo.DescriptorSetQueue.Enqueue((descriptorSetInfo.setIndex, descriptorSetInfo.set));
        }

        private void ProcessPendingUBOs(FrameInfo frameInfo)
        {
            if (frameInfo.CurrentEffect is null)
                return;

            var pipelineLayout = frameInfo.CurrentEffect.PassShaders[frameInfo.PassType].Layout;
            while (_pendingUBOs.TryDequeue(out var ubo))
            {
                if (ubo.PerPipelineDescriptorSet.TryGetValue(pipelineLayout, out var descriptorSetInfo))
                    frameInfo.DescriptorSetQueue.Enqueue((descriptorSetInfo.setIndex, descriptorSetInfo.set));
            }
        }

        public unsafe void BindQueuedDescriptorSets(FrameInfo frameInfo, PipelineBindPoint bindPoint = PipelineBindPoint.Graphics)
        {
            if (frameInfo.CommandBuffer is null || frameInfo.CurrentEffect is null)
                return;

            var queue = frameInfo.DescriptorSetQueue;
            int count = queue.Count;
            if (count == 0)
                return;

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
            finalDescriptorSets.Clear();

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
            if (frameInfo.CurrentEffect != effect || type != frameInfo.PassType)
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
                        var descriptorSet = shader.Pipeline.CreateDescriptorSet(layout.DescriptorSetLayout);
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

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            var descriptorSet = pipeline.CreateDescriptorSet(layout.DescriptorSetLayout);
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
            var writeDescriptorSets = _writeDescriptorSetListPool.Get();

            for (int bindingIndex = 0; bindingIndex < material.Textures.Count; bindingIndex++)
            {
                if (material.Textures[bindingIndex]?.TextureInfo is LoadedTextureInfo loadedInfo)
                {
                    var matchingBinding = layout.Bindings.FirstOrDefault(b => b.Binding == bindingIndex && b.DescriptorType == DescriptorType.CombinedImageSampler);

                    if (matchingBinding != null)
                    {
                        var imageInfo = new DescriptorImageInfo
                        {
                            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                            ImageView = loadedInfo.ImageView,
                            Sampler = loadedInfo.Sampler,
                        };

                        writeDescriptorSets.Add(new WriteDescriptorSet()
                        {
                            SType = StructureType.WriteDescriptorSet,
                            DstSet = descriptorSet,
                            DstBinding = matchingBinding.Binding,
                            DstArrayElement = 0,
                            DescriptorType = DescriptorType.CombinedImageSampler,
                            DescriptorCount = 1,
                            PImageInfo = &imageInfo
                        });
                    }
                }

            }

            fixed (WriteDescriptorSet* pWriteDescriptorSets = writeDescriptorSets.ToArray())
            {
                _context.Api.UpdateDescriptorSets(_context.Device, (uint)writeDescriptorSets.Count, pWriteDescriptorSets, 0, null);
            }

            _writeDescriptorSetListPool.Return(writeDescriptorSets);
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
            _pendingUBOs.Clear();
        }
    }

    public class ObjectPool<T>
    {
        private readonly Func<T> _factory;
        private readonly Action<T> _reset;
        private readonly Stack<T> _objects = new Stack<T>();

        public ObjectPool(Func<T> factory, Action<T> reset)
        {
            _factory = factory;
            _reset = reset;
        }

        public T Get() => _objects.Count > 0 ? _objects.Pop() : _factory();

        public void Return(T item)
        {
            _reset(item);
            _objects.Push(item);
        }
    }
}
