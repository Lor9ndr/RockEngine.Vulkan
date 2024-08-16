using RockEngine.Vulkan.Assets;
using Assimp;
using System.Numerics;
using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.Rendering.ComponentRenderers;
using RockEngine.Vulkan.VkObjects;
using Scene = Assimp.Scene;

namespace RockEngine.Vulkan
{
    internal class AssimpLoader : IDisposable
    {
        private readonly AssimpContext _assimpContext;
        private readonly LogStream _logStream;

        public AssimpLoader()
        {
            _assimpContext = new AssimpContext();

            // Create a LogStream that writes to console
            _logStream = new ConsoleLogStream();
            _logStream.Attach();
        }

        public async ValueTask<List<MeshData>> LoadMeshesAsync(string filePath)
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

                    vertices[iv] = vertex;
                }
               
                List<Texture> textures = new List<Texture>();
                if (scene.Materials[mesh.MaterialIndex].HasTextureDiffuse)
                {
                    var texturePath = scene.Materials[mesh.MaterialIndex].TextureDiffuse.FilePath;
                    var texture = new Texture(Directory.GetParent(filePath) + "\\" + texturePath);
                    textures.Add(texture);
                }
               /* if (scene.Materials[mesh.MaterialIndex].HasTextureNormal)
                {
                    var texturePath = scene.Materials[mesh.MaterialIndex].TextureNormal.FilePath;
                    var texture = new Texture(Directory.GetParent(filePath) + "\\" + texturePath);
                    textures.Add(texture);
                }*/
               /* if (scene.Materials[mesh.MaterialIndex].HasTextureSpecular)
                {
                    var texturePath = scene.Materials[mesh.MaterialIndex].TextureSpecular.FilePath;
                    var texture = new Texture(Directory.GetParent(filePath) + "\\" + texturePath);
                    textures.Add(texture);
                }*/

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
}
