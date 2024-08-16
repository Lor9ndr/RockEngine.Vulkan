namespace RockEngine.Vulkan.VkObjects.Infos.Texture
{
    public abstract record TextureInfo;

    /// <summary>
    /// Info of not loaded texture into the engine
    /// </summary>
    /// <param name="Path">Path where it is located</param>
    public record NotLoadedTextureInfo(string Path) : TextureInfo;

    /// <summary>
    /// Loaded to the engine texture
    /// </summary>
    /// <param name="Image">vulkan image</param>
    /// <param name="ImageMemory">vulkan image memory</param>
    /// <param name="ImageView">vulkan image view</param>
    /// <param name="Sampler">vulkan sampler</param>
    public record LoadedTextureInfo(
        Image Image,
        DeviceMemory ImageMemory,
        ImageView ImageView,
        Sampler Sampler
    ) : TextureInfo;

    /// <summary>
    /// Loaded to the engine texture and used in the binded shaders
    /// </summary>
    /// <param name="LoadedInfo">Previous state of the texture</param>
    /// <param name="DescriptorSet">vulkan descriptor set to which it has been binded</param>
    public record ShaderUsedTextureInfo(
       LoadedTextureInfo LoadedInfo,
       DescriptorSetWrapper DescriptorSet
        ) : LoadedTextureInfo(LoadedInfo.Image, LoadedInfo.ImageMemory, LoadedInfo.ImageView, LoadedInfo.Sampler);

}
