using Silk.NET.Windowing;

namespace RockEngine.Vulkan.Renderers
{
    internal class DebugRenderer : Renderer
    {
        public DebugRenderer(IWindow window) 
            : base(window)
        {
        }

        public override Task InitializeAsync()
        {
            throw new NotImplementedException();
        }

        public override void Render()
        {
            throw new NotImplementedException();
        }
        public override void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
