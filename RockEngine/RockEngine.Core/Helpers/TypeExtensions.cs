using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;

namespace RockEngine.Core.Helpers
{
    internal static class TypeExtensions
    {
        public static int SizeOf<T>(this T _) where T : allows ref struct
        {
            return Unsafe.SizeOf<T>();
        }
    }

    internal static class FormatExtensions
    {
        public static uint GetBytesPerPixel(this Format format)
        {
            return format switch
            {
                Format.R8Unorm => 1,
                Format.R8G8Unorm => 2,
                Format.R8G8B8Unorm => 3,
                Format.R8G8B8A8Unorm => 4,
                Format.R16G16B16A16Sfloat => 8,
                Format.R32G32B32A32Sfloat => 16,
                Format.BC1RgbUnormBlock => 8, // Block compressed formats have different sizing
                Format.BC1RgbaUnormBlock => 8,
                Format.BC2UnormBlock => 16,
                Format.BC3UnormBlock => 16,
                Format.BC4UnormBlock => 8,
                Format.BC5UnormBlock => 16,
                Format.BC6HUfloatBlock => 16,
                Format.BC7UnormBlock => 16,
                _ => 4 // Default to 4 bytes per pixel
            };
        }
    }
}
