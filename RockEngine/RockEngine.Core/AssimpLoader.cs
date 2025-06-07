using Assimp;

using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;

using Scene = Assimp.Scene;

namespace RockEngine.Core
{
    public class AssimpLoader : IDisposable
    {
        private readonly AssimpContext _assimpContext;
        private readonly LogStream _logStream;
        readonly ConcurrentDictionary<string, Texture> loadedTextures = new ConcurrentDictionary<string, Texture>();

        public AssimpLoader()
        {
            _assimpContext = new AssimpContext();

            // Create a LogStream that writes to console
            _logStream = new ConsoleLogStream();
            _logStream.Attach();
        }

        public async Task<List<MeshAssimpData>> LoadMeshesAsync(string filePath, VulkanContext context)
        {
            var pathExtension = Path.GetExtension(filePath);
            if (!_assimpContext.IsImportFormatSupported(pathExtension))
            {
                throw new ArgumentException("Mesh format " + pathExtension + " is not supported!  Cannot load {1}", "filename");
            }
            Scene scene = _assimpContext.ImportFile(filePath, PostProcessSteps.Triangulate | PostProcessSteps.GenerateSmoothNormals | PostProcessSteps.FlipUVs | PostProcessSteps.GlobalScale);

            if (scene == null || scene.MeshCount == 0)
            {
                throw new Exception($"Failed to load mesh from file: {filePath}");
            }

            ConcurrentBag<MeshAssimpData> meshes = new ConcurrentBag<MeshAssimpData>();
            for (int i = 0; i < scene.Meshes.Count; i++)
            {
                int j = i;
                var mesh = scene.Meshes[j];

                var vertices = new Vertex[mesh.VertexCount];
                var loadVerticesTask = Parallel.ForAsync(0, mesh.VertexCount, (iv, ct) =>
                {
                    var vertex = new Vertex
                    {
                        Position = new Vector3(mesh.Vertices[iv].X, mesh.Vertices[iv].Y, mesh.Vertices[iv].Z)
                    };

                    if (mesh.HasNormals)
                    {
                        vertex.Normal = new Vector3(mesh.Normals[iv].X, mesh.Normals[iv].Y, mesh.Normals[iv].Z);
                    }

                    if (mesh.HasTextureCoords(0))
                    {
                        vertex.TexCoord = new Vector2(mesh.TextureCoordinateChannels[0][iv].X, mesh.TextureCoordinateChannels[0][iv].Y);

                    }
                    // Add tangent/bitangent data
                    if (mesh.HasTangentBasis)
                    {
                        var tangent = mesh.Tangents[iv];
                        vertex.Tangent = new Vector3(
                            tangent.X,
                            tangent.Y,
                            tangent.Z
                        );
                        var bitangent = mesh.BiTangents[iv];
                        vertex.Bitangent = new Vector3(
                            bitangent.X,
                            bitangent.Y,
                            bitangent.Z
                        );
                    }

                    vertices[iv] = vertex;
                    return ValueTask.CompletedTask;
                });
                // Currently order of loading textures are important
                List<Texture> textures = new List<Texture>();
                var material = scene.Materials[mesh.MaterialIndex];
                await LoadTextureAsync(context, material.TextureDiffuse, filePath, textures);
                await LoadTextureAsync(context, material.TextureNormal, filePath, textures);
                await LoadTextureAsync(context, material.TextureHeight, filePath, textures);
                await LoadTextureAsync(context, material.TextureSpecular, filePath, textures);
                await LoadTextureAsync(context, material.TextureAmbient, filePath, textures);
                await LoadTextureAsync(context, material.TextureEmissive, filePath, textures);

                await loadVerticesTask;

                MeshAssimpData meshData = new MeshAssimpData(mesh.Name, vertices, mesh.GetUnsignedIndices(), textures);
                meshes.Add(meshData);

            }
            //);
            Debugger.Log(1, "Load mesh", $"Mesh {filePath} was loaded");
            return meshes.ToList();
        }

        private async ValueTask LoadTextureAsync(VulkanContext context, TextureSlot slot, string modelDir, List<Texture> textures)
        {
            if (slot.TextureType != TextureType.None)
            {
                var texturePath = slot.FilePath;
                if (!loadedTextures.TryGetValue(texturePath, out var texture))

                {
                    texture = await Texture.CreateAsync(context,
                        Directory.GetParent(modelDir) + "\\" + texturePath);
                    loadedTextures[texturePath] = texture;
                }
                textures.Add(texture);
            }
        }

        public void Dispose()
        {
            _logStream.Detach();
            _assimpContext.Dispose();
        }
    }

    public record struct MeshAssimpData(string Name, Vertex[] Vertices, uint[] Indices, List<Texture> Textures);
}

/*using Assimp;

using RockEngine.Core.Assets;
using RockEngine.Core.Assets.AssetData;

using System.Numerics;

namespace RockEngine.Core
{
    public class AssimpLoader : IDisposable
    {
        private readonly AssimpContext _assimpContext;
        private readonly LogStream _logStream;

        public AssimpLoader()
        {
            _assimpContext = new AssimpContext();
            _logStream = new ConsoleLogStream();
            _logStream.Attach();
        }

        public async Task<List<MeshData>> LoadMeshesAsync(string filePath, AssetManager assetManager)
        {
            var pathExtension = Path.GetExtension(filePath);
            if (!_assimpContext.IsImportFormatSupported(pathExtension))
            {
                throw new ArgumentException($"Mesh format {pathExtension} is not supported! Cannot load {filePath}");
            }

            Scene scene = _assimpContext.ImportFile(filePath,
                PostProcessSteps.Triangulate |
                PostProcessSteps.GenerateSmoothNormals |
                PostProcessSteps.FlipUVs |
                PostProcessSteps.GlobalScale);

            if (scene == null || scene.MeshCount == 0)
            {
                throw new Exception($"Failed to load mesh from file: {filePath}");
            }

            List<MeshData> meshes = new List<MeshData>(scene.Meshes.Count);
            for (int i = 0; i < scene.Meshes.Count; i++)
            {
                var mesh = scene.Meshes[i];
                var vertices = new Vertex[mesh.VertexCount];

                for (int iv = 0; iv < mesh.VertexCount; iv++)
                {
                    var vertex = new Vertex
                    {
                        Position = new Vector3(mesh.Vertices[iv].X, mesh.Vertices[iv].Y, mesh.Vertices[iv].Z)
                    };

                    if (mesh.HasNormals)
                    {
                        vertex.Normal = new Vector3(mesh.Normals[iv].X, mesh.Normals[iv].Y, mesh.Normals[iv].Z);
                    }

                    if (mesh.HasTextureCoords(0))
                    {
                        vertex.TexCoord = new Vector2(mesh.TextureCoordinateChannels[0][iv].X,
                                                      mesh.TextureCoordinateChannels[0][iv].Y);
                    }

                    if (mesh.HasTangentBasis)
                    {
                        vertex.Tangent = new Vector3(
                            mesh.Tangents[iv].X,
                            mesh.Tangents[iv].Y,
                            mesh.Tangents[iv].Z
                        );
                        vertex.Bitangent = new Vector3(
                            mesh.BiTangents[iv].X,
                            mesh.BiTangents[iv].Y,
                            mesh.BiTangents[iv].Z
                        );
                    }

                    vertices[iv] = vertex;
                }

                List<string> texturePaths = new List<string>();
                var material = scene.Materials[mesh.MaterialIndex];
                CollectTexturePaths(material.TextureDiffuse, filePath, texturePaths);
                CollectTexturePaths(material.TextureNormal, filePath, texturePaths);
                CollectTexturePaths(material.TextureHeight, filePath, texturePaths);
                CollectTexturePaths(material.TextureSpecular, filePath, texturePaths);
                CollectTexturePaths(material.TextureAmbient, filePath, texturePaths);
                CollectTexturePaths(material.TextureEmissive, filePath, texturePaths);

                MeshData meshData = new MeshData(
                    mesh.Name,
                    vertices,
                    mesh.GetUnsignedIndices(),
                    texturePaths)
                {
                    SourcePath = filePath
                };

                meshes.Add(meshData);
            }
            return meshes;
        }

        private void CollectTexturePaths(TextureSlot slot, string modelDir, List<string> texturePaths)
        {
            if (slot.TextureType != TextureType.None)
            {
                var texturePath = slot.FilePath;
                var fullPath = Path.Combine(Path.GetDirectoryName(modelDir), texturePath);
                if (File.Exists(fullPath))
                {
                    texturePaths.Add(fullPath);
                }
            }
        }

        public void Dispose()
        {
            _logStream.Detach();
            _assimpContext.Dispose();
        }
    }
}*/