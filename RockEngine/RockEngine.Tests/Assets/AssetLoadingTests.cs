using System.Numerics;
using NUnit.Framework;
using RockEngine.Assets;
using RockEngine.Core;
using RockEngine.Core.Assets;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Core.ResourceProviders;
using SkiaSharp;

namespace RockEngine.Tests
{
    [TestFixture]
    public class AssetLoadingTests : TestBase
    {
        private string? _tempDir;
        private string _testImagePath;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
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
            var assetManager = Scope.GetInstance<IProjectManager>();
            var projectName = $"TestProject_{TestContext.CurrentContext.Test.Name}";
            await assetManager.CreateProjectAsync<ProjectAsset, ProjectData>(_tempDir, projectName).ConfigureAwait(false);
        }

        #region TextureAsset Tests

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

                await textureAsset.LoadDataAsync().ConfigureAwait(false);

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

                await textureAsset.LoadDataAsync().ConfigureAwait(false);
                await textureAsset.LoadGpuResourcesAsync().ConfigureAwait(false);

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

        #endregion

        #region MeshAsset Tests

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

                await meshAsset.LoadGpuResourcesAsync().ConfigureAwait(false);

                Assert.That(meshAsset.GpuReady, Is.True);
            }
            finally
            {
                meshAsset.Dispose();
            }
        }

        #endregion

        #region MaterialAsset Tests

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

        #endregion

        #region ModelAsset Tests

        [Test]
        
        public async Task ModelAsset_CreateAndLoad_ShouldContainParts()
        {
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
            await assetManager.SaveAsync(meshAsset).ConfigureAwait(false);
            await assetManager.SaveAsync(materialAsset).ConfigureAwait(false);

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

            await assetManager.SaveAsync(modelAsset).ConfigureAwait(false);

            var loadedModel = await assetManager.LoadAssetAsync<ModelAsset>("Models/TestModel.asset").ConfigureAwait(false);

            Assert.That(loadedModel, Is.Not.Null);
            Assert.That(loadedModel.Parts.Count, Is.EqualTo(1));
            var loadedPart = loadedModel.Parts[0];
            Assert.That(loadedPart.Name, Is.EqualTo("Part1"));
            Assert.That(loadedPart.Mesh.AssetID, Is.EqualTo(meshAsset.ID));
            Assert.That(loadedPart.Material.AssetID, Is.EqualTo(materialAsset.ID));

            modelAsset.Dispose();
            loadedModel.Dispose();
        }

        #endregion

        #region SceneAsset Tests

        [Test]
        
        public async Task SceneAsset_InstantiateEntities_ShouldLoadCorrectly()
        {
            var sceneAsset = new SceneAsset
            {
                Name = "TestScene",
                Path = new AssetPath("Scenes", "TestScene")
            };

            // Create entity data (simulate loading from serialized data)
            var entity = sceneAsset.CreateEntity("TestEntity");
            entity.Transform.Position = new Vector3(1, 2, 3);
            var meshRenderer = new MeshRenderer();
            meshRenderer.SetProviders(new MeshProvider(new AssetReference<MeshAsset>(Guid.NewGuid())),
                new MaterialProvider(new AssetReference<MaterialAsset>(Guid.NewGuid())));
            entity.AddComponent(meshRenderer);

            // Serialize to data
            sceneAsset.BeforeSaving();
            var sceneData = sceneAsset.Data;

            // Reload from data (simulate loading from disk)
            var newSceneAsset = new SceneAsset
            {
                Name = "TestScene",
                Path = new AssetPath("Scenes", "TestScene")
            };
            newSceneAsset.SetData(sceneData);

            await newSceneAsset.InstantiateEntities().ConfigureAwait(false);

            Assert.That(newSceneAsset.Entities.Count, Is.EqualTo(1));
            var loadedEntity = newSceneAsset.Entities.Values.First();
            Assert.That(loadedEntity.Name, Is.EqualTo("TestEntity"));
            Assert.That(loadedEntity.Transform.Position, Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(loadedEntity.HasComponent<MeshRenderer>(), Is.True);

            newSceneAsset.Unload();
            sceneAsset.Unload();
        }

        #endregion

        #region AssetManager Tests

        [Test]
        
        public async Task AssetManager_LoadByPath_ShouldReturnAsset()
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
            await assetManager.SaveAsync(meshAsset).ConfigureAwait(false);

            var loaded = await assetManager.LoadAssetAsync<MeshAsset>("Meshes/PathTest.asset").ConfigureAwait(false);

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
            await assetManager.SaveAsync(textureAsset).ConfigureAwait(false);

            var loaded = await assetManager.LoadAssetAsync<TextureAsset>("Textures/SaveTest.asset").ConfigureAwait(false);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Name, Is.EqualTo("SaveTest"));
            Assert.That(loaded.IsDataLoaded, Is.True);

            await loaded.LoadDataAsync().ConfigureAwait(false);
            Assert.That(loaded.Width, Is.EqualTo(64));
            Assert.That(loaded.Height, Is.EqualTo(64));

            textureAsset.Dispose();
            loaded.Dispose();
        }

        [Test]
        
        public async Task AssetManager_Cache_ShouldReturnCachedAsset()
        {
            var meshAsset = new MeshAsset
            {
                Name = "CacheTest",
                Path = new AssetPath("Meshes", "CacheTest")
            };
            meshAsset.SetGeometry(
                new[] { new Vertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero) },
                null);

            var assetManager = Scope.GetInstance<IAssetManager>();
            await assetManager.SaveAsync(meshAsset).ConfigureAwait(false);

            var first = await assetManager.LoadAssetAsync<MeshAsset>("Meshes/CacheTest.asset").ConfigureAwait(false);
            var second = await assetManager.LoadAssetAsync<MeshAsset>("Meshes/CacheTest.asset").ConfigureAwait(false);

            Assert.That(ReferenceEquals(first, second), Is.True, "Cache should return same instance");

            meshAsset.Dispose();
            first.Dispose();
        }

        [Test]
        public async Task AssetManager_ConcurrentLoads_ShouldNotThrow()
        {
            var tasks = new List<Task>();
            for (int i = 0; i < 50; i++)
            {
                var textureAsset = new TextureAsset
                {
                    Name = $"ConcurrentTest_{i}",
                    Path = new AssetPath("Textures", $"ConcurrentTest_{i}")
                };
                textureAsset.SetData(new TextureData
                {
                    FilePaths = [_testImagePath],
                    GenerateMipmaps = true
                });
                var assetManager = Scope.GetInstance<IAssetManager>();
                tasks.Add(Task.Run(async () =>
                {
                    await assetManager.SaveAsync(textureAsset).ConfigureAwait(false);
                    var loaded = await assetManager.LoadAssetAsync<TextureAsset>(textureAsset.Path.ToString()).ConfigureAwait(false);
                    Assert.That(loaded, Is.Not.Null);
                }));
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        #endregion

        #region AssetReference Tests

        [Test]
        
        public async Task AssetReference_Resolve_ShouldReturnAsset()
        {
            var meshAsset = new MeshAsset
            {
                ID = Guid.NewGuid(),
                Name = "TestMesh",
                Path = new AssetPath("Meshes", "TestMesh.rck")
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
            await assetManager.SaveAsync(meshAsset).ConfigureAwait(false);

            var reference = new AssetReference<MeshAsset>(meshAsset.ID);
            var loaded = await reference.GetAssetAsync().ConfigureAwait(false);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.ID, Is.EqualTo(meshAsset.ID));
            Assert.That(reference.IsResolved, Is.True);

            meshAsset.Dispose();
            loaded.Dispose();
        }

        [Test]
        public async Task AssetReference_ImplicitConversion_ShouldWork()
        {
            var meshAsset = new MeshAsset
            {
                ID = Guid.NewGuid(),
                Name = "ImplicitTest",
                Path = new AssetPath("Meshes", "ImplicitTest")
            };
            meshAsset.SetGeometry(
                [new Vertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero)],
                null);

            var assetManager = Scope.GetInstance<IAssetManager>();
            await assetManager.SaveAsync(meshAsset).ConfigureAwait(false);

            AssetReference<MeshAsset> reference = meshAsset.ID;
            MeshAsset resolved = await reference.GetAssetAsync().ConfigureAwait(false);

            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved.ID, Is.EqualTo(meshAsset.ID));
        }

        #endregion

        #region Serialization Tests

        [Test]
        public async Task Serialization_BoxedVector3_ShouldSurviveRoundTrip()
        {
            // This tests the TODO in MaterialData: MessagePack serialization of boxed Vector3
            var materialAsset = new MaterialAsset();
            materialAsset.SetData(new MaterialData
            {
                PipelineName = "Test",
                Parameters = new Dictionary<string, object>
                {
                    ["Color"] = new Vector3(0.5f, 0.2f, 0.8f)
                }
            });

            var assetManager = Scope.GetInstance<IAssetManager>();
            await assetManager.SaveAsync(materialAsset).ConfigureAwait(false);

            var loaded = await assetManager.LoadAssetAsync<MaterialAsset>(materialAsset.Path.ToString()).ConfigureAwait(false);

            var param = loaded.Parameters["Color"];
            Assert.That(param, Is.InstanceOf<Vector3>());
            Assert.That((Vector3)param, Is.EqualTo(new Vector3(0.5f, 0.2f, 0.8f)));

            materialAsset.Dispose();
            loaded.Dispose();
        }

        #endregion

        #region Stress Tests

        [Test]
        public async Task StressTest_ManyTextures_ConcurrentLoad()
        {
            const int textureCount = 100;
            var texturePaths = new List<string>();

            // Create many textures
            for (int i = 0; i < textureCount; i++)
            {
                var textureAsset = new TextureAsset
                {
                    Name = $"StressTex_{i}",
                    Path = new AssetPath("Textures", $"StressTex_{i}")
                };
                textureAsset.SetData(new TextureData
                {
                    FilePaths = new List<string> { _testImagePath },
                    GenerateMipmaps = true
                });
                var assetManager = Scope.GetInstance<IAssetManager>();
                await assetManager.SaveAsync(textureAsset).ConfigureAwait(false);
                texturePaths.Add($"Textures/StressTex_{i}.asset");
            }

            // Load them concurrently
            var tasks = texturePaths.Select(path => Task.Run(async () =>
            {
                var assetManager = Scope.GetInstance<IAssetManager>();
                var texture = await assetManager.LoadAssetAsync<TextureAsset>(path).ConfigureAwait(false);
                Assert.That(texture, Is.Not.Null);
                return texture;
            })).ToList();

            var loadedTextures = await Task.WhenAll(tasks).ConfigureAwait(false);

            // Verify all loaded
            Assert.That(loadedTextures.Length, Is.EqualTo(textureCount));
            foreach (var tex in loadedTextures)
            {
                tex.Dispose();
            }
        }

        [Test]
        public async Task StressTest_MemoryPressure_WithCache()
        {
            var assetManager = Scope.GetInstance<IAssetManager>();
            var assets = new List<TextureAsset>();

            // Create many large textures (but we only have one small test image)
            for (int i = 0; i < 200; i++)
            {
                var textureAsset = new TextureAsset
                {
                    Name = $"MemoryTex_{i}",
                    Path = new AssetPath("Textures", $"MemoryTex_{i}")
                };
                textureAsset.SetData(new TextureData
                {
                    FilePaths = new List<string> { _testImagePath },
                    GenerateMipmaps = true
                });
                await assetManager.SaveAsync(textureAsset).ConfigureAwait(false);
                assets.Add(textureAsset);
            }

            // Load them all
            var loadTasks = assets.Select(a => assetManager.LoadAssetAsync<TextureAsset>(a.Path.ToString()));
            var loaded = await Task.WhenAll(loadTasks).ConfigureAwait(false);

            // Force cache eviction by loading more (cache size is limited)
            // The cache should evict old entries automatically
            for (int i = 0; i < 50; i++)
            {
                var tex = await assetManager.LoadAssetAsync<TextureAsset>($"Textures/MemoryTex_{i}.asset").ConfigureAwait(false);
                Assert.That(tex, Is.Not.Null);
            }

            // Cleanup
            foreach (var tex in loaded)
            {
                tex.Dispose();
            }
        }

        [Test]
        
        public async Task StressTest_ModelWithManyParts_LoadAndUnload()
        {
            const int partCount = 50;
            var assetManager = Scope.GetInstance<IAssetManager>();

            // Create a base mesh and material to reuse
            var baseMesh = new MeshAsset
            {
                Name = "BaseMesh",
                Path = new AssetPath("Meshes", "BaseMesh")
            };
            baseMesh.SetGeometry(
                new[] { new Vertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero) },
                null);
            await assetManager.SaveAsync(baseMesh).ConfigureAwait(false);

            var baseMaterial = new MaterialAsset
            {
                Name = "BaseMaterial",
                Path = new AssetPath("Materials", "BaseMaterial")
            };
            baseMaterial.SetData(new MaterialData { PipelineName = "Test" });
            await assetManager.SaveAsync(baseMaterial).ConfigureAwait(false);

            // Create a model with many parts
            var model = new ModelAsset
            {
                Name = "StressModel",
                Path = new AssetPath("Models", "StressModel")
            };

            for (int i = 0; i < partCount; i++)
            {
                model.AddPart(new ModelPart
                {
                    Mesh = new AssetReference<MeshAsset>(baseMesh),
                    Material = new AssetReference<MaterialAsset>(baseMaterial),
                    Name = $"Part_{i}",
                    Transform = Matrix4x4.CreateTranslation(i, 0, 0)
                });
            }

            await assetManager.SaveAsync(model).ConfigureAwait(false);

            // Load the model and verify parts
            var loadedModel = await assetManager.LoadAssetAsync<ModelAsset>("Models/StressModel.asset").ConfigureAwait(false);
            Assert.That(loadedModel.Parts.Count, Is.EqualTo(partCount));

            // Unload GPU resources
            loadedModel.UnloadGpuResources();
            Assert.That(loadedModel.GpuReady, Is.False);

            loadedModel.Dispose();
            baseMesh.Dispose();
            baseMaterial.Dispose();
        }


        #endregion
    }
}