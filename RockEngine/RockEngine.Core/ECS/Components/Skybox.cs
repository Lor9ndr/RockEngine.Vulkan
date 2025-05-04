using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Texturing;

namespace RockEngine.Core.ECS.Components
{
    public class Skybox : Component
    {
        public Texture Cubemap { get; set; }
        public Material Material { get; private set;}
        private Mesh _mesh;

        public override async ValueTask OnStart(Renderer renderer)
        {
            _mesh = new Mesh();
            Material = new Material(renderer.PipelineManager.GetPipelineByName("Skybox"), Cubemap);
            Entity.ChangeLayer(RenderLayerType.Solid);
            _mesh.SetEntity(this.Entity);
            Vertex[] vertices =
            [
                 // Front face (Z+)
               new Vertex(-1.0f, -1.0f,  1.0f,0,0,0,0,0), // 0
               new Vertex(  1.0f, -1.0f,  1.0f, 0, 0, 0, 0, 0), // 1
               new Vertex(  1.0f,  1.0f,  1.0f, 0, 0, 0, 0, 0), // 2
               new Vertex( -1.0f,  1.0f,  1.0f, 0, 0, 0, 0, 0), // 3
    
                // Back face (Z-)
               new Vertex( -1.0f, -1.0f, -1.0f,0,0,0,0,0), // 4
               new Vertex(  1.0f, -1.0f, -1.0f,0,0,0,0,0), // 5
               new Vertex(  1.0f,  1.0f, -1.0f,0,0,0,0,0), // 6
               new Vertex( -1.0f,  1.0f, -1.0f,0,0,0,0,0), // 7
            ];
            _mesh.SetMeshData(vertices,
            [
                // Front face
                0, 1, 2, 2, 3, 0,
    
                // Back face
                5, 4, 7, 7, 6, 5,
    
                // Left face
                4, 0, 3, 3, 7, 4,
    
                // Right face
                1, 5, 6, 6, 2, 1,
    
                // Top face
                3, 2, 6, 6, 7, 3,
    
                // Bottom face
                4, 5, 1, 1, 0, 4
            ]);
            _mesh.Material = Material;
            await _mesh.OnStart(renderer);
        }
        public override ValueTask Update(Renderer renderer)
        {
            Entity.Transform.Scale = new System.Numerics.Vector3(10000, 10000, 10000);

            return default;
        }
    }
}
