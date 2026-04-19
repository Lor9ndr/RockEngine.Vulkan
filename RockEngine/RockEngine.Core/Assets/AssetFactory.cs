using Assimp;

using RockEngine.Assets;
using RockEngine.Core.DI;
using RockEngine.Core.Extensions;
using RockEngine.Core.Rendering.Texturing;
using SkiaSharp;

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

        public IAsset CreateMaterial(string name, string template, Dictionary<string, IAssetReference<IAsset>>? textures = null, Dictionary<string, object>? parameters = null)
        {
            var material = Create<MaterialAsset>(new AssetPath("Materials", name));
            material.SetData(new MaterialData
            {
                PipelineName = template,
                Textures = textures is null ? [] : textures
                .Select(s => new KeyValuePair<string, AssetReference<TextureAsset>>(s.Key, new AssetReference<TextureAsset>(s.Value.AssetID))).ToDictionary(),
                Parameters = parameters is null ? [] : parameters
            });
            return material;
        }

        public async Task<IAsset> CreateModelFromFileAsync(string filePath, string? modelName = null, string parentPath = "Models", CancellationToken cancellationToken = default)
        {
            modelName ??= Path.GetFileNameWithoutExtension(filePath);
            var meshesData = await _assimpLoader.LoadMeshesAsync(filePath);
            var modelAsset = Create<ModelAsset>(new AssetPath(parentPath, modelName));

            var textureCache = new Dictionary<string, TextureAsset>(StringComparer.OrdinalIgnoreCase);

            foreach (var meshData in meshesData)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var meshName = !string.IsNullOrEmpty(meshData.Name) ? meshData.Name : $"Mesh_{Guid.NewGuid()}";
                var meshAsset = Create<MeshAsset>(
                    new AssetPath($"{parentPath}/{modelName}/Meshes", meshName),
                    meshName);
                meshAsset.SetGeometry(meshData.Vertices, meshData.Indices);

                // Define texture folder once per mesh
                var textureFolder = $"{parentPath}/{modelName}/Textures";

                // Group textures by semantic
                var texturesBySemantic = meshData.Textures
                    .GroupBy(t => t.Semantic)
                    .ToDictionary(g => g.Key, g => g.First());

                TextureAsset? albedoTexture = null;
                TextureAsset? normalTexture = null;
                TextureAsset? mraTexture = null;

                // Handle albedo
                if (texturesBySemantic.TryGetValue(TextureSemantic.Albedo, out var albedoSemantic))
                {
                    albedoTexture = await CreateTextureAsync(albedoSemantic.Slot, textureFolder, textureCache);
                }
                else
                {
                    // Use fallback white texture
                    albedoTexture = CreateDefaultTexture("white", textureFolder);
                }

                // Handle normal
                if (texturesBySemantic.TryGetValue(TextureSemantic.Normal, out var normalSemantic))
                {
                    normalTexture = await CreateTextureAsync(normalSemantic.Slot, textureFolder, textureCache);
                }
                else
                {
                    normalTexture = CreateDefaultTexture("blue", textureFolder);
                }

                // Handle MRA
                if (texturesBySemantic.TryGetValue(TextureSemantic.MRA, out var mraSemantic))
                {
                    mraTexture = await CreateTextureAsync(mraSemantic.Slot, textureFolder, textureCache);
                }
                else if (texturesBySemantic.TryGetValue(TextureSemantic.Metallic, out var metallicSemantic) &&
                         texturesBySemantic.TryGetValue(TextureSemantic.Roughness, out var roughnessSemantic) &&
                         texturesBySemantic.TryGetValue(TextureSemantic.AmbientOcclusion, out var aoSemantic))
                {
                    // Combine them into one MRA texture
                    mraTexture = await CreateCombinedMRATextureAsync(
                        textureFolder,
                        metallicSemantic,
                        roughnessSemantic,
                        aoSemantic,
                        cancellationToken);
                }
                else
                {
                    // Create default MRA texture (e.g., all gray)
                    mraTexture = CreateDefaultMRATexture(textureFolder);
                }

                // Now create the material
                var materialAsset = Create<MaterialAsset>(
                    new AssetPath($"{parentPath}/{modelName}/Materials", meshName),
                    meshName);

                var materialTextures = new Dictionary<string, AssetReference<TextureAsset>>();
                if (albedoTexture != null)
                {
                    materialTextures["Albedo"] = new AssetReference<TextureAsset>(albedoTexture.ID);
                }

                if (normalTexture != null)
                {
                    materialTextures["Normal"] = new AssetReference<TextureAsset>(normalTexture.ID);
                }

                if (mraTexture != null)
                {
                    materialTextures["MRA"] = new AssetReference<TextureAsset>(mraTexture.ID);
                }

                materialAsset.SetData(new MaterialData
                {
                    PipelineName = "Geometry",
                    Textures = materialTextures,
                    Parameters = new Dictionary<string, object>()
                });

                modelAsset.AddPart(new ModelPart
                {
                    Name = meshData.Name,
                    Mesh = meshAsset,
                    Material = materialAsset
                });

                modelAsset.Dependencies.Add(meshAsset);
                modelAsset.Dependencies.Add(materialAsset);
                // Add texture dependencies
                if (albedoTexture != null)
                {
                    modelAsset.Dependencies.Add(albedoTexture);
                }

                if (normalTexture != null)
                {
                    modelAsset.Dependencies.Add(normalTexture);
                }

                if (mraTexture != null)
                {
                    modelAsset.Dependencies.Add(mraTexture);
                }

                _assetRepository.Add(meshAsset);
                _assetRepository.Add(materialAsset);
            }

            _assetRepository.Add(modelAsset);
            return modelAsset;
        }

        private async Task<TextureAsset> CreateCombinedMRATextureAsync(string textureFolder,
            TextureSlotWithSemantic metallicSlot,
            TextureSlotWithSemantic roughnessSlot,
            TextureSlotWithSemantic aoSlot,
            CancellationToken cancellationToken)
        {
            // Load all three images
            var metallicBitmap = SKBitmap.Decode(metallicSlot.Slot.FilePath);
            var roughnessBitmap = SKBitmap.Decode(roughnessSlot.Slot.FilePath);
            var aoBitmap = SKBitmap.Decode(aoSlot.Slot.FilePath);

            // Ensure all dimensions match
            int width = metallicBitmap.Width;
            int height = metallicBitmap.Height;
            if (roughnessBitmap.Width != width || roughnessBitmap.Height != height ||
                aoBitmap.Width != width || aoBitmap.Height != height)
            {
                throw new InvalidOperationException("MRA texture dimensions do not match");
            }

            // Create a new bitmap for the combined texture
            var combinedBitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using (var canvas = new SKCanvas(combinedBitmap))
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        var metallicPixel = metallicBitmap.GetPixel(x, y);
                        var roughnessPixel = roughnessBitmap.GetPixel(x, y);
                        var aoPixel = aoBitmap.GetPixel(x, y);

                        // Assumes metallic and roughness are single-channel textures
                        // We'll take the red channel as the value
                        byte metallic = metallicPixel.Red;
                        byte roughness = roughnessPixel.Red;
                        byte ao = aoPixel.Red;

                        var combinedColor = new SKColor(metallic, roughness, ao);
                        combinedBitmap.SetPixel(x, y, combinedColor);
                    }
                }
            }

            // Save the combined texture to a temporary file (or keep in memory)
            string combinedFileName = $"mra_{Guid.NewGuid()}.png";
            string combinedPath = Path.Combine(textureFolder, combinedFileName);
            using (var data = combinedBitmap.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = File.OpenWrite(combinedPath))
            {
                data.SaveTo(stream);
            }

            // Create a texture asset for the combined texture
            var textureAsset = Create<TextureAsset>(new AssetPath(textureFolder, combinedFileName));
            textureAsset.SetData(new TextureData
            {
                FilePaths = [combinedPath],
                GenerateMipmaps = true,
                Dimension = TextureDimension.Texture2D,
                ConvertToSrgb = false, // MRA should be linear
                Sampler = new SamplerState
                {
                    AddressModeU = metallicSlot.Slot.WrapModeU.ToEngineWrap(),
                    AddressModeV = metallicSlot.Slot.WrapModeV.ToEngineWrap(),
                }
            });
            _assetRepository.Add(textureAsset);

            return textureAsset;
        }
        private async Task<TextureAsset> CreateTextureAsync(TextureSlot slot,
                                                            string textureFolder,
                                                            Dictionary<string, TextureAsset> textureCache)
        {
            if (textureCache.TryGetValue(slot.FilePath, out var cached))
            {
                return cached;
            }

            var textureName = Path.GetFileName(slot.FilePath);
            var textureAsset = Create<TextureAsset>(new AssetPath(textureFolder, textureName));

            textureAsset.SetData(new TextureData
            {
                FilePaths = [slot.FilePath],
                GenerateMipmaps = true,
                Dimension = TextureDimension.Texture2D,
                ConvertToSrgb = slot.TextureType == TextureType.Diffuse,
                Sampler = new SamplerState
                {
                    AddressModeU = slot.WrapModeU.ToEngineWrap(),
                    AddressModeV = slot.WrapModeV.ToEngineWrap()
                }
            });

            textureCache[slot.FilePath] = textureAsset;
            _assetRepository.Add(textureAsset);
            return textureAsset;
        }

        private TextureAsset CreateDefaultMRATexture(string textureFolder)
        {
            // Create a 1x1 gray texture (metallic=0.5, roughness=0.5, ao=1.0)
            using var bitmap = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Opaque);
            bitmap.SetPixel(0, 0, new SKColor(128, 128, 255)); // metallic=0.5, roughness=0.5, ao=1.0
            string fileName = $"mra_default_{Guid.NewGuid()}.png";
            if (!Directory.Exists(textureFolder))
            {
                Directory.CreateDirectory(textureFolder);
            }
            string filePath = Path.Combine(textureFolder, fileName);
            using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = File.OpenWrite(filePath))
            {
                data.SaveTo(stream);
            }

            var textureAsset = Create<TextureAsset>(new AssetPath(textureFolder, fileName));
            textureAsset.SetData(new TextureData
            {
                FilePaths = [filePath],
                GenerateMipmaps = true,
                Dimension = TextureDimension.Texture2D,
                ConvertToSrgb = false,
                Sampler = new SamplerState()
            });
            _assetRepository.Add(textureAsset);
            return textureAsset;
        }

        private TextureAsset CreateDefaultTexture(string color, string textureFolder)
        {
            // Create a 1x1 texture with the given color (simplified)
            SKColor skColor = color switch
            {
                "white" => SKColors.White,
                "black" => SKColors.Black,
                "blue" => SKColors.Blue,
                _ => SKColors.White
            };
            using var bitmap = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Opaque);
            bitmap.SetPixel(0, 0, skColor);

            string fileName = $"{color}_default_{Guid.NewGuid()}.png";
            string filePath = Path.Combine(textureFolder, fileName);
            using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = File.OpenWrite(filePath))
            {
                data.SaveTo(stream);
            }

            var textureAsset = Create<TextureAsset>(new AssetPath(textureFolder, fileName));
            textureAsset.SetData(new TextureData
            {
                FilePaths = [filePath],
                GenerateMipmaps = true,
                Dimension = TextureDimension.Texture2D,
                ConvertToSrgb = true, // Albedo should be sRGB
                Sampler = new SamplerState()
            });
            _assetRepository.Add(textureAsset);
            return textureAsset;
        }


    }

}
