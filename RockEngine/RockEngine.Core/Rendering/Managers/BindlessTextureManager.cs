namespace RockEngine.Core.Rendering.Managers
{
    /* public class BindlessTextureManager : IDisposable
     {
         private readonly VulkanContext _context;
         private readonly uint _maxTextures;
         private readonly List<Texture> _textures = new();
         private readonly Dictionary<Texture, uint> _textureToIndex = new();
         private readonly ConcurrentQueue<uint> _dirtyIndices = new();
         private VkDescriptorSet _descriptorSet;
         private DescriptorPoolManager _pool;
         private Texture _defaultTexture;
         private bool _disposed;

         public BindlessTextureManager(VulkanContext context, AppSettings config)
         {
             _context = context;
             _maxTextures = config.MaxBindlessTextures;
             CreatePool();
             CreateDefaultTexture();
         }

         private void CreatePool()
         {
             var poolSize = new DescriptorPoolSize
             {
                 Type = DescriptorType.CombinedImageSampler,
                 DescriptorCount = _maxTextures
             };

             var flags = DescriptorPoolCreateFlags.UpdateAfterBindBitExt;

             unsafe
             {
                 var createInfo = new DescriptorPoolCreateInfo
                 {
                     SType = StructureType.DescriptorPoolCreateInfo,
                     Flags = flags,
                     MaxSets = 1,
                     PoolSizeCount = 1,
                     PPoolSizes = &poolSize
                 };

                 VulkanContext.Vk.CreateDescriptorPool(_context.Device, &createInfo,
                     in VulkanContext.CustomAllocator<VkDescriptorPool>(),
                     out var pool).VkAssertResult("Failed to create bindless descriptor pool");
                 _pool = new DescriptorPoolManager(_context, [poolSize], _maxTextures, DescriptorPoolCreateFlags.FreeDescriptorSetBit | DescriptorPoolCreateFlags.UpdateAfterBindBitExt)
             }
         }

         private void CreateDefaultTexture()
         {
             _defaultTexture = Texture2D.CreateColorTexture(_context, new Silk.NET.Maths.Vector4D<byte>(255,255,255,255));
             _textures.Add(_defaultTexture);
             _textureToIndex[_defaultTexture] = 0;
             _dirtyIndices.Enqueue(0);
         }

         private void AllocateDescriptorSet(VkDescriptorSetLayout descriptorSetLayout)
         {
             unsafe
             {
                 uint maxDescriptors = _maxTextures;
                 var variableCountInfo = new DescriptorSetVariableDescriptorCountAllocateInfoEXT
                 {
                     SType = StructureType.DescriptorSetVariableDescriptorCountAllocateInfoExt,
                     DescriptorSetCount = 1,
                     PDescriptorCounts = &maxDescriptors
                 };

                 var allocateInfo = new DescriptorSetAllocateInfo
                 {
                     SType = StructureType.DescriptorSetAllocateInfo,
                     DescriptorPool = _pool,
                     DescriptorSetCount = 1,
                     PSetLayouts = &descriptorSetLayout.DescriptorSetLayout
                 };
                 allocateInfo.PNext = &variableCountInfo;

                 VulkanContext.Vk.AllocateDescriptorSets(_context.Device, &allocateInfo,
                     out var descriptorSet).VkAssertResult("Failed to allocate bindless descriptor set");
                 _descriptorSet = new VkDescriptorSet(descriptorSet);
             }

             UpdateDescriptorSet(); // initial write for default texture
         }

         public uint GetOrAddTextureIndex(Texture texture)
         {
             if (texture == null) return 0;

             lock (_textureToIndex)
             {
                 if (_textureToIndex.TryGetValue(texture, out var index))
                     return index;

                 if (_textures.Count >= _maxTextures)
                     throw new InvalidOperationException($"Exceeded maximum bindless textures ({_maxTextures})");

                 uint newIndex = (uint)_textures.Count;
                 _textures.Add(texture);
                 _textureToIndex[texture] = newIndex;
                 _dirtyIndices.Enqueue(newIndex);
                 return newIndex;
             }
         }

         public void UpdateDescriptorSetIfNeeded()
         {
             if (_dirtyIndices.IsEmpty) return;
             UpdateDescriptorSet();
         }

         private unsafe void UpdateDescriptorSet()
         {
             // Collect all unique dirty indices
             var uniqueDirty = new HashSet<uint>();
             while (_dirtyIndices.TryDequeue(out var idx))
                 uniqueDirty.Add(idx);

             var writes = new List<WriteDescriptorSet>();
             var imageInfos = new List<DescriptorImageInfo>();

             foreach (var index in uniqueDirty)
             {
                 var texture = _textures[(int)index];
                 var imageView = texture.Image.GetView();
                 var sampler = Texturing.Texture.CreateSampler(_context, BaseMipLevel);

                 imageInfos.Add(new DescriptorImageInfo
                 {
                     ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                     ImageView = imageView,
                     Sampler = sampler
                 });

                 var write = new WriteDescriptorSet
                 {
                     SType = StructureType.WriteDescriptorSet,
                     DstSet = _descriptorSet,
                     DstBinding = 0,
                     DstArrayElement = index,
                     DescriptorCount = 1,
                     DescriptorType = DescriptorType.CombinedImageSampler,
                     PImageInfo = (DescriptorImageInfo*)Unsafe.AsPointer(ref imageInfos[^1])
                 };
                 writes.Add(write);
             }

             if (writes.Count > 0)
             {
                 fixed (WriteDescriptorSet* pWrites = writes.ToArray())
                 {
                     VulkanContext.Vk.UpdateDescriptorSets(_context.Device, (uint)writes.Count, pWrites, 0, null);
                 }
             }
         }

         public VkDescriptorSet GetDescriptorSet()
         {
             UpdateDescriptorSetIfNeeded();
             return _descriptorSet;
         }

         public VkDescriptorSetLayout GetLayout() => _layout;

         public void Dispose()
         {
             if (_disposed) return;
             _layout?.Dispose();
             _pool?.Dispose();
             _defaultTexture?.Dispose();
             _disposed = true;
         }
     }*/
}