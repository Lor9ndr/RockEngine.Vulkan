using NLog;

using RockEngine.Core.DI;
using RockEngine.Core.ECS;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Vulkan;

using Silk.NET.Input;
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
        private IShaderManager _shaderManager;
        protected World _world;
        protected PipelineManager _pipelineManager;
        protected AssimpLoader _assimpLoader;
        protected readonly AppSettings _appSettings;

        private readonly Container _container;
        private readonly Scope _applicationScope;

        public CancellationTokenSource CancellationTokenSource { get; set; }
        protected CancellationToken CancellationToken => CancellationTokenSource.Token;
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly SemaphoreSlim _renderingLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _updateLock = new SemaphoreSlim(1, 1);



        public Application()
        {
            // Initialize DI container if not already initialized
            if (!IoC.Container.IsLocked)
            {
                IoC.Initialize();
            }
            _applicationScope = AsyncScopedLifestyle.BeginScope(IoC.Container);

            _appSettings = IoC.Container.GetInstance<AppSettings>();
            AppDomain.CurrentDomain.UnhandledException += AppDomain_UnhandledException;
        }

        private void AppDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Console.WriteLine(sender);
            Console.WriteLine(e.ExceptionObject);
            Console.WriteLine(Environment.StackTrace);
        }

        public void Run()
        {
            CancellationTokenSource = new CancellationTokenSource();
            // Configure window
            _window = IoC.Container.GetInstance<IWindow>();
            _window.Title = _appSettings.Name;
            _window.Size = _appSettings.LoadSize;

            // Resolve other dependencies
            _world = IoC.Container.GetInstance<World>();
            _assimpLoader = IoC.Container.GetInstance<AssimpLoader>();
            _window.Load += () => OnWindowLoad().GetAwaiter().GetResult();

            _window.Run();
        }

        private async Task OnWindowLoad()
        {
            try
            {
                Stopwatch loadWatch = Stopwatch.StartNew();
                _inputContext = IoC.Container.GetInstance<IInputContext>();

                // Resolve window-dependent components
                _context = IoC.Container.GetInstance<VulkanContext>();
                _graphicsEngine = IoC.Container.GetInstance<GraphicsEngine>();
                _pipelineManager = IoC.Container.GetInstance<PipelineManager>();
                _renderer = IoC.Container.GetInstance<Renderer>();
                _shaderManager = IoC.Container.GetInstance<IShaderManager>();

                await _shaderManager.CompileAllShadersAsync();

                PerformanceTracer.Initialize(_context);

                _logger.Info($"Core systems initialized in: {loadWatch.ElapsedMilliseconds} ms");
                await _renderer.InitializeAsync().ConfigureAwait(false);
                await _world.Start(_renderer).ConfigureAwait(false);
                _layerStack = IoC.Container.GetInstance<LayerStack>();

                foreach (var item in IoC.Container.GetAllInstances<ILayer>())
                {
                    await _layerStack.PushLayer(item);
                }
                await Load().ConfigureAwait(false);


                _window.Render += Render;
                _window.Update += (s) =>
                {
                    using (PerformanceTracer.BeginSection("Update"))
                    {
                        Update(s).GetAwaiter().GetResult();
                    }
                    PerformanceTracer.ProcessQueries(_context, _graphicsEngine.FrameIndex);
                };

                loadWatch.Stop();
                _logger.Info($"Application loaded in: {loadWatch.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }

        }


        protected virtual Task Load() => Task.CompletedTask;


        protected virtual async Task Update(double deltaTime)
        {
            try
            {
                Time.Update(_window.Time, deltaTime);
                _layerStack.Update();
                await _world.Update(_renderer);
                await _renderer.UpdateFrameData();
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
                throw;
            }
        }

        protected virtual void Render(double time)
        {
            try
            {
                using (PerformanceTracer.BeginSection("Whole Render"))
                {
                    if (_layerStack.Count == 0) return;

                    var batch = _graphicsEngine.Begin();
                    if (batch is null) return;
                   // var gpuPerfSec = PerformanceTracer.BeginSection("Whole GPU Render", batch.CommandBuffer, _graphicsEngine.FrameIndex);

                    using (PerformanceTracer.BeginSection("_layerStack.RenderImGui"))
                    {
                        _layerStack.RenderImGui(batch.CommandBuffer);
                    }

                    using (PerformanceTracer.BeginSection("_layerStack.Render"))
                    {
                        _layerStack.Render(batch.CommandBuffer);
                    }

                    using (PerformanceTracer.BeginSection("_renderer.Render"))
                    {
                        _renderer.Render(batch);
                    }

                    using (PerformanceTracer.BeginSection("_graphicsEngine.end & Submit"))
                    {
                        //gpuPerfSec.Dispose();
                        _graphicsEngine.SubmitAndPresent(batch);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
                throw;
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