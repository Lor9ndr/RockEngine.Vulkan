using RockEngine.Core.Helpers;

using SkiaSharp;

using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Texturing
{
    public static class MipDataProvider
    {
        private static readonly string MipmapsDir = Path.Combine("Content", "Mipmaps");
        public static async Task<(nint Data, ulong Size)> LoadMipAsync(StreamableTexture texture, uint mipLevel)
        {
            var path = GetMipPath(texture, mipLevel);

            // Генерируем мипмапы если их нет
            if (!File.Exists(path))
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(texture.SourcePath);
                GenerateMipChain(texture.SourcePath,  MipmapsDir);
            }

            var data = await File.ReadAllBytesAsync(path);
            var ptr = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, ptr, data.Length);

            return (ptr, (ulong)data.Length);
        }


        private static string GetMipPath(StreamableTexture texture, uint mipLevel)
        {
            return Path.Combine(MipmapsDir, $"{Path.GetFileNameWithoutExtension(texture.SourcePath)}_mip_{mipLevel}.bin");
        }


        private static ulong CalculateMipSize(StreamableTexture texture, uint mipLevel)
        {
            var width = Math.Max(texture.Image.Width >> (int)mipLevel, 1);
            var height = Math.Max(texture.Image.Height >> (int)mipLevel, 1);
            return (ulong)(width * height * texture.Image.Format.GetBytesPerPixel());
        }


        public static void GenerateMipChain(string sourcePath, string outputDir)
        {
            using var bitmap = SKBitmap.Decode(sourcePath);
            var mipLevels = (uint)Math.Log2(Math.Max(bitmap.Width, bitmap.Height)) + 1;
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }
            for (uint i = 0; i < mipLevels; i++)
            {
                using var mip = GenerateMip(bitmap, i);
                var data = mip.Bytes;
                File.WriteAllBytes(Path.Combine(outputDir, $"{Path.GetFileNameWithoutExtension(sourcePath)}_mip_{i}.bin"), data);
            }
        }

        private static SKBitmap GenerateMip(SKBitmap source, uint mipLevel)
        {
            var scale = 1.0f / MathF.Pow(2, mipLevel);
            var width = (int)MathF.Max(source.Width * scale, 1);
            var height = (int)MathF.Max(source.Height * scale, 1);

            var scaled = new SKBitmap(width, height);
            using var canvas = new SKCanvas(scaled);

            canvas.DrawBitmap(source,
                new SKRect(0, 0, width, height));

            return scaled;
        }
    }
}