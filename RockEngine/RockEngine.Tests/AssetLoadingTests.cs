using System.Numerics;
using NUnit.Framework;
using RockEngine.Assets;
using RockEngine.Core;
using RockEngine.Core.Assets;
using RockEngine.Core.Rendering.Texturing;
using SkiaSharp;

namespace RockEngine.Tests
{
    [TestFixture]
    public class AssetLoadingTests : TestBase
    {
        private string _tempDir;
        private string _testImagePath;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            // 6. Prepare temporary test files (common for all tests)
            _tempDir = Path.Combine(Path.GetTempPath(), "RockEngineTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _testImagePath = Path.Combine(_tempDir, "test.png");
            using var bitmap = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Red);
            }

            using var fs = File.OpenWrite(_testImagePath);
            bitmap.Encode(SKEncodedImageFormat.Png, 100).SaveTo(fs);

        }

        [OneTimeTearDown]
        public void OneTimeTeardown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        [SetUp]
        public async Task SetUp()
        {
            // Create a temporary project so AssetManager has a BasePath
            var assetManager = Scope.GetInstance<IProjectManager>();
            var projectName = $"TestProject_{TestContext.CurrentContext.Test.Name}";
            await assetManager.CreateProjectAsync<ProjectAsset, ProjectData>(_tempDir, projectName);
        }


        [Test]
        public async Task TextureAsset_LoadImageData_ShouldPopulateProperties()
        {
            var textureAsset = new TextureAsset();
            try
            {
                textureAsset.SetData(new TextureData
                {
                    FilePaths = new List<string> { _testImagePath },
                    GenerateMipmaps = false,
                    FlipVertically = false
                });

                await textureAsset.LoadDataAsync();

                Assert.That(textureAsset.IsDataLoaded, Is.True);
                Assert.That(textureAsset.Width, Is.EqualTo(64));
                Assert.That(textureAsset.Height, Is.EqualTo(64));
                Assert.That(textureAsset.Format, Is.EqualTo(TextureFormat.R8G8B8A8Unorm));
            }
            finally
            {
                textureAsset.Dispose();
            }
        }

        [Test]
        public async Task TextureAsset_LoadGpuResources_ShouldCreateTexture()
        {
            var textureAsset = new TextureAsset();
            try
            {
                textureAsset.SetData(new TextureData
                {
                    FilePaths = new List<string> { _testImagePath },
                    GenerateMipmaps = true,
                    Format = TextureFormat.R8G8B8A8Unorm,
                    Sampler = new SamplerState
                    {
                        AddressModeU = TextureWrap.Repeat,
                        AddressModeV = TextureWrap.Repeat,
                        MinFilter = TextureFilter.Linear,
                        MagFilter = TextureFilter.Linear
                    }
                });

                await textureAsset.LoadDataAsync();
                await textureAsset.LoadGpuResourcesAsync();

                Assert.That(textureAsset.GpuReady, Is.True);
                Assert.That(textureAsset.Texture, Is.Not.Null);
                Assert.That(textureAsset.Texture.Image, Is.Not.Null);
                Assert.That(textureAsset.Texture.Image.GetView(), Is.Not.Null);
            }
            finally
            {
                textureAsset.Dispose();
            }
        }

        [Test]
        public async Task MeshAsset_LoadGeometryData_ShouldStoreVerticesAndIndices()
        {
            var vertices = new[]
            {
                new Vertex(new Vector3(0,0,0), new Vector3(0,0,1), new Vector2(0,0)),
                new Vertex(new Vector3(1,0,0), new Vector3(0,0,1), new Vector2(1,0)),
                new Vertex(new Vector3(0,1,0), new Vector3(0,0,1), new Vector2(0,1))
            };
            var indices = new uint[] { 0, 1, 2 };

            var meshAsset = new MeshAsset();
            try
            {
                meshAsset.SetGeometry(vertices, indices);

                Assert.That(meshAsset.VerticesCount, Is.EqualTo(3));
                Assert.That(meshAsset.IndicesCount, Is.EqualTo(3));
            }
            finally
            {
                meshAsset.Dispose();
            }
        }

        [Test]
        public async Task MeshAsset_LoadGpuResources_ShouldAddToGlobalBuffer()
        {
            var vertices = new[]
            {
                new Vertex(new Vector3(0,0,0), new Vector3(0,0,1), new Vector2(0,0)),
                new Vertex(new Vector3(1,0,0), new Vector3(0,0,1), new Vector2(1,0)),
                new Vertex(new Vector3(0,1,0), new Vector3(0,0,1), new Vector2(0,1))
            };
            var indices = new uint[] { 0, 1, 2 };

            var meshAsset = new MeshAsset();
            try
            {
                meshAsset.SetGeometry(vertices, indices);
                meshAsset.ID = Guid.NewGuid();

                await meshAsset.LoadGpuResourcesAsync();

                Assert.That(meshAsset.GpuReady, Is.True);
            }
            finally
            {
                meshAsset.Dispose();
            }
        }

        [Test]
        public async Task MaterialAsset_LoadData_ShouldStoreProperties()
        {
            var materialAsset = new MaterialAsset();
            try
            {
                var materialData = new MaterialData
                {
                    PipelineName = "TestPipeline",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Color"] = new Vector4(1, 0, 0, 1),
                        ["Roughness"] = 0.5f
                    }
                };
                materialAsset.SetData(materialData);

                Assert.That(materialAsset.IsDataLoaded, Is.True);
                Assert.That(materialAsset.PipelineName, Is.EqualTo("TestPipeline"));
                Assert.That(materialAsset.Parameters["Color"], Is.EqualTo(new Vector4(1, 0, 0, 1)));
                Assert.That(materialAsset.Parameters["Roughness"], Is.EqualTo(0.5f));
            }
            finally
            {
                materialAsset.Dispose();
            }
        }

        [Test]
        public async Task MaterialAsset_UpdateParameter_ShouldModifyData()
        {
            var materialAsset = new MaterialAsset();
            try
            {
                materialAsset.SetData(new MaterialData
                {
                    PipelineName = "TestPipeline",
                    Parameters = new Dictionary<string, object>
                    {
                        ["Color"] = new Vector4(1, 1, 1, 1)
                    }
                });

                materialAsset.UpdateParameter("Color", new Vector4(0, 0, 0, 1));

                Assert.That(materialAsset.Parameters["Color"], Is.EqualTo(new Vector4(0, 0, 0, 1)));
            }
            finally
            {
                materialAsset.Dispose();
            }
        }

        [Test]
        public async Task AssetReference_Resolve_ShouldReturnAsset()
        {
            var meshAsset = new MeshAsset
            {
                ID = Guid.NewGuid(),
                Name = "TestMesh",
                Path = new AssetPath("Meshes", "TestMesh.rck") // Assign a path for saving
            };
            meshAsset.SetGeometry(
                new[] { new Vertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero) },
                null);

            var repo = Scope.GetInstance<IAssetRepository>();
            repo.Add(meshAsset);

            var reference = new AssetReference<MeshAsset>(meshAsset.ID);
            var resolved = reference.Asset;

            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved.ID, Is.EqualTo(meshAsset.ID));

            repo.Remove(meshAsset.ID);
            meshAsset.Dispose();
        }

        [Test]
        public async Task AssetReference_GetAssetAsync_ShouldLoadIfNotInRepo()
        {
            var meshAsset = new MeshAsset
            {
                ID = Guid.NewGuid(),
                Name = "AsyncTestMesh",
                Path = new AssetPath("Meshes", "AsyncTestMesh.rck")
            };
            meshAsset.SetGeometry(
                new[] { new Vertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero) },
                null);

            var assetManager = Scope.GetInstance<IAssetManager>();
            await assetManager.SaveAsync(meshAsset);

            var reference = new AssetReference<MeshAsset>(meshAsset.ID);
            var loaded = await reference.GetAssetAsync();

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.ID, Is.EqualTo(meshAsset.ID));
            Assert.That(reference.IsResolved, Is.True);

            meshAsset.Dispose();
            loaded.Dispose();
        }

        [Test]
        public async Task AssetManager_LoadAssetByPath_ShouldReturnAsset()
        {
            var meshAsset = new MeshAsset
            {
                Name = "PathTest",
                Path = new AssetPath("Meshes", "PathTest")
            };
            meshAsset.SetGeometry(
                new[] { new Vertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero) },
                null);

            var assetManager = Scope.GetInstance<IAssetManager>();
            await assetManager.SaveAsync(meshAsset);

            var loaded = await assetManager.LoadAssetAsync<MeshAsset>("Meshes/PathTest.asset");

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Name, Is.EqualTo("PathTest"));
            Assert.That(loaded.VerticesCount, Is.EqualTo(1));

            meshAsset.Dispose();
            loaded.Dispose();
        }

        [Test]
        public async Task AssetManager_SaveAndLoad_ShouldPersistData()
        {
            var textureAsset = new TextureAsset
            {
                Name = "SaveTest",
                Path = new AssetPath("Textures", "SaveTest")
            };
            textureAsset.SetData(new TextureData
            {
                FilePaths = new List<string> { _testImagePath },
                GenerateMipmaps = true,
                Format = TextureFormat.R8G8B8A8Unorm
            });

            var assetManager = Scope.GetInstance<IAssetManager>();
            await assetManager.SaveAsync(textureAsset);

            var loaded = await assetManager.LoadAssetAsync<TextureAsset>("Textures/SaveTest.asset");
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Name, Is.EqualTo("SaveTest"));
            Assert.That(loaded.IsDataLoaded, Is.True);

            await loaded.LoadDataAsync();
            Assert.That(loaded.Width, Is.EqualTo(64));
            Assert.That(loaded.Height, Is.EqualTo(64));

            textureAsset.Dispose();
            loaded.Dispose();
        }

        [Test]
        public async Task ModelAsset_CreateAndLoad_ShouldContainParts()
        {
            // Create mesh and material assets with proper paths
            var meshAsset = new MeshAsset
            {
                Name = "ModelMesh",
                Path = new AssetPath("Meshes", "ModelMesh")
            };
            meshAsset.SetGeometry(
                new[] { new Vertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero) },
                null);

            var materialAsset = new MaterialAsset
            {
                Name = "ModelMaterial",
                Path = new AssetPath("Materials", "ModelMaterial")
            };
            materialAsset.SetData(new MaterialData { PipelineName = "Test" });

            var assetManager = Scope.GetInstance<IAssetManager>();
            await assetManager.SaveAsync(meshAsset);
            await assetManager.SaveAsync(materialAsset);

            var modelAsset = new ModelAsset
            {
                Name = "TestModel",
                Path = new AssetPath("Models", "TestModel")
            };

            var part = new ModelPart
            {
                Mesh = new AssetReference<MeshAsset>(meshAsset),
                Material = new AssetReference<MaterialAsset>(materialAsset),
                Name = "Part1",
                Transform = Matrix4x4.Identity
            };
            modelAsset.AddPart(part);

            await assetManager.SaveAsync(modelAsset);

            var loadedModel = await assetManager.LoadAssetAsync<ModelAsset>("Models/TestModel.asset");

            Assert.That(loadedModel, Is.Not.Null);
            Assert.That(loadedModel.Parts.Count, Is.EqualTo(1));
            var loadedPart = loadedModel.Parts[0];
            Assert.That(loadedPart.Name, Is.EqualTo("Part1"));
            Assert.That(loadedPart.Mesh.AssetID, Is.EqualTo(meshAsset.ID));
            Assert.That(loadedPart.Material.AssetID, Is.EqualTo(materialAsset.ID));

            modelAsset.Dispose();

            loadedModel.Dispose();

            Assert.That(modelAsset.GpuReady, Is.False);
            Assert.That(materialAsset.GpuReady, Is.False);
            Assert.That(meshAsset.GpuReady, Is.False);
        }
    }
}