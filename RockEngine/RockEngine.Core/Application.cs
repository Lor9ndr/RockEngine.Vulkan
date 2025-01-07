using RockEngine.Core.ECS;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Vulkan;

using Silk.NET.Input;
using Silk.NET.Windowing;

namespace RockEngine.Core
{
    public abstract class Application : IDisposable
    {
        protected RenderingContext _renderingContext;
        protected IWindow _window;
        protected LayerStack _layerStack;
        protected GraphicsEngine _graphicsEngine;
        protected IInputContext _inputContext;
        protected Renderer _renderer;
        protected World _world;
        protected event Func<Task> OnLoad;

        private PipelineManager _pipelineManager;

        public CancellationTokenSource CancellationTokenSource { get; set; }
        protected CancellationToken  CancellationToken => CancellationTokenSource.Token;

        public Application(string appName, int width, int height)
        {
            _window = Window.Create(WindowOptions.DefaultVulkan);
            _window.Title = appName;
            _window.Size = new Silk.NET.Maths.Vector2D<int>(width, height);
            _layerStack = new LayerStack();
            CancellationTokenSource = new CancellationTokenSource();
            _window.Load += async () =>
            {
                _renderingContext = new RenderingContext(_window, _window.Title);
                _graphicsEngine = new GraphicsEngine(_renderingContext);
                _inputContext = _window.CreateInput();
                _pipelineManager = new PipelineManager(_renderingContext);
                _renderer = new Renderer(_renderingContext, _graphicsEngine,_pipelineManager);
                _world = new World();
                await OnLoad?.Invoke();

                await _world.Start(_renderer);
                _window.Render += Render;
                _window.Update += async (s) => await Update(s);
            };
           

        }

        public async Task Run()
        {
            await Task.Run(_window.Run, CancellationToken)
                .ConfigureAwait(false);
        }

        protected virtual async Task Update(double deltaTime)
        {
            Time.Update(_window.Time, deltaTime);
            _layerStack.Update();
            await _world.Update(_renderer);
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
            _renderer.Render(vkCommandBuffer);
            _graphicsEngine.End(vkCommandBuffer);
            _graphicsEngine.Submit([vkCommandBuffer.VkObjectNative]);
        }

        public  Task PushLayer(ILayer layer)
        {
            return _layerStack.PushLayer(layer);
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
