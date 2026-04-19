using RockEngine.Core.Internal;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.ResourceBindings
{
    public class TextureBinding : ResourceBinding, IDisposable
    {
        private readonly uint _arrayLayer;
        private readonly uint _layerCount;
        private readonly List<IDisposable> _subscriptionTokens = new();

        public Texture?[] Textures { get; private set; }
        public uint BaseMipLevel { get; }
        public uint LevelCount { get; }
        public override DescriptorType DescriptorType => DescriptorType.CombinedImageSampler;
        public ImageLayout ImageLayout { get; }

        public TextureBinding(
            uint setLocation,
            uint bindingLocation,
            uint baseMipLevel,
            uint levelCount,
            ImageLayout imageLayout,
            uint arrayLayer,
            uint layerCount,
            params Texture[] textures)
            : base(setLocation, new UIntRange(bindingLocation, (uint)(bindingLocation + textures.Length - 1)))
        {
            BaseMipLevel = baseMipLevel;
            LevelCount = levelCount;
            ImageLayout = imageLayout;
            Textures = textures;
            _arrayLayer = arrayLayer;
            _layerCount = layerCount;

            // Подписываемся на каждую текстуру через новую систему
            for (int i = 0; i < textures.Length; i++)
            {
                if (textures[i] != null)
                {
                    var observer = new TextureObserver(this, i);
                    var token = textures[i].Subscribe(observer);
                    _subscriptionTokens.Add(token);
                }
            }
        }
        public TextureBinding(
            uint setLocation,
            uint bindingLocation,
            uint baseMipLevel,
            uint levelCount,
            ImageLayout imageLayout,
            params Texture[] textures)
            : this(setLocation, bindingLocation, baseMipLevel, levelCount, imageLayout, 0, Vk.RemainingArrayLayers, textures)
        {
        }
        public TextureBinding(
            uint setLocation,
            uint bindingLocation,
            uint baseMipLevel,
            uint levelCount,
            ImageLayout imageLayout,
            Texture texture)
            : this(setLocation, bindingLocation, baseMipLevel, levelCount, imageLayout, 0, texture.Image.ArrayLayers, texture)
        {
        }


        private void MarkDirty()
        {
            foreach (var setList in _descriptorSetsByLayout.Values)
            {
                foreach (var set in setList)
                {
                    set?.IsDirty = true;
                }
            }
        }

        // Внутренний наблюдатель, реализующий IResourceObserver
        private sealed class TextureObserver : IResourceObserver
        {
            private readonly TextureBinding _binding;
            private readonly int _index;

            public TextureObserver(TextureBinding binding, int index)
            {
                _binding = binding;
                _index = index;
            }

            public void OnResourceChanged(ulong resourceId, ResourceChangeType changeType)
            {
                switch (changeType)
                {
                    case ResourceChangeType.DataUpdated:
                    case ResourceChangeType.Resized:
                        // Изменилось содержимое или размер – нужно перезаписать дескриптор
                        _binding.MarkDirty();
                        break;

                    case ResourceChangeType.Disposed:
                        // Текстура уничтожена – зануляем слот, чтобы не использовать её в дескрипторе
                        if (_index < _binding.Textures.Length)
                        {
                            _binding.Textures[_index] = null;
                        }

                        _binding.MarkDirty();
                        break;
                }
            }
        }

        public override unsafe void UpdateDescriptorSet(VulkanContext context, uint frameIndex, VkDescriptorSetLayout descriptorSetLayout)
        {
            var descriptor = GetDescriptorSetForLayout(descriptorSetLayout, frameIndex);

            // Подсчитываем количество живых текстур (не null)
            int validCount = 0;
            for (int i = 0; i < Textures.Length; i++)
            {
                if (Textures[i] != null)
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                return; // нечего обновлять
            }

            // Используем stackalloc, если массив небольшой, иначе можно пул
            var writes = stackalloc WriteDescriptorSet[validCount];
            var imageInfos = stackalloc DescriptorImageInfo[validCount];

            int writeIdx = 0;
            for (int i = 0; i < Textures.Length; i++)
            {
                var texture = Textures[i];
                if (texture == null)
                {
                    continue;
                }

                uint maxMip = Math.Min(texture.TotalMipLevels, BaseMipLevel + LevelCount);
                var imageView = texture.Image.GetView(
                    baseMipLevel: BaseMipLevel,
                    levelCount: LevelCount,
                    baseArrayLayer: _arrayLayer,
                    layerCount: _layerCount
                );

                imageInfos[writeIdx] = new DescriptorImageInfo
                {
                    ImageLayout = ImageLayout,
                    ImageView = imageView,
                    Sampler = Texture.CreateSampler(context, maxMip)
                };

                writes[writeIdx] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = descriptor,
                    DstBinding = (uint)(BindingLocation.Start + i), // важно: используем исходный индекс связки, а не writeIdx
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType,
                    DescriptorCount = 1,
                    PImageInfo = &imageInfos[writeIdx]
                };
                writeIdx++;
            }

            VulkanContext.Vk.UpdateDescriptorSets(context.Device, (uint)validCount, writes, 0, null);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Отписываемся от всех текстур через токены
                foreach (var token in _subscriptionTokens)
                {
                    token.Dispose();
                }

                _subscriptionTokens.Clear();
                Textures = Array.Empty<Texture>();
                _descriptorSetsByLayout.Clear();
            }
        }

        public override TextureBinding Clone()
        {
            // Клонируем массив текстур (поверхностно) – токены не копируются, их нужно будет создать заново
            var clonedTextures = (Texture[])Textures.Clone();
            return new TextureBinding(
                SetLocation,
                BindingLocation.Start,
                BaseMipLevel,
                LevelCount,
                ImageLayout,
                _arrayLayer,
                _layerCount,
                clonedTextures
            );
        }

        ~TextureBinding()
        {
            Dispose(false);
        }
    }
}