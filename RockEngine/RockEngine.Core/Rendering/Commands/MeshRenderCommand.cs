using RockEngine.Core.ECS.Components;

namespace RockEngine.Core.Rendering.Commands
{
    internal readonly record struct MeshRenderCommand : IRenderCommand
    {
        public Mesh Mesh { get; }

        public MeshRenderCommand(Mesh mesh)
        {
            Mesh = mesh;
        }
    }
}
