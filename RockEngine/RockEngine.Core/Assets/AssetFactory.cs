using Assimp;

using RockEngine.Assets;
using RockEngine.Core.DI;
using RockEngine.Core.Extensions;
using RockEngine.Core.Rendering.Texturing;

namespace RockEngine.Core.Assets
{
    public class AssetFactory(AssimpLoader assimpLoader, IAssetRepository assetRepository) : IAssetFactory
    {
        private readonly AssimpLoader _assimpLoader = assimpLoader;
        private readonly IAssetRepository _assetRepository = assetRepository;

        public T Create<T>(AssetPath path, string? name = null) where T : IAsset
        {
            return (T)Create(path, typeof(T), name);
        }
        public IAsset Create(AssetPath path, Type type, string? name = null) 
        {
            var asset = (IAsset)IoC.Container.GetInstance(type);
            asset.Path = path;
            asset.Name = name ?? Path.GetFileNameWithoutExtension(path.FullPath);
            return asset;
        }

        public IAsset CreateMaterial(string name, string template, List<IAssetReference<IAsset>>? textures = null, Dictionary<string, object>? parameters = null)
        {
            var material = Create<MaterialAsset>(new AssetPath("Materials", name));
            material.SetData(new MaterialData
            {
                PipelineName = template,
                Textures = textures is null ? [] : textures.Select(s => (AssetReference<TextureAsset>)s).ToList(),
                Parameters = parameters is null ? [] : parameters
            });
            return material;
        }

        public async Task<IAsset> CreateModelFromFileAsync(string filePath, string? modelName = null, string parentPath = "Models")
        {
            modelName ??= Path.GetFileNameWithoutExtension(filePath);
            var meshesData = await _assimpLoader.LoadMeshesAsync(filePath);
            var modelAsset = Create<ModelAsset>(new AssetPath(parentPath, modelName));

            var textureCache = new Dictionary<string, TextureAsset>(StringComparer.OrdinalIgnoreCase);

            foreach (var meshData in meshesData)
            {
                var meshName = !string.IsNullOrEmpty(meshData.Name) ? meshData.Name : $"Mesh_{Guid.NewGuid()}";
                var meshAsset = Create<MeshAsset>(
                    new AssetPath($"{parentPath}/{modelName}/Meshes", meshName),
                    meshName);

                meshAsset.SetGeometry(meshData.Vertices, meshData.Indices);

                var materialAsset = Create<MaterialAsset>(
                    new AssetPath($"{parentPath}/{modelName}/Materials", meshName),
                    meshName);

                var textures = await CreateTexturesAsync(meshData.Textures, $"{parentPath}/{modelName}/Textures", textureCache);

                materialAsset.SetData(new MaterialData
                {
                    PipelineName = "Geometry",
                    Textures = textures.Select(texture => new AssetReference<TextureAsset>(texture)).ToList()
                });

                modelAsset.AddPart(new ModelPart { Name = meshData.Name, Mesh = meshAsset, Material = materialAsset });
                modelAsset.Dependencies.Add(meshAsset);
                modelAsset.Dependencies.Add(materialAsset);
                foreach (var texture in textures)
                {
                    materialAsset.Dependencies.Add(texture);
                }
                _assetRepository.Add(meshAsset);
                _assetRepository.Add(materialAsset);
            }

            _assetRepository.Add(modelAsset);
            return modelAsset;
        }

        private async Task<List<TextureAsset>> CreateTexturesAsync(List<TextureSlot> texturePaths, string textureFolder, Dictionary<string, TextureAsset> textureCache)
        {
            var textureIDs = new List<TextureAsset>();

            foreach (var slot in texturePaths)
            {
                if (!textureCache.TryGetValue(slot.FilePath, out var textureAsset))
                {
                    var textureName = Path.GetFileName(slot.FilePath);
                    textureAsset = Create<TextureAsset>(new AssetPath(textureFolder, textureName));
                    textureAsset.SetData(new TextureData
                    {
                        FilePaths = [slot.FilePath],
                        GenerateMipmaps = true,
                        Dimension = TextureDimension.Texture2D,
                        ConvertToSrgb = slot.TextureType == TextureType.Diffuse,
                        Sampler = new SamplerState()
                        {
                             AddressModeU = slot.WrapModeU.ToEngineWrap(),
                             AddressModeV = slot.WrapModeV.ToEngineWrap(),
                        }
                    });
                    textureCache[slot.FilePath] = textureAsset;
                    _assetRepository.Add(textureAsset);
                }
                textureIDs.Add(textureAsset);
            }

            return textureIDs;
        }
    }
}
