using RockEngine.Core.ECS.Components;

namespace RockEngine.Core.Rendering.Commands
{
    internal readonly record struct RenderCameraCommand : IRenderCommand
    {
        public Camera Camera { get; }
        public RenderCameraCommand(Camera camera)
        {
            Camera = camera;
        }
    }
}
