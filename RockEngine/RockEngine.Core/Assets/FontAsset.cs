using MemoryPack;
using NLog;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.FontRendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Core.Rendering.Texturing.Atlasing;
using RockEngine.Vulkan;

namespace RockEngine.Core.Assets
{
    [MemoryPackable]
    public sealed partial class FontAsset : Asset<FontData>, IGpuResource, IDisposable
    {
        public override string Type => "Font";

        [MemoryPackIgnore]
        public FontAtlas? FontAtlas { get; private set; }

        [MemoryPackIgnore]
        public bool GpuReady => FontAtlas is not null;

        private Atlas? _atlas;
        private readonly SemaphoreSlim _gpuLock = new(1, 1);
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
        private bool _disposed;

        public async ValueTask LoadGpuResourcesAsync()
        {
            if (GpuReady)
            {
                return;
            }

            await _gpuLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (GpuReady)
                {
                    return;
                }

                if (!IsDataLoaded)
                {
                    await LoadDataAsync().ConfigureAwait(false);
                }

                if (Data is null)
                {
                    throw new InvalidOperationException("FontData is not loaded.");
                }

                var vk = IoC.Container.GetInstance<VulkanContext>();
                var globalTextureArray = IoC.Container.GetInstance<GlobalTextureArray>();  // see note below

                // Create the atlas page (bindless registration callback)
                _atlas = new Atlas(
                    vk,
                    (uint)Data.AtlasWidth,
                    (uint)Data.AtlasHeight,
                    TextureFormat.R8Unorm,                     // single channel glyph atlas
                    new MaxRectsAllocator(),
                    tex => (nint)globalTextureArray.AllocateIndex(tex) // returns IntPtr bindless index
                );

                var submitContext = vk.GraphicsSubmitContext;

                FontAtlas = new FontAtlas(
                    _atlas,
                    Data.FontFilePath.ToString(),
                    Data.FontSize,
                    Data.Characters.Distinct(),                // avoid duplicate work
                    submitContext
                );

                Logger.Info("Font '{Name}' loaded ({GlyphCount} glyphs)", Name, FontAtlas.Glyphs.Count);
            }
            finally
            {
                _gpuLock.Release();
            }
        }

        public void UnloadGpuResources()
        {
            _gpuLock.Wait();
            try
            {
                FontAtlas?.Dispose();
                FontAtlas = null;
                _atlas?.Dispose();
                _atlas = null;
            }
            finally
            {
                _gpuLock.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            UnloadGpuResources();
            _gpuLock.Dispose();
            _disposed = true;
        }

        // Convenience method to set data and mark dirty
        public void Configure(string fontPath, float size, string? characters = null)
        {
            Data ??= new FontData();
            Data.FontFilePath = fontPath;
            Data.FontSize = size;
            if (characters is not null)
            {
                Data.Characters = characters;
            }

            UpdateModified();
        }
    }
}