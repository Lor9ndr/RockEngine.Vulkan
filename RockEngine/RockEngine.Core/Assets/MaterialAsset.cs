
using MessagePack;

using NLog;

using RockEngine.Assets;
using RockEngine.Core.Attributes;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Core.ResourceProviders;

using System.Collections.Concurrent;

namespace RockEngine.Core.Assets
{
    [MessagePackObject]
    public sealed partial class MaterialAsset : Asset<MaterialData>, IGpuResource, IResourceProvider<Material>, IDisposable
    {
        [Key(7)]
        public override string Type => "Material";

        [SerializeIgnore]
        [IgnoreMember]
        public bool GpuReady => MaterialInstance != null;

        [SerializeIgnore]
        [IgnoreMember]
        public Material? MaterialInstance { get; private set; }

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        
        [SerializeIgnore]
        [IgnoreMember]
        private readonly ConcurrentDictionary<Guid, Texture> _loadedTextures = new();
        
        [SerializeIgnore]
        [IgnoreMember]
        private readonly SemaphoreSlim _gpuLock = new(1, 1);
        
        [SerializeIgnore]
        [IgnoreMember]
        private bool _disposed;

        // Material property accessors
        [IgnoreMember]
        public string PipelineName => Data?.PipelineName ?? "Default";

        [Key(11)]
        public List<AssetReference<TextureAsset>> Textures => Data?.Textures ?? new();
        [Key(12)]
        public Dictionary<string, object> Parameters => Data?.Parameters ?? new();

        public async ValueTask LoadGpuResourcesAsync()
        {
            if (GpuReady) return;

            await _gpuLock.WaitAsync();
            try
            {
                if (GpuReady) return;

                if (!IsDataLoaded)
                {
                    await LoadDataAsync();
                }

                await CreateMaterialAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to create material '{MaterialName}'", Name);
                throw;
            }
            finally
            {
                _gpuLock.Release();
            }
        }

        private async Task CreateMaterialAsync()
        {
            var templateManager = IoC.Container.GetInstance<MaterialTemplateManager>();
            var assetManager = IoC.Container.GetInstance<IAssetManager>();

            // Create material from template
            MaterialInstance = templateManager.CreateMaterialFromTemplate(
                Data!.PipelineName,
                Name ?? "Unnamed Material"
            );

            // Load and bind textures
            await LoadAndBindTextures();

            // Apply material parameters
            ApplyMaterialParameters();

            _logger.Debug("Created material '{MaterialName}' with template '{Template}'", Name, Data.PipelineName);
        }

        private async Task LoadAndBindTextures()
        {
            if (MaterialInstance == null || Data?.Textures == null) return;

            for (int i = 0; i < Data.Textures.Count; i++)
            {
                var textureRef = Data.Textures[i];
                try
                {
                    var textureAsset = await textureRef.GetAssetAsync();
                    if (textureAsset != null)
                    {
                        await textureAsset.LoadGpuResourcesAsync();

                        if (textureAsset.Texture != null)
                        {
                            _loadedTextures[textureRef.AssetID] = textureAsset.Texture;
                            MaterialInstance.BindResource(new TextureBinding(2, (uint)i, 0, 1,Silk.NET.Vulkan.ImageLayout.ShaderReadOnlyOptimal, textureAsset.Texture));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to load texture {ID} for material {Material}",
                        textureRef.AssetID, Name);
                }
            }
        }

        private void ApplyMaterialParameters()
        {
            if (MaterialInstance == null || Data?.Parameters == null) return;

            foreach (var param in Data.Parameters)
            {
                try
                {
                    MaterialInstance.PushConstant(param.Key,param.Value);
                    
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to set parameter '{Param}' for material '{Material}'",
                        param.Key, Name);
                }
            }
        }

        public void UpdateParameter(string name, object value)
        {
            if (Data?.Parameters != null)
            {
                Data.Parameters[name] = value;
                // Update GPU if loaded
                MaterialInstance?.PushConstant(name, value);
            }
        }

        public void AddTexture(AssetReference<TextureAsset> textureRef, string slotName = "")
        {
            if (Data != null)
            {
                Data.Textures.Add(textureRef);
                UpdateModified();
            }
        }

        public void RemoveTexture(Guid textureId)
        {
            if (Data != null)
            {
                Data.Textures.RemoveAll(t => t.AssetID == textureId);
                UpdateModified();
            }
        }

        public void SetPipeline(string pipelineName)
        {
            if (Data != null)
            {
                Data.PipelineName = pipelineName;
                UpdateModified();

                // Recreate material if loaded
                if (MaterialInstance != null)
                {
                    UnloadGpuResources();
                }
            }
        }

        public void UnloadGpuResources()
        {
            _gpuLock.Wait();
            try
            {
                MaterialInstance?.Dispose();
                MaterialInstance = null;
                _loadedTextures.Clear();
            }
            finally
            {
                _gpuLock.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            UnloadGpuResources();
            _gpuLock.Dispose();
            _disposed = true;
        }

        public async ValueTask<Material> GetAsync()
        {
            if (MaterialInstance != null) return MaterialInstance;

            await LoadGpuResourcesAsync();
            return MaterialInstance!;
        }

        public override void UnloadData()
        {
            base.UnloadData();
            UnloadGpuResources();
        }
    }
}
