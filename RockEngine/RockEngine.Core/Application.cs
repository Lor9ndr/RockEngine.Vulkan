using RockEngine.Core.ECS;
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
        protected World _world;
        protected event Action OnLoad;

        public CancellationTokenSource CancellationTokenSource { get; set; }
        protected CancellationToken  CancellationToken => CancellationTokenSource.Token;


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
                _world = new World();
                OnLoad?.Invoke();
            };
            _window.Render += Render;
            _window.Update += Update;

        }

        public async Task Run()
        {
            await Task.Run(() => _window.Run(), CancellationToken)
                .ConfigureAwait(false);
        }

        protected virtual void Update(double deltaTime)
        {
             Time.Update(_window.Time, deltaTime);
             _layerStack.Update();
        }

        protected virtual void Render(double time)
        {
            if (_layerStack.Count == 0)
            {
                return;
            }
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
            CancellationTokenSource.Cancel();
            _graphicsEngine.Dispose();

            _renderingContext.Dispose();
            _window.Close();
            _window.Dispose();
        }
    }

}
