
using System.Collections.Concurrent;
using MessagePack;
using NLog;
using RockEngine.Core.Attributes;
using RockEngine.Core.DI;
using RockEngine.Core.Info;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Core.ResourceProviders;
using Silk.NET.Vulkan;

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
        public Dictionary<string, AssetReference<TextureAsset>> Textures => Data.Textures;

        [Key(12)]
        public Dictionary<string, object> Parameters => Data?.Parameters ?? new();

        
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

                await CreateMaterialAsync().ConfigureAwait(false);
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

            // Create material from template
            MaterialInstance = templateManager.CreateMaterialFromTemplate(
                Data!.PipelineName,
                Name ?? "Unnamed Material"
            );

            // Load and bind textures
            await LoadAndBindTextures().ConfigureAwait(false);

            // Apply material parameters
            ApplyMaterialParameters();

            _logger.Debug("Created material '{MaterialName}' with template '{Template}'", Name, Data.PipelineName);
        }

        
        private async Task LoadAndBindTextures()
        {
            if (MaterialInstance == null || Data?.Textures == null)
            {
                return;
            }

            // Get the list of expected texture resources from the material
            var expectedTextures = MaterialInstance.Passes.SelectMany(pass => pass.Value.ExpectedResources
                .Where(kv => kv.Value.Image.HasValue)
                .Select(kv => kv.Key))
                .ToList();

            // If no expected resources are known, fall back to low‑level binding (legacy)
            if (expectedTextures.Count == 0)
            {
                // Fallback to direct binding (as before)
                int i = 0;
                foreach (var texture in Data.Textures)
                {
                    try
                    {
                        var textureRef = texture.Value;
                        var textureAsset = await textureRef.GetAssetAsync().ConfigureAwait(false);
                        if (textureAsset?.Texture != null)
                        {
                            _loadedTextures[textureRef.AssetID] = textureAsset.Texture;
                            MaterialInstance.BindResource(new TextureBinding(
                                MaterialInfo.TEXTURE_SET, (uint)i, 0, 1,
                                ImageLayout.ShaderReadOnlyOptimal, textureAsset.Texture));
                        }
                        i++;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to load texture {AssetID}", texture.Value.AssetID);
                    }

                }

                return;
            }

            var globalArray = IoC.Container.GetInstance<GlobalTextureArray>();

            foreach (var pass in MaterialInstance.Passes.Values)
            {
                if (pass.UsesBindlessTextures())
                {
                    // Get the ordered texture slot names from push constants
                    var slotNames = pass.GetTextureSlotNamesFromPushConstants();
                    if (slotNames.Count == 0)
                    {
                        continue;
                    }

                    // Build texture array in the same order as slotNames
                    var textures = new List<Texture>();
                    foreach (var slot in slotNames)
                    {

                        if (Data.Textures.TryGetValue(slot, out var texRef))
                        {
                            try
                            {
                                var texAsset = await texRef.GetAssetAsync().ConfigureAwait(false);
                                await texAsset.LoadGpuResourcesAsync().ConfigureAwait(false);
                                textures.Add(texAsset.Texture);
                                var index = globalArray.AllocateIndex(texAsset.Texture);
                                pass.PushConstant($"{slot}Index", index);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Failed to load texture {AssetID}", texRef.AssetID);
                            }

                        }
                        else
                        {
                            _logger.Warn($"Material '{Name}' missing texture for slot '{slot}'");
                            // HAVE TO HANDLE SOMEHOW
                            textures.Add(null);
                        }

                    }

                    // Set push constant indices for each slot
                    for (int i = 0; i < slotNames.Count; i++)
                    {
                        string slot = slotNames[i];
                    }
                }
                else
                {

                    foreach (var item in pass.ExpectedResources)
                    {
                        if (Data.Textures.TryGetValue(item.Key, out var assetRef))
                        {
                            var texAsset = await assetRef.GetAssetAsync().ConfigureAwait(false);
                            await texAsset.LoadGpuResourcesAsync().ConfigureAwait(false);
                            var binding = new TextureBinding(
                                   setLocation: item.Value.Set,
                                   bindingLocation: item.Value.Reflection.Binding,
                                   baseMipLevel: 0,
                                   levelCount: Vk.RemainingMipLevels,
                                   imageLayout: ImageLayout.ShaderReadOnlyOptimal,
                                   textures: texAsset.Texture!
                             );
                            pass.BindResource(binding);
                        }
                    }
                }
            }
        }

        private void ApplyMaterialParameters()
        {
            if (MaterialInstance == null || Data?.Parameters == null)
            {
                return;
            }

            foreach (var param in Data.Parameters)
            {
                try
                {
                    MaterialInstance.SetPushConstant<object>(param.Key, param.Value);

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
                MaterialInstance?.SetPushConstant(name, value);
            }
        }

        public void AddTexture(AssetReference<TextureAsset> textureRef, string slotName)
        {
            if (Data != null)
            {
                Data.Textures[slotName] = textureRef;
                UpdateModified();
            }
        }

        public void RemoveTexture(string slot)
        {
            if (Data != null)
            {
                Data.Textures.Remove(slot);
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
            if (_disposed)
            {
                return;
            }

            UnloadGpuResources();
            _gpuLock.Dispose();
            _disposed = true;
        }

        
        public async ValueTask<Material> GetAsync()
        {
            if (MaterialInstance != null)
            {
                return MaterialInstance;
            }

            await LoadGpuResourcesAsync().ConfigureAwait(false);
            return MaterialInstance!;
        }

        public override void UnloadData()
        {
            base.UnloadData();
            UnloadGpuResources();
        }
    }
}
