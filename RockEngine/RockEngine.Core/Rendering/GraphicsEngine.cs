using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering
{
    public class GraphicsEngine : IDisposable
    {
        private readonly VulkanContext _renderingContext;
        private readonly VkCommandPool _commandBufferPool;
        private readonly VkSwapchain _swapchain;
        private readonly RenderPassManager _renderPassManager;
        private uint _currentImageIndex;

        public VkSwapchain Swapchain => _swapchain;

        public VkCommandPool CommandBufferPool => _commandBufferPool;

        public RenderPassManager RenderPassManager => _renderPassManager;

        public uint CurrentImageIndex { get => _currentImageIndex; private set => _currentImageIndex = value; }

        private readonly VkCommandBuffer[] _renderCommandBuffers;
        public GraphicsEngine(VulkanContext renderingContext)
        {
            _renderingContext = renderingContext;
            var commandPoolCreateInfo = new CommandPoolCreateInfo()
            {
                SType = StructureType.CommandPoolCreateInfo,
                Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
                QueueFamilyIndex = _renderingContext.Device.QueueFamilyIndices.GraphicsFamily.Value
            };
            _commandBufferPool = VkCommandPool.Create(_renderingContext, in commandPoolCreateInfo);
            _renderCommandBuffers = _commandBufferPool.AllocateCommandBuffers((uint)_renderingContext.MaxFramesPerFlight);
            _swapchain = VkSwapchain.Create(_renderingContext, _renderingContext.Surface);
            _renderPassManager = new RenderPassManager(_renderingContext);
        }

        private VkCommandBuffer GetCurrentCommandBuffer()
        {
            return _renderCommandBuffers[_swapchain.CurrentFrameIndex];
        }

        public VkCommandBuffer? Begin()
        {
            if (_swapchain.Surface.Size.X == 0 || _swapchain.Surface.Size.Y == 0)
            {
                return null;
            }

            var result = _swapchain.AcquireNextImage(ref _currentImageIndex)
               .VkAssertResult("Failed to acquire swap chain image!", Result.SuboptimalKhr, Result.ErrorOutOfDateKhr);

            if (result == Result.ErrorOutOfDateKhr)
            {
                RecreateSwapchain();
                return null;
            }

            var commandBuffer = GetCurrentCommandBuffer();

            var beginInfo = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.None,
                PInheritanceInfo = default
            };
            commandBuffer.Reset(CommandBufferResetFlags.None);
            commandBuffer.Begin(in beginInfo);

            return commandBuffer;
        }

        public void Submit(params CommandBuffer[] commandBuffers)
        {
            _swapchain.SubmitCommandBuffers(commandBuffers, _currentImageIndex);
        }

        public void End(VkCommandBuffer commandBuffer)
        {
            commandBuffer.End();
        }

        private void RecreateSwapchain()
        {
            _swapchain.RecreateSwapchain();
        }


        public void Dispose()
        {
            _commandBufferPool.Dispose();
            _swapchain.Dispose();
        }
    }
}