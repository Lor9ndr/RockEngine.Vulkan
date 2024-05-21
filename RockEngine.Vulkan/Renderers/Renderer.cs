using Silk.NET.Windowing;

namespace RockEngine.Vulkan.Renderers
{
    public abstract class Renderer : IDisposable
    {
        protected readonly IWindow _window;

        protected Renderer(IWindow window)
        {
            _window = window;
        }

        public abstract Task InitializeAsync();
        public abstract void Render();
        public abstract void Dispose();
    }
}
