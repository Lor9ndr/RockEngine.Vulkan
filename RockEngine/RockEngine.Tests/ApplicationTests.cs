using Moq;
using RockEngine.Core;
using RockEngine.Core.ECS;
using RockEngine.Core.Rendering;
using RockEngine.Vulkan;
using Silk.NET.Input;
using Silk.NET.Windowing;
using Silk.NET.Maths;
using RockEngine.Editor;

namespace RockEngine.Tests
{
    public class ApplicationTests 
    {
        private class TestLayer : ILayer
        {
            public bool IsAttached;
            public Task OnAttach()
            {
                IsAttached = true;
                return Task.CompletedTask;
            }

            public void OnDetach()
            {
                IsAttached = false;
            }

            public void OnImGuiRender(VkCommandBuffer vkCommandBuffer)
            {
            }

            public void OnRender(VkCommandBuffer vkCommandBuffer)
            {
            }

            public void OnUpdate()
            {
            }
        }
        private class TestApplication : EditorApplication
        {
            public TestApplication(int width, int height) : base(width, height)
            {
                OnLoad += TestApplication_OnLoad;
            }

            private Task TestApplication_OnLoad()
            {
                Loaded = true;
                return Task.CompletedTask;
            }

            public RenderingContext RenderingContext => _renderingContext;
            public IWindow Window => _window;
            public LayerStack LayerStack => _layerStack;
            public GraphicsEngine GraphicsEngine => _graphicsEngine;
            public IInputContext InputContext => _inputContext;
            public World World => _world;

            public bool Loaded { get; private set; }

            public void InvokeUpdate(double deltaTime) => Update(deltaTime);
            public void InvokeRender(double time) => Render(time);

            public override void Dispose()
            {
                base.Dispose();
                Loaded = false;
            }
        }

            
        private static TestApplication _application = new TestApplication(Width, Height);
        private const int Width = 800;
        private const int Height = 600;
        private static Task _applicationTask;


        [Before( Class)]
        public static async Task SetUpApplication()
        {
            _applicationTask =  _application.Run();

            // Wait for the application to load
            while (!_application.Loaded)
            {
                await Task.Delay(10);
            }
        }

        [After(Class)]
        public static async Task TearDownApplication()
        {
            if (_application != null)
            {
                _application.Dispose();
                if (_applicationTask != null)
                {
                    await _applicationTask;
                    _applicationTask.Dispose();
                }
                _application = null;
                _applicationTask = null;
            }

        }

        [Test, Retry(0), Timeout(30_000), NotInParallel]

        public async Task Constructor_InitializesProperties(CancellationToken token)
        {
            await Assert.That(_application.Window).IsNotNull();
            await Assert.That(_application.Window.Size).IsEqualTo(new Vector2D<int>(Width, Height));
            await Assert.That(_application.LayerStack).IsNotNull();
        }

        [Test, Retry(0), DependsOn(nameof(Constructor_InitializesProperties))]

        public async Task OnLoad_InitializesComponents()
        {
            await Assert.That(_application.RenderingContext).IsNotNull();
            await Assert.That(_application.GraphicsEngine).IsNotNull();
            await Assert.That(_application.InputContext).IsNotNull();
            await Assert.That(_application.World).IsNotNull();
        }


        [Test, Retry(0), DependsOn(nameof(Constructor_InitializesProperties))]

        public async Task Update_UpdatesTimeAndLayerStack()
        {
            await Assert.That(Time.TotalTime).IsNotEqualTo(0);
        }

       

        [Test, Retry(0), DependsOn(nameof(Constructor_InitializesProperties))]
        public async Task PopLayer_RemovesLayerFromLayerStack()
        {
            var testLayer = new TestLayer();
            await _application.PushLayer(testLayer);

            await Assert.That(testLayer.IsAttached).IsTrue();
            _application.PopLayer(testLayer);

            await Assert.That(testLayer.IsAttached).IsFalse();
        }
    }
}
