using RockEngine.Core;
using RockEngine.Core.Rendering;
using RockEngine.Vulkan;

using Silk.NET.Windowing;

namespace RockEngine.Tests
{
    public class ApplicationTests
    {
        private class TestLayer : ILayer
        {
            public bool IsAttached;
            public bool IsUpdated;
            public bool IsRendered;
            public bool IsImguiRendered;

            public Task OnAttach()
            {
                IsAttached = true;
                return Task.CompletedTask;
            }

            public void OnDetach() => IsAttached = false;
            public void OnUpdate() => IsUpdated = true;
            public void OnRender(VkCommandBuffer cmdBuffer) => IsRendered = true;
            public void OnImGuiRender(VkCommandBuffer cmdBuffer) => IsImguiRendered = true;
        }

        private class TestApplication : Application
        {
            public bool UpdateCalled;
            public ManualResetEventSlim LoadedEvent = new();

            public VulkanContext RenderingContext => _context;
            public IWindow Window => _window;

            public TestApplication() : base("TEST", 800, 600)
            {
                OnLoad += CompleteLoad;
            }

            private async Task CompleteLoad()
            {
                LoadedEvent.Set();
            }

            public void InvokeUpdate(double deltaTime) => Update(deltaTime).GetAwaiter().GetResult();
            public void InvokeRender(double time) => Render(time);
        }

        private TestApplication _application;
        private CancellationTokenSource _cts;

        [Before(Test)]
        public async Task SetUp()
        {
            _cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            _application = new TestApplication();

            // Запускаем приложение в фоне
            var runTask = _application.Run();

            // Ждем инициализации с таймаутом
            if (!_application.LoadedEvent.Wait(TimeSpan.FromSeconds(200), _cts.Token))
            {
                throw new TimeoutException("Application failed to initialize");
            }
        }

        [After(Test)]
        public async Task TearDown()
        {
            _cts.Cancel();
            _application.Dispose();
            await Task.Delay(100); // Даем время на очистку
        }

        [Test]
        public async Task Application_InitializesCoreComponents()
        {
            await Assert.That(_application.Window).IsNotNull();
            await Assert.That(_application.RenderingContext).IsNotNull();
        }

        [Test]
        public async Task LayerStack_AddRemoveLayers()
        {
            var layer = new TestLayer();
            await _application.PushLayer(layer);

            await Assert.That(layer.IsAttached).IsTrue();

            _application.PopLayer(layer);
            await Assert.That(layer.IsAttached).IsFalse();
        }

        [Test]
        public async Task Update_ProcessesFrame()
        {
            var initialTime = Time.TotalTime;
            _application.InvokeUpdate(0.016);

            await Assert.That(Time.TotalTime).IsGreaterThan(initialTime);
        }
    }
}