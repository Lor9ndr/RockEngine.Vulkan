using NLog;

using RockEngine.Assets;
using RockEngine.Core;
using RockEngine.Core.Assets;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering.Texturing;

namespace RockEngine.Window.Tests
{
    public class AssetSystemTestApplication : Application
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private IAssetManager _assetManager;
        private IAssetFactory _assetFactory;
        private IProjectManager _projectManager;

        protected override async Task Load()
        {
            try
            {
                _logger.Info("Starting asset system test...");

                // Get services from container
                _assetManager = IoC.Container.GetInstance<IAssetManager>();
                _assetFactory = IoC.Container.GetInstance<IAssetFactory>();
                _projectManager = _assetManager as IProjectManager;

                // Create a test project
                await CreateTestProject();

                // Test basic asset operations
                await TestBasicAssetOperations();

                // Test material creation
                await TestMaterialCreation();

                // Test texture loading
                await TestTextureLoading();

                // Test model import
                await TestModelImport();

                // Test scene serialization
                await TestSceneSerialization();

                _logger.Info("Asset system test completed successfully!");

                // Keep window open for inspection
                // Stop() when ready to exit
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Asset system test failed");
                throw;
            }
        }

        private async Task CreateTestProject()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "RockEngineTests", Guid.NewGuid().ToString());

            _logger.Info($"Creating test project at: {tempPath}");

            var project = await _projectManager.CreateProjectAsync<ProjectAsset>(
                tempPath,
                "TestProject"
            );

            _logger.Info($"Project created: {project.Name} (ID: {project.ID})");
        }

        private async Task TestBasicAssetOperations()
        {
            _logger.Info("Testing basic asset operations...");

            // Create a simple material
            var materialPath = new AssetPath("Materials", "TestMaterial");
            var material = _assetFactory.Create<MaterialAsset>(materialPath, "TestMaterial");

            // Set material data
            material.SetData(new MaterialData
            {
                PipelineName = "PBR",
                BaseColor = System.Numerics.Vector4.One,
                Metallic = 0.5f,
                Roughness = 0.3f
            });

            // Save the asset
            await _assetManager.SaveAsync(material);
            _logger.Info($"Material saved: {material.Name}");

            // Load it back
            var loadedMaterial = await _assetManager.LoadAssetAsync<MaterialAsset>("Materials/TestMaterial.asset");
            _logger.Info($"Material loaded: {loadedMaterial.Name}, ID: {loadedMaterial.ID}");

            // Verify data
            await loadedMaterial.LoadDataAsync();
            var materialData = loadedMaterial.GetData() as MaterialData;
            if (materialData != null)
            {
                _logger.Info($"Material data verified: Pipeline={materialData.PipelineName}, Metallic={materialData.Metallic}");
            }
        }

        private async Task TestMaterialCreation()
        {
            _logger.Info("Testing material creation...");

            // Create material with textures
            var materialPath = new AssetPath("Materials", "TexturedMaterial");
            var material = _assetFactory.Create<MaterialAsset>(materialPath, "TexturedMaterial");

            // Create a test texture first
            await CreateTestTexture();

            // Load texture reference
            var textureRef = new AssetReference<TextureAsset>(await GetTextureGuid("TestTexture"));

            material.SetData(new MaterialData
            {
                PipelineName = "PBR",
                Textures = new List<AssetReference<TextureAsset>> { textureRef },
                Parameters = new Dictionary<string, object>
                {
                    { "tiling", new System.Numerics.Vector2(2.0f, 2.0f) },
                    { "emissiveStrength", 1.0f }
                }
            });

            await _assetManager.SaveAsync(material);

            // Test GPU resource loading
            var loadedMaterial = await _assetManager.LoadAssetAsync<MaterialAsset>("Materials/TexturedMaterial.asset");
            await loadedMaterial.LoadGpuResourcesAsync();

            if (loadedMaterial.GpuReady)
            {
                _logger.Info("Material GPU resources loaded successfully");
            }
        }

        private async Task CreateTestTexture()
        {
            // Create a simple test texture using SkiaSharp
            var texturePath = new AssetPath("Textures", "TestTexture");
            var textureAsset = _assetFactory.Create<TextureAsset>(texturePath, "TestTexture");

            // Create a simple 2x2 colored texture
            using var bitmap = new SkiaSharp.SKBitmap(2, 2);
            bitmap.SetPixel(0, 0, SkiaSharp.SKColor.Parse("#FF0000"));
            bitmap.SetPixel(1, 0, SkiaSharp.SKColor.Parse("#00FF00"));
            bitmap.SetPixel(0, 1, SkiaSharp.SKColor.Parse("#0000FF"));
            bitmap.SetPixel(1, 1, SkiaSharp.SKColor.Parse("#FFFFFF"));

            var tempTexturePath = Path.Combine(Path.GetTempPath(), "test_texture.png");
            using var fileStream = File.OpenWrite(tempTexturePath);
            bitmap.Encode(fileStream, SkiaSharp.SKEncodedImageFormat.Png, 100);

            textureAsset.SetData(new TextureData
            {
                FilePaths = new List<string> { tempTexturePath },
                Width = 2,
                Height = 2,
                Format = RockEngine.Core.Rendering.Texturing.TextureFormat.R8G8B8A8Unorm,
                GenerateMipmaps = true,
                Dimension = RockEngine.Core.Rendering.Texturing.TextureDimension.Texture2D,
                FlipVertically = true
            });

            await _assetManager.SaveAsync(textureAsset);
            _logger.Info($"Test texture created and saved");

            // Test GPU loading
            await textureAsset.LoadGpuResourcesAsync();

            if (textureAsset.GpuReady)
            {
                _logger.Info("Texture GPU resources loaded successfully");
            }

            // Clean up temp file
            File.Delete(tempTexturePath);
        }

        private async Task<Guid> GetTextureGuid(string textureName)
        {
            var texture = await _assetManager.LoadAssetAsync<TextureAsset>($"Textures/{textureName}.asset");
            return texture.ID;
        }

        private async Task TestTextureLoading()
        {
            _logger.Info("Testing texture loading...");

            // Test loading existing texture
            var texture = await _assetManager.LoadAssetAsync<TextureAsset>("Textures/TestTexture.asset");

            // Test async loading
            var textureRef = new AssetReference<TextureAsset>(texture.ID);
            var loadedTexture = await textureRef.GetAssetAsync();

            _logger.Info($"Texture loaded: {loadedTexture.Name}, Size: {loadedTexture.Width}x{loadedTexture.Height}");

            // Test GPU resource lifecycle
            await loadedTexture.LoadGpuResourcesAsync();
            loadedTexture.UnloadGpuResources();

            // Reload to test
            await loadedTexture.LoadGpuResourcesAsync();
            _logger.Info("Texture GPU lifecycle test passed");
        }

        private async Task TestModelImport()
        {
            _logger.Info("Testing model import...");

            // Check if we have a test model file
            var testModelPath = FindTestModel();
            if (string.IsNullOrEmpty(testModelPath))
            {
                _logger.Warn("No test model found, skipping model import test");
                return;
            }

            try
            {
                var modelAsset = (ModelAsset)await _assetFactory.CreateModelFromFileAsync(
                    testModelPath,
                    "TestModel",
                    "Models/Test"
                );

                await _assetManager.SaveAsync(modelAsset);
                _logger.Info($"Model imported: {modelAsset.Name} with {modelAsset.Parts.Count} parts");

                // Test GPU resource loading for model
                await modelAsset.LoadGpuResourcesAsync();
                _logger.Info("Model GPU resources loaded");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Model import test failed");
            }
        }

        private async Task TestSceneSerialization()
        {
            _logger.Info("Testing scene serialization...");

            var scenePath = new AssetPath("Scenes", "TestScene");
            var scene = _assetFactory.Create<SceneAsset>(scenePath, "TestScene");

            // Create some test entities
            var entity1 = scene.CreateEntity("TestEntity1");
            var entity2 = scene.CreateEntity("TestEntity2");

            // Set some transforms
            entity1.Transform.Position = new System.Numerics.Vector3(1, 2, 3);
            entity2.Transform.Scale = new System.Numerics.Vector3(2, 2, 2);

            // Add components
            // Note: You might need to register components in your DI container
            // entity1.AddComponent(new MeshRendererComponent());

            // Save the scene
            await _assetManager.SaveAsync(scene);
            _logger.Info($"Scene saved with {scene.Entities.Count} entities");

            // Load it back
            var loadedScene = await _assetManager.LoadAssetAsync<SceneAsset>("Scenes/TestScene.asset");

            // Instantiate entities from loaded data
            await loadedScene.InstantiateEntities();
            _logger.Info($"Scene loaded with {loadedScene.Entities.Count} entities");
        }

        private string FindTestModel()
        {
            // Look for test models in common locations
            var possiblePaths = new[]
            {
                "TestData/cube.obj",
                "Assets/Test/cube.obj",
                "../../../TestData/cube.obj"
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    return Path.GetFullPath(path);
                }
            }

            return null;
        }
    }
}