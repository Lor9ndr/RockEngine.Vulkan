using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Assimp;
using Scene = Assimp.Scene;
using TextureType = Assimp.TextureType;

namespace RockEngine.Core
{
    public class AssimpLoader : IDisposable
    {
        private AssimpContext _assimpContext;

        public AssimpLoader()
        {
            _assimpContext = new AssimpContext();
        }
        public async Task<List<MeshAssimpData>> LoadMeshesAsync(string filePath)
        {
            // Create a LogStream that writes to console
            var logStream = new ConsoleLogStream();
            logStream.Attach();


            var pathExtension = Path.GetExtension(filePath);
            if (!_assimpContext.IsImportFormatSupported(pathExtension))
            {
                throw new ArgumentException("Mesh format " + pathExtension + " is not supported!  Cannot load {1}", "filename");
            }
            Scene scene = _assimpContext.ImportFile(filePath, PostProcessSteps.Triangulate |
                PostProcessSteps.GenerateSmoothNormals |
                PostProcessSteps.FlipUVs |
                PostProcessSteps.GlobalScale |
                PostProcessSteps.OptimizeGraph |
                PostProcessSteps.CalculateTangentSpace);

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
                        Position = new Vector4(mesh.Vertices[iv].X, mesh.Vertices[iv].Y, mesh.Vertices[iv].Z, 0)
                    };

                    if (mesh.HasNormals)
                    {
                        vertex.Normal = new Vector4(mesh.Normals[iv].X, mesh.Normals[iv].Y, mesh.Normals[iv].Z, 0);
                    }

                    if (mesh.HasTextureCoords(0))
                    {
                        vertex.TexCoord = new Vector2(mesh.TextureCoordinateChannels[0][iv].X, mesh.TextureCoordinateChannels[0][iv].Y);

                    }
                    // Add tangent/bitangent data
                    if (mesh.HasTangentBasis)
                    {
                        var tangent = mesh.Tangents[iv];
                        vertex.Tangent = new Vector4(
                            tangent.X,
                            tangent.Y,
                            tangent.Z,
                            0
                        );
                        var bitangent = mesh.BiTangents[iv];
                        vertex.Bitangent = new Vector4(
                            bitangent.X,
                            bitangent.Y,
                            bitangent.Z,
                            0
                        );
                    }

                    vertices[iv] = vertex;
                    return ValueTask.CompletedTask;
                });
                await loadVerticesTask.ConfigureAwait(false);
                // Currently order of loading textures are important
                List<TextureSlot> textures = new List<TextureSlot>();
                var material = scene.Materials[mesh.MaterialIndex];
                GetTexturePath(material.TextureDiffuse, filePath, textures);
                GetTexturePath(material.TextureNormal, filePath, textures);
                GetTexturePath(material.TextureHeight, filePath, textures);
                GetTexturePath(material.TextureSpecular, filePath, textures);
                GetTexturePath(material.TextureAmbient, filePath, textures);
                GetTexturePath(material.TextureEmissive, filePath, textures);

                List<TextureSlotWithSemantic> texturesWithSemantic = new List<TextureSlotWithSemantic>();
                foreach (var slot in textures) // your original list
                {
                    var semantic = GetSemanticFromTextureSlot(slot, filePath);
                    texturesWithSemantic.Add(new TextureSlotWithSemantic(slot, semantic));
                }
                MeshAssimpData meshData = new MeshAssimpData(mesh.Name, vertices, mesh.GetUnsignedIndices(), texturesWithSemantic);
                meshes.Add(meshData);

            }
            logStream.Detach();
            //);
            Debugger.Log(1, "Load mesh", $"Mesh {filePath} was loaded");
            return meshes.ToList();

        }

        private void GetTexturePath(TextureSlot slot, string modelDir, List<TextureSlot> textures)
        {
            if (slot.TextureType != TextureType.None)
            {
                var texturePath = slot.FilePath;
                slot.FilePath = Directory.GetParent(modelDir) + "\\" + texturePath;
                textures.Add(slot);
            }
        }
        private TextureSemantic GetSemanticFromTextureSlot(TextureSlot slot, string modelDir)
        {
            string fileName = Path.GetFileName(slot.FilePath).ToLowerInvariant();

            /* // Combined MRA texture (usually named "mra", "orm", "pbr")
             if (fileName.Contains("mra") || fileName.Contains("orm") || fileName.Contains("pbr"))
                 return TextureSemantic.MRA;

             // Separate maps
             if (fileName.Contains("albedo") || fileName.Contains("diffuse") || fileName.Contains("basecolor"))
                 return TextureSemantic.Albedo;
             if (fileName.Contains("normal") || fileName.Contains("bump"))
                 return TextureSemantic.Normal;
             if (fileName.Contains("metallic") || fileName.Contains("metalness"))
                 return TextureSemantic.Metallic;
             if (fileName.Contains("roughness"))
                 return TextureSemantic.Roughness;
             if (fileName.Contains("ao") || fileName.Contains("ambientocclusion") || fileName.Contains("occlusion"))
                 return TextureSemantic.AmbientOcclusion;
             if (fileName.Contains("emissive"))
                 return TextureSemantic.Emissive;*/

            // If we have a known Assimp type, use that as fallback
            return slot.TextureType switch
            {
                TextureType.Diffuse => TextureSemantic.Albedo,
                TextureType.Normals => TextureSemantic.Normal,
                TextureType.Emissive => TextureSemantic.Emissive,
                _ => TextureSemantic.Unknown
            };
        }

        public void Dispose()
        {

        }
    }
    public enum TextureSemantic
    {
        Albedo,
        Normal,
        Metallic,
        Roughness,
        AmbientOcclusion,
        MRA, // Combined Metallic-Roughness-AO texture
        Emissive,
        Unknown
    }
    public record struct TextureSlotWithSemantic(TextureSlot Slot, TextureSemantic Semantic);
    public record struct MeshAssimpData(string Name, Vertex[] Vertices, uint[] Indices, List<TextureSlotWithSemantic> Textures);
}
