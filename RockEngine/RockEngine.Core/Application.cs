using RockEngine.Core.Rendering;
using RockEngine.Vulkan;

using Silk.NET.Input;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl;

namespace RockEngine.Core
{
    public abstract class Application : IDisposable
    {
        protected RenderingContext _renderingContext;
        protected IWindow _window;
        protected LayerStack _layerStack;
        protected GraphicsEngine _graphicsEngine;
        protected IInputContext _inputContext;
        protected CancellationTokenSource CancellationTokenSource { get; set; }
        protected CancellationToken  CancellationToken => CancellationTokenSource.Token;

        protected event Action OnLoad;

        public Application(string appName, int width, int height)
        {
            SdlWindowing.Use();
            _window = Window.Create(WindowOptions.DefaultVulkan);
            _window.Title = appName;
            _window.Size = new Silk.NET.Maths.Vector2D<int>(width, height);
            _layerStack = new LayerStack();
            CancellationTokenSource = new CancellationTokenSource();
            _window.Load += () =>
            {
                _renderingContext = new RenderingContext(_window, _window.Title);
                _graphicsEngine = new GraphicsEngine(_renderingContext);
                _inputContext = _window.CreateInput();
                OnLoad?.Invoke();
            };
            _window.Render += Render;
            _window.Update += Update;

        }

        public async Task Run()
        {
            try
            {
                await Task.Run(() => _window.Run(), CancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation
            }
        }

        protected virtual void Update(double deltaTime)
        {
             Time.Update(_window.Time);
             _layerStack.Update();
        }

        protected virtual void Render(double time)
        {
            var vkCommandBuffer =  _graphicsEngine.Begin();
            if (vkCommandBuffer is null)
            {
                return;
            }
            _layerStack.RenderImGui(vkCommandBuffer);
            _layerStack.Render(vkCommandBuffer);
            _graphicsEngine.End(vkCommandBuffer);
            _graphicsEngine.Submit([vkCommandBuffer.VkObjectNative]);
        }


        public void PushLayer(ILayer layer)
        {
            _layerStack.PushLayer(layer);
        }

        public void PopLayer(ILayer layer)
        {
            _layerStack.PopLayer(layer);
        }

        public virtual void Dispose()
        {
            _graphicsEngine.Dispose();
            _renderingContext.Dispose();
            _window.Dispose();
        }
    }

}
