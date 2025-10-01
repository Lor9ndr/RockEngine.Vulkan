using NSubstitute;

using RockEngine.Core;
using RockEngine.Core.Assets;
using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.Assets.Serializers;
using RockEngine.Core.DI;

using Vertex = RockEngine.Core.Vertex;

namespace RockEngine.Tests
{


namespace RockEngine.Core.Tests.Assets
    {
        public class MeshLoadingTests
        {
            private static AssetManager _assetManager;
            private static ProjectAsset _project;
            private static IAssetSerializer _serializer;
            private static AssimpLoader _assimpLoader;

            [Before(Class)]
            public static async Task Setup()
            {
                // Create mock dependencies
                _serializer = Substitute.For<IAssetSerializer>();

                _assimpLoader = Substitute.For<AssimpLoader>();
                IoC.Initialize();
               

                // Initialize AssetManager with mock dependencies
                _assetManager = IoC.Container.GetInstance<AssetManager>();

                // Create a test project
                _project = await _assetManager.CreateProjectAsync("TestProject", "\\test");
            }

            [Test]
            public async Task MeshAsset_Should_Be_Registered_In_AssetManager()
            {
                // Act
                var model =  await _assetManager.LoadModelAsync("Resources/Models/Revoulier/Cerberus_LP.FBX", "Revoulier");
                var meshId = model.Parts[0].Mesh.ID;

                // Assert
                var registeredAsset = _assetManager.GetAsset<MeshAsset>(meshId);
                 await Assert.That(registeredAsset).IsNotNull();
                 await Assert.That(registeredAsset.Name).StartsWith("Mesh_");
            }

            [Test]
            public async Task SetGeometry_Should_Update_MeshData_Correctly()
            {
                // Arrange
                
                var mesh = _assetManager.Create<MeshAsset>("Test/Meshes", "TestMesh");
                var vertices = DefaultMeshes.Cube.Vertices;
                var indices = DefaultMeshes.Cube.Indices;

                // Act
                mesh.SetGeometry(vertices, indices);

                var data = mesh.GetData() as MeshData;
                // Assert
                 await Assert.That(data.Vertices).IsEqualTo(vertices);
                 await Assert.That(data.Indices).IsEqualTo(indices);
                 await _assetManager.SaveAsync(mesh);
                 mesh.SetData(null);

                 await mesh.LoadDataAsync();
                for (int i = 0; i < data.Vertices.Length; i++)
                {
                    Vertex item = data.Vertices[i];
                     await Assert.That(item).IsEqualTo(vertices[i]);
                }
                for (int i = 0; i < data.Indices.Length; i++)
                {
                    var item = data.Indices[i];
                     await Assert.That(item).IsEqualTo(indices[i]);
                }
            }

            [Test]
            public async Task LoadModelAsync_WithInvalidFilePath_ShouldThrowFileNotFoundException()
            {
                // Arrange
                var invalidPath = "Invalid/Path/To/Model.fbx";

                // Act & Assert
                 await Assert.ThrowsAsync<FileNotFoundException>(async () =>
                     await _assetManager.LoadModelAsync(invalidPath, "TestModel"));
            }

        
            [Test]
            public async Task SetGeometry_WithNullVertices_ShouldThrowArgumentNullException()
            {
                // Arrange
                var mesh = _assetManager.Create<MeshAsset>("Test/Meshes", "NullVerticesTest");

                // Act & Assert
                 await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                    mesh.SetGeometry(null, new uint[0]));
            }



            [Test]
            public async Task LoadMetadataAsync_ForNonExistentAsset_ShouldThrowFileNotFoundException()
            {
                // Arrange
                var invalidPath = new AssetPath("Invalid/Path", "NonExistent.asset");

                // Act & Assert
                 await Assert.ThrowsAsync<FileNotFoundException>(async () =>
                     _assetManager.GetMetadataByPath(invalidPath.FullPath));
            }

            [Test]
            public async Task GetAsset_WithInvalidId_ShouldReturnNull()
            {
                // Arrange
                var invalidId = Guid.NewGuid();

                // Act
                var result = _assetManager.GetAsset<MeshAsset>(invalidId);

                // Assert
                 await Assert.That(result).IsNull();
            }



            [Test]
            public async Task Serialize_Then_Deserialize_MeshAsset_Should_Preserve_Data()
            {
                // Arrange
                var originalMesh = _assetManager.Create<MeshAsset>(new AssetPath(nameof(Serialize_Then_Deserialize_MeshAsset_Should_Preserve_Data)), "TestMesh");
                originalMesh.SetData(DefaultMeshes.Cube);

                var serializer = IoC.Container.GetInstance<IAssetSerializer>();
                using var stream = new MemoryStream();

                // Act - Serialize
                await serializer.SerializeAsync(originalMesh, stream);

                // Reset stream for reading
                stream.Position = 0;

                // Act - Deserialize
                var metadata = await serializer.DeserializeMetadataAsync(stream);
                stream.Position = 0;
                var data = await serializer.DeserializeDataAsync(stream, typeof(MeshData));

                // Assert
                 await Assert.That(metadata.Name).IsEqualTo("TestMesh");
                 await Assert.That(data).IsTypeOf<MeshData>();

                var meshData = (MeshData)data;
                await Assert.That((uint)meshData.Vertices.Length).IsEqualTo(originalMesh.VerticesCount);
                await Assert.That(originalMesh.HasIndices).IsEqualTo(true);
                await Assert.That((uint)meshData.Indices.Length).IsEqualTo(originalMesh.IndicesCount.Value);
            }

            [Test]
            public async Task LoadingSameAssetById_ShouldReturnSameInstanceAndData()
            {
                // Arrange
                var mesh = _assetManager.Create<MeshAsset>($"Test/{nameof(LoadingSameAssetById_ShouldReturnSameInstanceAndData)}", "TestMesh");
                var vertices = DefaultMeshes.Cube.Vertices;
                var indices = DefaultMeshes.Cube.Indices;
                mesh.SetGeometry(vertices, indices);
                await _assetManager.SaveAsync(mesh);
                var id = mesh.ID;

                // Act: Retrieve asset twice
                var mesh1 = _assetManager.GetAsset<MeshAsset>(id);
                var mesh2 = _assetManager.GetAsset<MeshAsset>(id);


                // Assert 1: Verify same instance (caching behavior)
                await Assert.That(ReferenceEquals(mesh1, mesh2)).IsTrue();

                // Act: Load data for both references
                await mesh1.LoadDataAsync();
                await mesh2.LoadDataAsync();
                var data1 = (MeshData)mesh1.GetData();
                var data2 = (MeshData)mesh2.GetData();

                // Assert 2: Verify data consistency
                await Assert.That(data1.Vertices).IsEqualTo(data2.Vertices);
                await Assert.That(data1.Indices).IsEqualTo(data2.Indices);

                


                mesh = (MeshAsset)await _assetManager.LoadAsync<MeshData>(mesh.Path);
                await Assert.That(mesh.ID).IsEqualTo(id);

            }
        }
    }
}