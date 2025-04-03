using Assimp;

using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using System.Numerics;

using Scene = Assimp.Scene;

namespace RockEngine.Core
{
    public class AssimpLoader : IDisposable
    {
        private readonly AssimpContext _assimpContext;
        private readonly LogStream _logStream;
        private readonly TextureStreamer _textureStreamer;
        readonly Dictionary<string, Texture> loadedTextures = new Dictionary<string, Texture>();

        public AssimpLoader(TextureStreamer streamer)
        {
            _assimpContext = new AssimpContext();

            // Create a LogStream that writes to console
            _logStream = new ConsoleLogStream();
            _logStream.Attach();
            _textureStreamer = streamer;
        }

        public async ValueTask<List<MeshData>> LoadMeshesAsync(string filePath, VulkanContext context, VkCommandBuffer? commandBuffer = null)
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
                        vertex.TexCoord = new Vector2(mesh.TextureCoordinateChannels[0][iv].X, mesh.TextureCoordinateChannels[0][iv].Y);
                    }
                    // Add tangent/bitangent data
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

                List<Texture> textures = new List<Texture>();
                var material = scene.Materials[mesh.MaterialIndex];
                if (material.HasTextureDiffuse)
                {
                    var texturePath = material.TextureDiffuse.FilePath;
                    if (!loadedTextures.TryGetValue(texturePath, out var texture))
                    {
                        // Create streamable texture with base mip
                        var baseImage = await Texture.CreateBaseImageAsync(context, Directory.GetParent(filePath) + "\\" + texturePath);
                        texture = new StreamableTexture(
                            context,
                            baseImage.Image,
                            baseImage.ImageView,
                            baseImage.Sampler, Directory.GetParent(filePath) + "\\" + texturePath);

                        // Schedule mip streaming
                        await ((StreamableTexture)texture).StreamNextMipAsync(_textureStreamer, 1.0f);
                        loadedTextures[texturePath] = texture;
                    }
                    textures.Add(texture);
                }
                if (material.HasTextureNormal)
                {
                    var texturePath = material.TextureNormal.FilePath;
                    if (!loadedTextures.TryGetValue(texturePath, out var texture))
                    {
                        texture = await Texture.CreateAsync(context, Directory.GetParent(filePath) + "\\" + texturePath);
                        loadedTextures[texturePath] = texture;
                    }
                    textures.Add(texture);
                }


                if (material.HasTextureDisplacement)
                {
                    var texturePath = material.TextureSpecular.FilePath;
                    if (!loadedTextures.TryGetValue(texturePath, out var texture))
                    {
                        texture = await Texture.CreateAsync(context, Directory.GetParent(filePath) + "\\" + texturePath);
                        loadedTextures[texturePath] = texture;
                    }
                    textures.Add(texture);
                }

                MeshData meshData = new MeshData(mesh.Name, vertices, mesh.GetUnsignedIndices(), textures);
                meshes.Add(meshData);
            }
            return await ValueTask.FromResult(meshes);
        }

        public void Dispose()
        {
            _logStream.Detach();
            _assimpContext.Dispose();
        }
    }

    public record struct MeshData(string Name, Vertex[] Vertices, uint[] Indices, List<Texture> Textures);
}
