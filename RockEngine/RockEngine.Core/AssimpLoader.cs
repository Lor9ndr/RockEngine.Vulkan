using Assimp;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;

using Scene = Assimp.Scene;
using TextureType = Assimp.TextureType;

namespace RockEngine.Core
{
	public class AssimpLoader : IDisposable
	{
		public async Task<List<MeshAssimpData>> LoadMeshesAsync(string filePath)
		{

			var assimpContext = new AssimpContext();

			// Create a LogStream that writes to console
			var logStream = new ConsoleLogStream();
			logStream.Attach();


			var pathExtension = Path.GetExtension(filePath);
			if (!assimpContext.IsImportFormatSupported(pathExtension))
			{
				throw new ArgumentException("Mesh format " + pathExtension + " is not supported!  Cannot load {1}", "filename");
			}
			Scene scene = assimpContext.ImportFile(filePath, PostProcessSteps.Triangulate |
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
				await loadVerticesTask;
				// Currently order of loading textures are important
				List<string> textures = new List<string>();
				var material = scene.Materials[mesh.MaterialIndex];
				GetTexturePath(material.TextureDiffuse, filePath, textures);
				GetTexturePath(material.TextureNormal, filePath, textures);
				GetTexturePath(material.TextureHeight, filePath, textures);
				GetTexturePath(material.TextureSpecular, filePath, textures);
				GetTexturePath(material.TextureAmbient, filePath, textures);
				GetTexturePath(material.TextureEmissive, filePath, textures);


				MeshAssimpData meshData = new MeshAssimpData(mesh.Name, vertices, mesh.GetUnsignedIndices(), textures);
				meshes.Add(meshData);

			}
			logStream.Detach();
			assimpContext.Dispose();
			//);
			Debugger.Log(1, "Load mesh", $"Mesh {filePath} was loaded");
			return meshes.ToList();

		}

		private void GetTexturePath(TextureSlot slot, string modelDir, List<string> textures)
		{
			if (slot.TextureType != TextureType.None)
			{
				var texturePath = slot.FilePath;
				var texture = Directory.GetParent(modelDir) + "\\" + texturePath;
				textures.Add(texture);
			}
		}

		public void Dispose()
		{

		}
	}

	public record struct MeshAssimpData(string Name, Vertex[] Vertices, uint[] Indices, List<string> Textures);
}
