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
                Format.R8G8B8A8Unorm => 4,
                Format.B8G8R8A8Unorm => 4,
                Format.R8G8B8A8Srgb => 4,
                Format.R8Unorm => 1,
                Format.R32G32B32A32Sfloat => 16,
                _ => throw new NotSupportedException($"Unsupported format: {format}")
            };
        }
    }
}
