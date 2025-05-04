
using RockEngine.Core.ECS.Components;

namespace RockEngine.Core.Rendering.Commands
{
    internal class MeshRenderCommand : IRenderCommand
    {
        public Mesh Mesh { get; }
        public uint TransformIndex { get; set; }
        public RenderLayerType RenderLayerType { get; set; }

        public MeshRenderCommand(Mesh mesh)
        {
            Mesh = mesh;
            RenderLayerType = mesh.Entity.Layer;
        }
    }
}
