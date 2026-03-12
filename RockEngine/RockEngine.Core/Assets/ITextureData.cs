using RockEngine.Core.Rendering.Texturing;

namespace RockEngine.Core.Assets
{
    /// <summary>
    /// Base interface for all texture data (API-agnostic)
    /// </summary>
    public interface ITextureData
    {
        /// <summary>
        /// Texture dimension/type
        /// </summary>
        TextureDimension Dimension { get; set; }

        /// <summary>
        /// Pixel format
        /// </summary>
        TextureFormat Format { get; set; }

        /// <summary>
        /// Width in pixels
        /// </summary>
        uint Width { get; set; }

        /// <summary>
        /// Height in pixels
        /// </summary>
        uint Height { get; set; }

        /// <summary>
        /// Depth in pixels (for 3D textures) or array layers (for array textures)
        /// </summary>
        uint Depth { get; set; }

        /// <summary>
        /// Number of mipmap levels
        /// </summary>
        uint MipLevels { get; set; }

        /// <summary>
        /// Whether to generate mipmaps
        /// </summary>
        bool GenerateMipmaps { get; set; }

        /// <summary>
        /// Sampler state for the texture
        /// </summary>
        SamplerState Sampler { get; set; }

        /// <summary>
        /// Texture usage flags
        /// </summary>
        TextureUsage Usage { get; set; }

        /// <summary>
        /// Memory allocation flags
        /// </summary>
        MemoryFlags MemoryFlags { get; set; }

        /// <summary>
        /// File paths for texture data (for file-based textures)
        /// </summary>
        List<string> FilePaths { get; set; }

        /// <summary>
        /// Whether the texture should be flipped vertically when loaded
        /// </summary>
        bool FlipVertically { get; set; }

        /// <summary>
        /// Whether to convert from linear to sRGB space
        /// </summary>
        bool ConvertToSrgb { get; set; }

        /// <summary>
        /// Gets the appropriate format considering sRGB conversion
        /// </summary>
        TextureFormat GetEffectiveFormat();

        /// <summary>
        /// Calculates the appropriate number of mip levels
        /// </summary>
        uint CalculateMipLevels();

        /// <summary>
        /// Validates the texture data
        /// </summary>
        bool Validate();
    }
}