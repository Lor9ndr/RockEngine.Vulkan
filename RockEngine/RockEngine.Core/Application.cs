using RockEngine.Core.ECS;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace RockEngine.Core
{
    public abstract class Application : IDisposable
    {
        protected VulkanContext _context;
        protected IWindow _window;
        protected LayerStack _layerStack;
        protected GraphicsEngine _graphicsEngine;
        protected IInputContext _inputContext;
        protected Renderer _renderer;
        protected World _world;
        protected event Func<Task> OnLoad;
        protected TextureStreamer _textureStreamer;

        private PipelineManager _pipelineManager;

        public CancellationTokenSource CancellationTokenSource { get; set; }
        protected CancellationToken CancellationToken => CancellationTokenSource.Token;

        public Application(string appName, int width, int height)
        {
            _window = Window.Create(WindowOptions.DefaultVulkan);
            _window.Title = appName;
            _window.Size = new Vector2D<int>(width, height);
            _layerStack = new LayerStack();
            CancellationTokenSource = new CancellationTokenSource();
            _window.Load += async () =>
            {
                _context = new VulkanContext(_window, _window.Title, 10);
                _graphicsEngine = new GraphicsEngine(_context);
                _inputContext = _window.CreateInput();
                _pipelineManager = new PipelineManager(_context);
                _renderer = new Renderer(_context, _graphicsEngine, _pipelineManager);
                _world = new World();
                _textureStreamer = new TextureStreamer(_context, _renderer);
                await OnLoad?.Invoke();

                await _world.Start(_renderer);
                _window.Render += async(s) => await Render(s);
                _window.Update += async (s) => await Update(s);
            };
        }

        public Task Run()
        {
            return Task.Factory.StartNew(_window.Run, CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        protected virtual async Task Update(double deltaTime)
        {

            Time.Update(_window.Time, deltaTime);
            _layerStack.Update();
            await _world.Update(_renderer)
                .ConfigureAwait(false);

            await _renderer.UpdateFrameData();
            await _context.SubmitContext.FlushAsync();


        }

        protected virtual async Task Render(double time)
        {
            _context.DisposePendingDisposals();
            using (PerformanceTracer.BeginSection("Whole Render"))
            {
                if (_layerStack.Count == 0)
                {
                    return;
                }
                var vkCommandBuffer = _graphicsEngine.Begin();
                if (vkCommandBuffer is null)
                {
                    return;
                }
                using (PerformanceTracer.BeginSection("_layerStack.RenderImGui"))
                {
                    _layerStack.RenderImGui(vkCommandBuffer);
                }
                using (PerformanceTracer.BeginSection("_layerStack.Render"))
                {
                    _layerStack.Render(vkCommandBuffer);
                }
                await _renderer.Render(vkCommandBuffer);
                using (PerformanceTracer.BeginSection("_graphicsEngine.end & Submit"))
                {
                    _graphicsEngine.End(vkCommandBuffer);
                    _graphicsEngine.SubmitAndPresent([vkCommandBuffer.VkObjectNative]);
                }
            }

        }

        public Task PushLayer(ILayer layer)
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
            _renderer.Dispose();
            _graphicsEngine.Dispose();

            _context.Dispose();
            _window.Close();
            _window.Dispose();
        }
    }

}
