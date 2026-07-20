using MemoryPack;

using RockEngine.Core.Assets;

using Silk.NET.Vulkan;

using SkiaSharp;

namespace RockEngine.Core.Rendering.Texturing
{
    public enum TextureDimension
    {
        Texture2D,
        TextureCube,
        Texture3D,
        TextureArray,
        TextureCubeArray
    }

    public enum TextureFormat
    {
        R8G8B8A8Unorm,
        R8G8B8A8Srgb,
        B8G8R8A8Unorm,
        B8G8R8A8Srgb,
        R32G32B32A32Float,
        D32Float,
        D24UnormS8Uint,
        R8Unorm,
        // Add other formats as needed
    }

    public enum TextureFilter
    {
        Nearest,
        Linear,
    }

    public enum TextureWrap
    {
        Repeat,
        MirroredRepeat,
        ClampToEdge,
        ClampToBorder
    }

    [Flags]
    public enum TextureUsage
    {
        None = 0,
        Sampled = 1,
        Storage = 2,
        ColorAttachment = 4,
        DepthStencilAttachment = 8,
        TransferSrc = 16,
        TransferDst = 32,
        TransientAttachment = 64
    }

    [Flags]
    public enum MemoryFlags
    {
        None = 0,
        DeviceLocal = 1,
        HostVisible = 2,
        HostCoherent = 4,
        HostCached = 8
    }
    [MemoryPackable]
    public partial struct SamplerState
    {
        public static SamplerState Default => new()
        {
            MinFilter = TextureFilter.Linear,
            MagFilter = TextureFilter.Linear,
            MipFilter = TextureFilter.Linear,
            AddressModeU = TextureWrap.Repeat,
            AddressModeV = TextureWrap.Repeat,
            AddressModeW = TextureWrap.Repeat,
            MipLodBias = 0.0f,
            MaxAnisotropy = 1.0f,
            CompareOp = CompareOp.Always,
            MinLod = 0.0f,
            MaxLod = Vk.RemainingMipLevels,
            BorderColor = BorderColor.FloatOpaqueBlack,
            UnnormalizedCoordinates = false,
            CompareEnable = true,
            AnisotropyEnable = true
        };

        public TextureFilter MinFilter { get; set; }
        public TextureFilter MagFilter { get; set; }
        public TextureFilter MipFilter { get; set; }
        public TextureWrap AddressModeU { get; set; }
        public TextureWrap AddressModeV { get; set; }
        public TextureWrap AddressModeW { get; set; }
        public float MipLodBias { get; set; }
        public float MaxAnisotropy { get; set; }
        public CompareOp CompareOp { get; set; }
        public float MinLod { get; set; }
        public float MaxLod { get;set;}
        public BorderColor BorderColor { get; set; }
        public bool UnnormalizedCoordinates { get; set; }
        public bool CompareEnable { get; set; }
        public bool AnisotropyEnable { get; set; }

        internal void SetMaxLod(uint actualLoadedMipLevels)
        {
            MaxLod = actualLoadedMipLevels;
        }
    }

    [MemoryPackable]
    public partial class TextureData : ITextureData
    {
        [MemoryPackConstructor]
        public TextureData(
           TextureDimension dimension,
           TextureFormat format,
           uint width,
           uint height,
           uint depth,
           uint mipLevels,
           uint arrayLayers,
           bool generateMipmaps,
           SamplerState sampler,
           TextureUsage usage,
           MemoryFlags memoryFlags,
           List<string> filePaths,
           bool flipVertically,
           bool convertToSrgb,
           string name)
        {
            Dimension = dimension;
            Format = format;
            Width = width;
            Height = height;
            Depth = depth;
            MipLevels = mipLevels;
            ArrayLayers = arrayLayers;
            GenerateMipmaps = generateMipmaps;
            Sampler = sampler;
            Usage = usage;
            MemoryFlags = memoryFlags;
            FilePaths = filePaths;
            FlipVertically = flipVertically;
            ConvertToSrgb = convertToSrgb;
            Name = name;
        }


        public TextureData(TextureDimension dimension,
                           TextureFormat format,
                           uint width,
                           uint height,
                           uint depth,
                           uint mipLevels,
                           bool generateMipmaps,
                           SamplerState sampler,
                           TextureUsage usage,
                           MemoryFlags memoryFlags,
                           List<string> filePaths,
                           bool flipVertically,
                           bool convertToSrgb,
                           string name)
        {
            Dimension = dimension;
            Format = format;
            Width = width;
            Height = height;
            Depth = depth;
            MipLevels = mipLevels;
            GenerateMipmaps = generateMipmaps;
            Sampler = sampler;
            Usage = usage;
            MemoryFlags = memoryFlags;
            FilePaths = filePaths;
            FlipVertically = flipVertically;
            ConvertToSrgb = convertToSrgb;
            Name = name;
        }

        public TextureData()
        {
        }

        public TextureDimension Dimension { get; set; } = TextureDimension.Texture2D;
        public TextureFormat Format { get; set; } = TextureFormat.R8G8B8A8Unorm;
        public uint Width { get; set; } = 1;
        public uint Height { get; set; } = 1;
        public uint Depth { get; set; } = 1;
        public uint MipLevels { get; set; } = 1;
        public bool GenerateMipmaps { get; set; } = false;
        public SamplerState Sampler { get; set; } = SamplerState.Default;
        public TextureUsage Usage { get; set; } = TextureUsage.Sampled | TextureUsage.TransferDst | TextureUsage.TransferSrc;
        public MemoryFlags MemoryFlags { get; set; } = MemoryFlags.DeviceLocal;
        public List<string> FilePaths { get; set; } = new();
        public bool FlipVertically { get; set; } = false;
        public bool ConvertToSrgb { get; set; } = true;
        public uint ArrayLayers { get; set; } = 1;

        // Additional metadata
        public string Name { get; set; } = string.Empty;

        
        public bool IsCubeMap => Dimension == TextureDimension.TextureCube ||
                                      Dimension == TextureDimension.TextureCubeArray;

        
        public bool IsArray => Dimension == TextureDimension.TextureArray ||
                               Dimension == TextureDimension.TextureCubeArray;

        private static TextureFormat GetSrgbFormat(TextureFormat format)
        {
            return format switch
            {
                TextureFormat.R8G8B8A8Unorm => TextureFormat.R8G8B8A8Srgb,
                TextureFormat.B8G8R8A8Unorm => TextureFormat.B8G8R8A8Srgb,
                _ => format
            };
        }

        private static TextureFilter ConvertFilter(TextureFilter filter)
        {
            return filter switch
            {
                TextureFilter.Nearest => TextureFilter.Nearest,
                TextureFilter.Linear => TextureFilter.Linear,
                _ => TextureFilter.Linear
            };
        }

        /// <summary>
        /// Automatically calculates mip levels if GenerateMipmaps is true and MipLevels == 0.
        /// Call this after Width/Height are set.
        /// </summary>
        public uint EnsureMipLevels()
        {
            if (GenerateMipmaps && MipLevels == 0)
            {
                uint maxDim = Math.Max(Width, Math.Max(Height, Depth));
                MipLevels = (uint)Math.Floor(Math.Log2(maxDim)) + 1;
            }
            else if (!GenerateMipmaps || MipLevels == 0)
            {
                MipLevels = 1;
            }
            return MipLevels;
        }

        // Helper methods
        public Format GetVulkanFormat()
        {
            var baseFormat = Format switch
            {
                TextureFormat.R8G8B8A8Unorm => Silk.NET.Vulkan.Format.R8G8B8A8Unorm,
                TextureFormat.R8G8B8A8Srgb => Silk.NET.Vulkan.Format.R8G8B8A8Srgb,
                TextureFormat.B8G8R8A8Unorm => Silk.NET.Vulkan.Format.B8G8R8A8Unorm,
                TextureFormat.B8G8R8A8Srgb => Silk.NET.Vulkan.Format.B8G8R8A8Srgb,
                TextureFormat.R32G32B32A32Float => Silk.NET.Vulkan.Format.R32G32B32A32Sfloat,
                TextureFormat.D32Float => Silk.NET.Vulkan.Format.D32Sfloat,
                TextureFormat.D24UnormS8Uint => Silk.NET.Vulkan.Format.D24UnormS8Uint,
                TextureFormat.R8Unorm => Silk.NET.Vulkan.Format.R8Unorm,
                _ => throw new NotImplementedException()
            };

            // If SRGB conversion is requested and the format has a direct SRGB variant, use it.
            if (ConvertToSrgb)
            {
                return baseFormat switch
                {
                    Silk.NET.Vulkan.Format.R8G8B8A8Unorm => Silk.NET.Vulkan.Format.R8G8B8A8Srgb,
                    Silk.NET.Vulkan.Format.B8G8R8A8Unorm => Silk.NET.Vulkan.Format.B8G8R8A8Srgb,
                    Silk.NET.Vulkan.Format.R8Unorm => Silk.NET.Vulkan.Format.R8Srgb,
                    _ => baseFormat
                };
            }

            return baseFormat;
        }
        public static Format GetVulkanFormat(SKColorType colorType, bool srgb)
        {
            return (colorType, srgb) switch
            {
                (SKColorType.Rgba8888, false) => Silk.NET.Vulkan.Format.R8G8B8A8Unorm,
                (SKColorType.Rgba8888, true) => Silk.NET.Vulkan.Format.R8G8B8A8Srgb,
                (SKColorType.Bgra8888, false) => Silk.NET.Vulkan.Format.B8G8R8A8Unorm,
                (SKColorType.Bgra8888, true) => Silk.NET.Vulkan.Format.B8G8R8A8Srgb,
                (SKColorType.Gray8, _) => Silk.NET.Vulkan.Format.R8Unorm,      // single channel
                                                                               // Add more mappings as needed
                _ => throw new NotSupportedException($"Unsupported SKColorType: {colorType}")
            };
        }
        public static TextureFormat FromSKFormat(SKColorType colorType, bool srgb)
        {
            return (colorType, srgb) switch
            {
                (SKColorType.Rgba8888, false) => TextureFormat.R8G8B8A8Unorm,
                (SKColorType.Rgba8888, true) => TextureFormat.R8G8B8A8Srgb,
                (SKColorType.Bgra8888, false) => TextureFormat.B8G8R8A8Unorm,
                (SKColorType.Bgra8888, true) => TextureFormat.B8G8R8A8Srgb,
                (SKColorType.Gray8, _) => TextureFormat.R8Unorm,      // single channel
                                                                      // Add more mappings as needed
                _ => throw new NotSupportedException($"Unsupported SKColorType: {colorType}")
            };
        }

        public ImageUsageFlags GetVulkanUsageFlags()
        {
            ImageUsageFlags flags = ImageUsageFlags.None;

            if (Usage.HasFlag(TextureUsage.Sampled))
            {
                flags |= ImageUsageFlags.SampledBit;
            }

            if (Usage.HasFlag(TextureUsage.Storage))
            {
                flags |= ImageUsageFlags.StorageBit;
            }

            if (Usage.HasFlag(TextureUsage.ColorAttachment))
            {
                flags |= ImageUsageFlags.ColorAttachmentBit;
            }

            if (Usage.HasFlag(TextureUsage.DepthStencilAttachment))
            {
                flags |= ImageUsageFlags.DepthStencilAttachmentBit;
            }

            if (Usage.HasFlag(TextureUsage.TransferSrc))
            {
                flags |= ImageUsageFlags.TransferSrcBit;
            }

            if (Usage.HasFlag(TextureUsage.TransferDst))
            {
                flags |= ImageUsageFlags.TransferDstBit;
            }

            if (Usage.HasFlag(TextureUsage.TransientAttachment))
            {
                flags |= ImageUsageFlags.TransientAttachmentBit;
            }

            return flags;
        }

        public MemoryPropertyFlags GetVulkanMemoryFlags()
        {
            MemoryPropertyFlags flags = MemoryPropertyFlags.None;

            if (MemoryFlags.HasFlag(MemoryFlags.DeviceLocal))
            {
                flags |= MemoryPropertyFlags.DeviceLocalBit;
            }

            if (MemoryFlags.HasFlag(MemoryFlags.HostVisible))
            {
                flags |= MemoryPropertyFlags.HostVisibleBit;
            }

            if (MemoryFlags.HasFlag(MemoryFlags.HostCoherent))
            {
                flags |= MemoryPropertyFlags.HostCoherentBit;
            }

            if (MemoryFlags.HasFlag(MemoryFlags.HostCached))
            {
                flags |= MemoryPropertyFlags.HostCachedBit;
            }

            return flags;
        }


        public uint CalculateMipLevels()
        {
            if (!GenerateMipmaps)
            {
                return 1;
            }

            uint maxDimension = Math.Max(Width, Math.Max(Height, Depth));
            return (uint)Math.Floor(Math.Log2(maxDimension)) + 1;
        }

        public bool Validate()
        {
            //if (Width == 0 || Height == 0) return false;
            if (IsCubeMap && FilePaths.Count != 6)
            {
                return false;
            }

            if (IsArray && Depth == 0)
            {
                return false;
            }

            return true;
        }

        public TextureFormat GetEffectiveFormat()
        {
            return GetSrgbFormat(Format);
        }
    }
}