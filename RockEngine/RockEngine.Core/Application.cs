using NLog;

using RockEngine.Core.DI;
using RockEngine.Core.ECS;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Shaders;
using RockEngine.Core.TPL;
using RockEngine.Vulkan;

using Silk.NET.Input;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;

using SimpleInjector;
using SimpleInjector.Lifestyles;

using System.Diagnostics;

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
        protected PipelineManager _pipelineManager;
        protected AssimpLoader _assimpLoader;
        protected readonly AppSettings _appSettings;

        private IShaderManager _shaderManager;
        private Task[] _renderTasks;
        private ImGuiSynchronizationContext _imguiContext;
        private readonly Container _container;
        private readonly Scope _applicationScope;

        public CancellationTokenSource CancellationTokenSource { get; set; }
        protected CancellationToken CancellationToken => CancellationTokenSource.Token;
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();



        public Application()
        {
            // Initialize DI container if not already initialized
            if (!IoC.Container.IsLocked)
            {
                IoC.Initialize();
            }
            _applicationScope = AsyncScopedLifestyle.BeginScope(IoC.Container);

            _appSettings = IoC.Container.GetInstance<AppSettings>();
        }

        public void Run()
        {
            CancellationTokenSource = new CancellationTokenSource();
            _imguiContext = new ImGuiSynchronizationContext();

            // Configure window
            _window = IoC.Container.GetInstance<IWindow>();
            _window.Title = _appSettings.Name;
            _window.Size = _appSettings.LoadSize;
            _window.IsEventDriven = false;

            // Resolve other dependencies
            _world = IoC.Container.GetInstance<World>();
            _assimpLoader = IoC.Container.GetInstance<AssimpLoader>();
            _window.Load += () =>
            {
                try
                {
                    OnWindowLoad().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to load window.");
                    throw;
                }
            };

            _window.Run();
        }

        private async Task OnWindowLoad()
        {
            Stopwatch loadWatch = Stopwatch.StartNew();
            _inputContext = IoC.Container.GetInstance<IInputContext>();

            // Resolve window-dependent components
            _context = IoC.Container.GetInstance<VulkanContext>();
            _graphicsEngine = IoC.Container.GetInstance<GraphicsEngine>();
            _pipelineManager = IoC.Container.GetInstance<PipelineManager>();
            _renderer = IoC.Container.GetInstance<Renderer>();
            _shaderManager = IoC.Container.GetInstance<IShaderManager>();
            _renderTasks = new Task[2];




            await _shaderManager.CompileAllShadersAsync();

            PerformanceTracer.Initialize(_context);
            PerformanceTracer.ProcessQueries(_context, _graphicsEngine.FrameIndex);
            PerformanceTracer.BeginFrame(_graphicsEngine.FrameIndex);
            await _renderer.InitializeAsync().ConfigureAwait(false);
            await _world.Start(_renderer).ConfigureAwait(false);

            _logger.Info($"Core systems initialized in: {loadWatch.ElapsedMilliseconds} ms");

            _layerStack = IoC.Container.GetInstance<LayerStack>();

           
            await Load().ConfigureAwait(false);
            _window.Update += (s) =>
            {
                try
                {
                    Update(s).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unhandled exception in Update. Closing application. :{0}, \n {1}", ex.Message, ex.StackTrace);
                    throw;
                }
            };

            _window.Render += (s) =>
            {
                try
                {
                    Render(s).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unhandled exception in Render. Closing application. :{0}, \n {1}", ex.Message, ex.StackTrace);
                    throw;
                }
            };

            loadWatch.Stop();
            _logger.Info($"Application loaded in: {loadWatch.ElapsedMilliseconds} ms");

        }


        protected virtual async Task Load()
        {
            foreach (var item in IoC.Container.GetAllInstances<ILayer>())
            {
                await _layerStack.PushLayer(item);
            }
        }

        protected virtual async Task Update(double deltaTime)
        {
            using (PerformanceTracer.BeginSection("Update"))
            {
                Time.Update(_window.Time, deltaTime);
                _layerStack.Update();
                _imguiContext.Update();

                await _world.Update(_renderer).ConfigureAwait(false);
                await _renderer.UpdateFrameData().ConfigureAwait(false);
            }
        }

        protected virtual async Task Render(double time)
        {
            using (PerformanceTracer.BeginSection("Whole Render"))
            {
                if (_layerStack.Count == 0)
                {
                    return;
                }

                var batch = _graphicsEngine.Begin();
                if (batch is null)
                {
                    return;
                }

                var frameIndex = _renderer.FrameIndex;
                
                PerformanceTracer.ProcessQueries(_context, frameIndex);
                PerformanceTracer.BeginFrame(frameIndex);

                var prevContext = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(_imguiContext);
                using (PerformanceTracer.BeginSection("_layerStack.RenderImGui"))
                {
                    using (PerformanceTracer.BeginSection("RenderImGui", batch.CommandBuffer, frameIndex))
                    {
                        await _layerStack.RenderImGui(batch.CommandBuffer).ConfigureAwait(true);
                    }
                }
                _imguiContext.Update();
                SynchronizationContext.SetSynchronizationContext(prevContext);

                _renderTasks[0] = Task.Run(() => {
                    using (PerformanceTracer.BeginSection("_layerStack.Render"))
                    {
                        var renderBatch = _context.GraphicsSubmitContext.CreateBatch();
                        using (PerformanceTracer.BeginSection("_layerStack.Render", renderBatch.CommandBuffer, frameIndex))
                        {
                            _layerStack.Render(renderBatch.CommandBuffer);
                        }
                        renderBatch.Submit();
                    }
                });

                _renderTasks[1] = Task.Run(async ()=>
                {
                    using (PerformanceTracer.BeginSection("_renderer.Render()"))
                    {
                        await _renderer.Render();
                    }
                });
                await Task.WhenAll(_renderTasks).ConfigureAwait(false);
                using (PerformanceTracer.BeginSection("_graphicsEngine.end & Submit"))
                {
                    _graphicsEngine.SubmitAndPresent(batch);
                }
            }

        }

        public Task PushLayer(ILayer layer) => _layerStack.PushLayer(layer);
        public void PopLayer(ILayer layer) => _layerStack.PopLayer(layer);


        public virtual void Dispose()
        {
            CancellationTokenSource?.Cancel();
            _context.Device.GraphicsQueue.WaitIdle();
            _context.Device.ComputeQueue.WaitIdle();

            _renderer?.Dispose();
            _graphicsEngine?.Dispose();
            _context?.Dispose();

            if (_window != null)
            {
                _window.Close();
                _window.Dispose();
            }

            _applicationScope?.Dispose();
            _container?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}