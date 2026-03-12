using RockEngine.Core.Internal;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

internal class StorageImageBinding : ResourceBinding
{
    private Texture[] _textures;
    private readonly ImageLayout _layout;
    private readonly uint _mipLevel;
    private readonly uint _levelCount;  
    private readonly uint _arrayLayer;
    private readonly uint _layerCount;

    public override DescriptorType DescriptorType => DescriptorType.StorageImage;
    public Texture[] Textures => _textures;
    public ImageLayout Layout => _layout;

    // Constructor for array of textures (each bound as a whole, no per‑texture layer control)
    public StorageImageBinding(Texture[] textures, uint setLocation, uint bindingLocation, ImageLayout layout)
        : base(setLocation, new UIntRange(bindingLocation, (uint)(bindingLocation + textures.Length - 1)))
    {
        _textures = textures;
        _layout = layout;
        _mipLevel = 0;
        _levelCount = Vk.RemainingMipLevels;
        _arrayLayer = 0;
        _layerCount = 1;
    }

    // Constructor for single texture with full control
    public StorageImageBinding(
        Texture texture,
        uint setLocation,
        uint bindingLocation,
        ImageLayout layout,
        uint mipLevel = 0,
        uint levelCount = Vk.RemainingMipLevels,   // new parameter
        uint arrayLayer = 0,
        uint layerCount = 1)
        : base(setLocation, new UIntRange(bindingLocation, bindingLocation))
    {
        _layout = layout;
        _textures = [texture];
        _mipLevel = mipLevel;
        _levelCount = levelCount;
        _arrayLayer = arrayLayer;
        _layerCount = layerCount;
    }

    public unsafe override void UpdateDescriptorSet(VulkanContext context, uint frameIndex, VkDescriptorSetLayout descriptorSetLayout)
    {
        var descriptor = GetDescriptorSetForLayout(descriptorSetLayout, frameIndex);
        var imageInfos = stackalloc DescriptorImageInfo[_textures.Length];
        var writes = stackalloc WriteDescriptorSet[_textures.Length];

        for (int i = 0; i < _textures.Length; i++)
        {
            var texture = _textures[i];

            var imageView = texture.Image.GetView(
                baseMipLevel: _mipLevel,
                levelCount: _levelCount,
                baseArrayLayer: _arrayLayer,
                layerCount: _layerCount
            );

            imageInfos[i] = new DescriptorImageInfo
            {
                ImageView = imageView,
                ImageLayout = _layout,
                Sampler = default
            };

            writes[i] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptor,
                DstBinding = BindingLocation.Start + (uint)i,
                DstArrayElement = 0,
                DescriptorType = DescriptorType,
                DescriptorCount = 1,
                PImageInfo = &imageInfos[i]
            };
        }

        VulkanContext.Vk.UpdateDescriptorSets(context.Device, (uint)_textures.Length, writes, 0, null);
    }

    public override StorageImageBinding Clone()
    {
        if (_textures.Length > 1)
        {
            return new StorageImageBinding(
                (Texture[])_textures.Clone(),
                SetLocation,
                BindingLocation.Start,
                _layout
            );
        }
        else
        {
            return new StorageImageBinding(
                _textures[0],
                SetLocation,
                BindingLocation.Start,
                _layout,
                _mipLevel,
                _levelCount,
                _arrayLayer,
                _layerCount
            );
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing) { }
            _textures = [];
            _descriptorSetsByLayout.Clear();
            _isDisposed = true;
        }
    }
}