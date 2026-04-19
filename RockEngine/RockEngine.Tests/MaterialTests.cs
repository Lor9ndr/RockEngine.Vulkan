/*using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using RockEngine.Assets;
using RockEngine.Core.Assets;
using RockEngine.Core.Internal;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;

namespace RockEngine.Tests
{
    [TestFixture]
    public class MaterialSystemTests : TestBase
    {
        private VulkanContext _context;
        private PipelineManager _pipelineManager;
        private MaterialTemplateManager _materialTemplateManager;
        private IAssetRepository _assetRepository;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            _context = GlobalTestSetup.VulkanContext;
            _pipelineManager = GlobalTestSetup.Container.GetInstance<PipelineManager>();
            _materialTemplateManager = GlobalTestSetup.Container.GetInstance<MaterialTemplateManager>();
            _assetRepository = GlobalTestSetup.Container.GetInstance<IAssetRepository>();
        }


        // Helper to check if a binding exists in the BindingCollection
        private bool HasBinding(BindingCollection collection, ResourceBinding binding)
        {
            foreach (var (_, perSet) in collection)
            {
                if (perSet.Any(b => b == binding))
                    return true;
            }
            return false;
        }

        #region MaterialTemplate Tests

        [Test]
        public void MaterialTemplate_GetOrCreate_ShouldReturnTemplate()
        {
            var template = _materialTemplateManager.GetOrCreateTemplate("Geometry");
            Assert.That(template, Is.Not.Null, "Geometry template should exist");
            Assert.That(template.Name, Is.EqualTo("Geometry"));
            Assert.That(template.PassTemplates, Is.Not.Empty);
        }

        [Test]
        public void MaterialTemplate_CreateInstance_ShouldCreateMaterialWithPasses()
        {
            var template = _materialTemplateManager.GetOrCreateTemplate("Geometry");
            var material = template.CreateInstance("TestMaterial", _pipelineManager);
            Assert.That(material, Is.Not.Null);
            Assert.That(material.Name, Is.EqualTo("TestMaterial"));
            Assert.That(material.Passes, Is.Not.Empty);
            Assert.That(material.Passes.ContainsKey(template.PassTemplates.Keys.First()), Is.True);
        }

        #endregion

        #region MaterialPass Tests

        [Test]
        public void MaterialPass_Constructor_ShouldInitializePushConstants()
        {
            var pipeline = _pipelineManager.GetPipelineByName("Geometry");
            Assert.That(pipeline, Is.Not.Null, "Geometry pipeline must exist");
            var pass = new MaterialPass(pipeline);
            Assert.That(pass.Pipeline, Is.EqualTo(pipeline));
            Assert.That(pass.PushConstants, Is.Not.Null);
            Assert.That(pass.Bindings, Is.Not.Null);
            Assert.That(pass.PushConstants.Count, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void MaterialPass_BindResource_ValidBinding_ShouldAdd()
        {
            var pipeline = _pipelineManager.GetPipelineByName("Geometry");
            var pass = new MaterialPass(pipeline);
            var texture = Texture2D.GetEmptyTexture(_context);
            var binding = new TextureBinding(2, 0, 0, 1, ImageLayout.ShaderReadOnlyOptimal, texture);

            var result = pass.BindResource(binding);

            Assert.That(result, Is.True);
            Assert.That(HasBinding(pass.Bindings, binding), Is.True);
        }

        [Test]
        public void MaterialPass_BindResource_InvalidSet_ShouldReturnFalse()
        {
            var pipeline = _pipelineManager.GetPipelineByName("Geometry");
            var pass = new MaterialPass(pipeline);
            var texture = Texture2D.GetEmptyTexture(_context);
            var binding = new TextureBinding(99, 0, 0, 1, ImageLayout.ShaderReadOnlyOptimal, texture);

            var result = pass.BindResource(binding);

            Assert.That(result, Is.False);
            Assert.That(HasBinding(pass.Bindings, binding), Is.False);
        }

        [Test]
        public void MaterialPass_PushConstant_Valid_ShouldStoreValue()
        {
            var pipeline = _pipelineManager.GetPipelineByName("Solid");
            if (pipeline == null) Assert.Inconclusive("Solid pipeline not found");
            var pass = new MaterialPass(pipeline);
            var expected = new Vector3(0.5f, 0.2f, 0.8f);

            pass.PushConstant("color", expected);

            var field = typeof(MaterialPass).GetField("_pushConstantValues", BindingFlags.NonPublic | BindingFlags.Instance);
            var dict = (Dictionary<string, byte[]>)field?.GetValue(pass);
            Assert.That(dict, Is.Not.Null);
            Assert.That(dict.ContainsKey("color"), Is.True);
            var bytes = dict["color"];
            var actual = System.Runtime.InteropServices.MemoryMarshal.Read<Vector3>(bytes);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void MaterialPass_PushConstant_InvalidName_ShouldThrow()
        {
            var pipeline = _pipelineManager.GetPipelineByName("Solid");
            if (pipeline == null) Assert.Inconclusive("Solid pipeline not found");
            var pass = new MaterialPass(pipeline);

            Assert.Throws<ArgumentException>(() => pass.PushConstant("nonexistent", 42));
        }

        [Test]
        public void MaterialPass_CmdPushConstants_ShouldNotThrow()
        {
            var pipeline = _pipelineManager.GetPipelineByName("Solid");
            if (pipeline == null) Assert.Inconclusive("Solid pipeline not found");
            var pass = new MaterialPass(pipeline);
            pass.PushConstant("color", new Vector3(1, 0, 0));
            var batch = _context.TransferSubmitContext.CreateBatch();

            Assert.DoesNotThrow(() => pass.CmdPushConstants(batch));
        }

        [Test]
        public void MaterialPass_Dispose_ShouldCleanup()
        {
            var pipeline = _pipelineManager.GetPipelineByName("Geometry");
            var pass = new MaterialPass(pipeline);
            pass.BindResource(new TextureBinding(2, 0, 0, 1, ImageLayout.ShaderReadOnlyOptimal, Texture2D.GetEmptyTexture(_context)));
            pass.PushConstant("test", 1.0f);

            pass.Dispose();

            var field = typeof(MaterialPass).GetField("_disposed", BindingFlags.NonPublic | BindingFlags.Instance);
            bool disposed = (bool)field?.GetValue(pass);
            Assert.That(disposed, Is.True);
        }

        #endregion

        #region BindingCollection Tests

        [Test]
        public void BindingCollection_Add_ShouldStoreBinding()
        {
            var collection = new BindingCollection();
            var texture = Texture2D.GetEmptyTexture(_context);
            var binding = new TextureBinding(0, 1, 0, 1, ImageLayout.ShaderReadOnlyOptimal, texture);

            collection.Add(binding);

            Assert.That(collection.Count, Is.EqualTo(1));
            Assert.That(collection.TryGetBindings(0, out var perSet), Is.True);
            Assert.That(perSet.Count, Is.EqualTo(1));
            Assert.That(perSet.Any(b => b == binding), Is.True);
        }

        [Test]
        public void BindingCollection_Remove_ShouldRemoveBinding()
        {
            var collection = new BindingCollection();
            var texture = Texture2D.GetEmptyTexture(_context);
            var binding = new TextureBinding(0, 1, 0, 1, ImageLayout.ShaderReadOnlyOptimal, texture);
            collection.Add(binding);

            var removed = collection.Remove(binding);

            Assert.That(removed, Is.True);
            Assert.That(collection.Count, Is.EqualTo(0));
            Assert.That(collection.TryGetBindings(0, out _), Is.False);
        }

        [Test]
        public void BindingCollection_DynamicOffsets_ShouldCollect()
        {
            var collection = new BindingCollection();
            var ubo = new UniformBufferBinding(new UniformBuffer(_context, 256, true), 0, 0, 0, 1);
            collection.Add(ubo);

            Assert.That(collection.DynamicOffsets.ToArray(), Is.Not.Empty);
            Assert.That(collection.DynamicOffsets[0], Is.EqualTo(0u));
        }

        [Test]
        public void BindingCollection_RemoveAll_ShouldRemoveMatching()
        {
            var collection = new BindingCollection();
            var texBinding = new TextureBinding(0, 1, 0, 1, ImageLayout.ShaderReadOnlyOptimal, Texture2D.GetEmptyTexture(_context));
            var uboBinding = new UniformBufferBinding(new UniformBuffer(_context, 256, false), 2, 0, 0, 1);
            collection.Add(texBinding);
            collection.Add(uboBinding);

            collection.RemoveAll(b => b is TextureBinding);

            Assert.That(collection.Count, Is.EqualTo(1));
            Assert.That(collection.TryGetBindings(0, out var perSet), Is.True);
            Assert.That(perSet.Count, Is.EqualTo(1));
            Assert.That(perSet.Any(b => b == uboBinding), Is.True);
            Assert.That(perSet.Any(b => b == texBinding), Is.False);
        }

        [Test]
        public void BindingCollection_Enumeration_ShouldReturnSetsInOrder()
        {
            var collection = new BindingCollection();
            var binding1 = new TextureBinding(2, 1, 0, 1, ImageLayout.ShaderReadOnlyOptimal, Texture2D.GetEmptyTexture(_context));
            var binding2 = new TextureBinding(0, 1, 0, 1, ImageLayout.ShaderReadOnlyOptimal, Texture2D.GetEmptyTexture(_context));
            collection.Add(binding1);
            collection.Add(binding2);

            var sets = collection.Select(x => x.Set).ToList();
            Assert.That(sets, Is.EqualTo(new[] { 0u, 2u }));
        }

        #endregion

        #region PerSetBindings Tests

        [Test]
        public void PerSetBindings_Add_ShouldStoreBinding()
        {
            var perSet = new PerSetBindings(0);
            var binding = new TextureBinding(0, 1, 0, 1, ImageLayout.ShaderReadOnlyOptimal, Texture2D.GetEmptyTexture(_context));

            perSet.Add(binding);

            Assert.That(perSet.Count, Is.EqualTo(1));
            Assert.That(perSet.Any(b => b == binding), Is.True);
        }

        [Test]
        public void PerSetBindings_Add_DifferentSet_ShouldThrow()
        {
            var perSet = new PerSetBindings(0);
            var binding = new TextureBinding(1, 1, 0, 1, ImageLayout.ShaderReadOnlyOptimal, Texture2D.GetEmptyTexture(_context));

            Assert.Throws<ArgumentException>(() => perSet.Add(binding));
        }

        [Test]
        public void PerSetBindings_Remove_ShouldRemove()
        {
            var perSet = new PerSetBindings(0);
            var binding = new TextureBinding(0, 1, 0, 1, ImageLayout.ShaderReadOnlyOptimal, Texture2D.GetEmptyTexture(_context));
            perSet.Add(binding);

            var removed = perSet.Remove(binding);

            Assert.That(removed, Is.True);
            Assert.That(perSet.Count, Is.EqualTo(0));
        }

        [Test]
        public void PerSetBindings_RemoveAll_ShouldRemoveMatching()
        {
            var perSet = new PerSetBindings(0);
            var texBinding = new TextureBinding(0, 1, 0, 1, ImageLayout.ShaderReadOnlyOptimal, Texture2D.GetEmptyTexture(_context));
            var uboBinding = new UniformBufferBinding(new UniformBuffer(_context, 256, false), 2, 0, 0, 1);
            perSet.Add(texBinding);
            perSet.Add(uboBinding);

            perSet.RemoveAll(b => b is TextureBinding);

            Assert.That(perSet.Count, Is.EqualTo(1));
            Assert.That(perSet.Any(b => b == texBinding), Is.False);
            Assert.That(perSet.Any(b => b == uboBinding), Is.True);
        }

        #endregion

        #region MaterialAsset Tests

        [Test]
        public async Task MaterialAsset_LoadGpuResources_ShouldCreateMaterial()
        {
            var materialData = new MaterialData
            {
                PipelineName = "Geometry",
                Parameters = new Dictionary<string, object>
                {
                    ["exposure"] = 1.0f
                }
            };
            var materialAsset = new MaterialAsset();
            SetAssetData(materialAsset, materialData);
            materialAsset.Name = "TestMaterial";

            await materialAsset.LoadGpuResourcesAsync();

            Assert.That(materialAsset.MaterialInstance, Is.Not.Null);
            Assert.That(materialAsset.MaterialInstance.Name, Is.EqualTo("TestMaterial"));
            Assert.That(materialAsset.MaterialInstance.Passes, Is.Not.Empty);
            var pass = materialAsset.MaterialInstance.Passes.Values.First();
            Assert.That(pass.Pipeline.Name, Is.EqualTo("Geometry"));
        }

        [Test]
        public async Task MaterialAsset_UpdateParameter_ShouldUpdateMaterial()
        {
            var materialData = new MaterialData
            {
                PipelineName = "Solid",
                Parameters = new Dictionary<string, object>
                {
                    ["color"] = new Vector3(1, 0, 0)
                }
            };
            var materialAsset = new MaterialAsset();
            SetAssetData(materialAsset, materialData);
            materialAsset.Name = "TestMaterial";
            await materialAsset.LoadGpuResourcesAsync();
            var material = materialAsset.MaterialInstance;

            materialAsset.UpdateParameter("color", new Vector3(0, 1, 0));

            Assert.That(materialAsset.Parameters["color"], Is.EqualTo(new Vector3(0, 1, 0)));
            // Verify the push constant was updated in the material pass
            var pass = material.Passes.Values.First();
            var field = typeof(MaterialPass).GetField("_pushConstantValues", BindingFlags.NonPublic | BindingFlags.Instance);
            var dict = (Dictionary<string, byte[]>)field?.GetValue(pass);
            Assert.That(dict, Is.Not.Null);
            Assert.That(dict.ContainsKey("color"), Is.True);
            var bytes = dict["color"];
            var vector = System.Runtime.InteropServices.MemoryMarshal.Read<Vector3>(bytes);
            Assert.That(vector, Is.EqualTo(new Vector3(0, 1, 0)));
        }

        [Test]
        public async Task MaterialAsset_AddTexture_ShouldBindTexture()
        {
            // Create texture asset and add to repository
            var textureData = new TextureData(); // empty texture data – will create a default texture
            var textureAsset = new TextureAsset();
            SetAssetData(textureAsset, textureData);
            textureAsset.Name = "TestTexture";
            textureAsset.ID = Guid.NewGuid();
            textureAsset.Path = new AssetPath("Textures", "TestTexture");

            // Add to repository so that AssetReference can resolve it
            _assetRepository.Add(textureAsset);

            // Load GPU resources for the texture (this creates the actual GPU texture)
            await textureAsset.LoadGpuResourcesAsync();

            var textureRef = new AssetReference<TextureAsset>(textureAsset.ID); // Use ID to reference
            var materialData = new MaterialData
            {
                PipelineName = "Geometry"
            };
            var materialAsset = new MaterialAsset();
            SetAssetData(materialAsset, materialData);
            materialAsset.Name = "TestMaterial";

            materialAsset.AddTexture(textureRef);
            await materialAsset.LoadGpuResourcesAsync();

            Assert.That(materialAsset.MaterialInstance, Is.Not.Null);
            var pass = materialAsset.MaterialInstance.Passes.Values.First();

            bool found = false;
            foreach (var bindingCollection in pass.Bindings)
            {
                foreach (var binding in bindingCollection.Item2)
                {
                    if (binding is TextureBinding tb)
                    {
                        // Assume TextureBinding has a property Textures (collection) or Texture (single)
                        // We'll check if it contains our texture
                        if (tb.Textures != null && tb.Textures.Contains(textureAsset.Texture))
                        {
                            found = true;
                            break;
                        }
                    }
                }
                if (found) break;
            }

            Assert.That(found, Is.True, "Texture binding not found");
        }

        [Test]
        public async Task MaterialAsset_UnloadGpuResources_ShouldCleanup()
        {
            var materialData = new MaterialData
            {
                PipelineName = "Geometry"
            };
            var materialAsset = new MaterialAsset();
            SetAssetData(materialAsset, materialData);
            materialAsset.Name = "TestMaterial";
            await materialAsset.LoadGpuResourcesAsync();
            var materialInstance = materialAsset.MaterialInstance;

            materialAsset.UnloadGpuResources();

            Assert.That(materialAsset.MaterialInstance, Is.Null);
            // Check that the material instance was disposed (we can check its disposed flag via reflection)
            var field = typeof(Material).GetField("_disposed", BindingFlags.NonPublic | BindingFlags.Instance);
            bool disposed = (bool)field?.GetValue(materialInstance);
            Assert.That(disposed, Is.True);
        }

        #endregion

        #region Helpers

        private static void SetAssetData<T>(Asset<T> asset, T data) where T : class, new()
        {
            asset.SetData(data);
        }

        #endregion
    }
}*/