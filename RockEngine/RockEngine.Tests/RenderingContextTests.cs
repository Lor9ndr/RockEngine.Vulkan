using Silk.NET.Windowing;
using RockEngine.Vulkan;
using Moq;

namespace RockEngine.Tests
{
    public class RenderingContextTests
    {
        [Test]
        public async Task Constructor_ShouldInitializeProperties()
        {
            // Arrange
            var mockWindow = new Mock<IWindow>();
            string appName = "TestApp";
            int maxFramesPerFlight = 3;

            var t1 = Task.Run(mockWindow.Object.Run);
            mockWindow.Object.Load += async () =>
            {
                // Act
                var context = new RenderingContext(mockWindow.Object, appName, maxFramesPerFlight);

                // Assert
                await Assert.That(context.Instance).IsNotNull();
                await Assert.That(context.Surface).IsNotNull();
                await Assert.That(context.Device).IsNotNull();
                await Assert.That(context.MaxFramesPerFlight).IsEqualTo(maxFramesPerFlight);
                mockWindow.Object.Close();
            };
            await t1;
        }

        [Test]
        public async Task Constructor_ShouldUseDefaultMaxFramesPerFlight_WhenNotSpecified()
        {
            // Arrange
            var mockWindow = new Mock<IWindow>();
            string appName = "TestApp";

            var t1 = Task.Run(mockWindow.Object.Run);
            mockWindow.Object.Load += async () =>
            {
                // Act
                var context = new RenderingContext(mockWindow.Object, appName);

                // Assert
                await Assert.That(context.MaxFramesPerFlight).IsEqualTo(3);
                mockWindow.Object.Close();
            };
            await t1;
        }

        [Test]
        public async Task Dispose_ShouldDisposeAllResources()
        {
            // Arrange
            var mockWindow = new Mock<IWindow>();
            var mockSurface = new Mock<ISurfaceHandler>();
            var mockDevice = new Mock<VkLogicalDevice>();
            var mockInstance = new Mock<VkInstance>();

            var t1 = Task.Run(mockWindow.Object.Run);
            mockWindow.Object.Load +=  () =>
            {
                var context = new RenderingContext(mockWindow.Object, "TestApp");

                // Act
                context.Dispose();

                // Assert
                mockSurface.Verify(s => s.Dispose(), Times.Once);
                mockDevice.Verify(d => d.Dispose(), Times.Once);
                mockInstance.Verify(i => i.Dispose(), Times.Once);
                mockWindow.Object.Close();
            };
            await t1;
        }
    }
}
