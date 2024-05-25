using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkBuilders;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Core;
using Silk.NET.GLFW;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;

using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace RockEngine.Vulkan
{
    public class Application : IDisposable
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Стили именования", Justification = "<Ожидание>")]
        private const int MAX_FRAMES_IN_FLIGHT = 2;
        private int _currentFrame = 0;

        private readonly Vk _api = Vk.GetApi();
        private IWindow _window;
        private VulkanSwapchain _swapchain;
        private VulkanRenderPass _renderPass;
        private VulkanPipelineLayout _pipelineLayout;
        private VulkanPipeline _pipeline;
        private VulkanFramebuffer[] _swapchainFramebuffers;
        private VulkanCommandBuffer[] _commandBuffers;
        private VulkanSemaphore[] _imageAvailableSemaphores;
        private VulkanSemaphore[] _renderFinishedSemaphores;
        private VulkanFence[] _inFlightFences;
        private bool _framebufferResized = false;
        private Vertex[] _triangleVertice = new Vertex[]
        {
            new Vertex(new Vector2(0.0f, -0.5f), new Vector3(1.0f, 1.0f, 1.0f)),
            new Vertex(new Vector2(0.5f, 0.5f), new Vector3(0.0f, 1.0f, 1.0f)),
            new Vertex(new Vector2(-0.5f, 0.5f), new Vector3(1.0f, 0.0f, 1.0f))
        };
        private VulkanContext _context;
        private RenderableObject _renderableObject;

        private Viewport Viewport => new Viewport() { Width = _window.Size.X, Height = _window.Size.Y, MaxDepth = 1.0f };

        public CancellationTokenSource CancellationTokenSource { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

#if DEBUG
        private const bool _enableValidationLayers = true;
#else
        private const bool _enableValidationLayers = false;
#endif

        private readonly string[] _validationLayers = { "VK_LAYER_KHRONOS_validation" };

        public Application()
        {
            CancellationTokenSource = new CancellationTokenSource();
            CancellationToken = CancellationTokenSource.Token;
        }

        public async Task RunAsync()
        {
            _window = Window.Create(WindowOptions.DefaultVulkan);
            _window.Title = "RockEngine";
            _window.Load += async () =>
            {
                try
                {
                    await Window_Load().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    throw;
                }
            };
            _window.Closing += Dispose;
            _window.Resize += WindowResized;

            try
            {
                // Assuming _window.Run() must be run on a background thread and cannot be awaited
                await Task.Run(() => _window.Run(), CancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation
            }
            catch (Exception)
            {
                throw;
            }
        }

        private void WindowResized(Silk.NET.Maths.Vector2D<int> obj)
        {
            _framebufferResized = true;
        }

        private void DrawFrame(double obj)
        {
            int width = _window.Size.X, height = _window.Size.Y;
            if (width == 0 || height == 0)
            {
                return; // Skip rendering if the window is minimized
            }
            var fence = _inFlightFences[_currentFrame].Fence;
            var commandBuffer = _commandBuffers[_currentFrame];
            var imageAvailableSemaphore = _imageAvailableSemaphores[_currentFrame];
            var renderFinishedSemaphore = _renderFinishedSemaphores[_currentFrame];

            _api.WaitForFences(_context.Device.Device, 1, in fence, true, ulong.MaxValue);

            uint imageIndex = 0;
            var result = _swapchain.SwapchainApi.AcquireNextImage(
                _context.Device.Device,
                _swapchain.Swapchain,
                ulong.MaxValue,
                imageAvailableSemaphore.Semaphore,
                default,
                ref imageIndex)
                    .ThrowCode("failed to acquire swap chain image!", Result.SuboptimalKhr, Result.ErrorOutOfDateKhr);
            if (result == Result.ErrorOutOfDateKhr)
            {
                RecreateSwapChain();
                return;
            }

            // Reset fence only if we are submitting work
            _api.ResetFences(_context.Device.Device, 1, in fence);

            _api.ResetCommandBuffer(commandBuffer.CommandBuffer, CommandBufferResetFlags.None);
            RecordCommandBuffer(commandBuffer, imageIndex);

            using var pwaitSemaphores = new Memory<Silk.NET.Vulkan.Semaphore>(new[] { imageAvailableSemaphore.Semaphore }).Pin();
            using var pSignalSemaphores = new Memory<Silk.NET.Vulkan.Semaphore>(new[] { renderFinishedSemaphore.Semaphore }).Pin();

            using var pwaitStages = new Memory<PipelineStageFlags>(new[] { PipelineStageFlags.ColorAttachmentOutputBit }).Pin();
            var buffer = commandBuffer.CommandBuffer;
            unsafe
            {
                SubmitInfo submitInfo = new SubmitInfo()
                {
                    SType = StructureType.SubmitInfo,
                    PWaitSemaphores = (Silk.NET.Vulkan.Semaphore*)pwaitSemaphores.Pointer,
                    WaitSemaphoreCount = 1,
                    PWaitDstStageMask = (PipelineStageFlags*)pwaitStages.Pointer,
                    CommandBufferCount = 1,
                    PCommandBuffers = &buffer,
                    SignalSemaphoreCount = 1,
                    PSignalSemaphores = (Silk.NET.Vulkan.Semaphore*)pSignalSemaphores.Pointer
                };
                _api.QueueSubmit(_context.Device.GraphicsQueue, 1, ref submitInfo, fence)
                    .ThrowCode("Failed to submit draw command buffer!");

                using var pswapchains = new Memory<SwapchainKHR>(new[] { _swapchain.Swapchain }).Pin();

                PresentInfoKHR presentInfo = new PresentInfoKHR()
                {
                    SType = StructureType.PresentInfoKhr,
                    WaitSemaphoreCount = 1,
                    PWaitSemaphores = (Silk.NET.Vulkan.Semaphore*)pSignalSemaphores.Pointer,
                    SwapchainCount = 1,
                    PSwapchains = (SwapchainKHR*)pswapchains.Pointer,
                    PImageIndices = &imageIndex,
                    PResults = null
                };
                result = _swapchain.SwapchainApi.QueuePresent(_context.Device.PresentQueue, &presentInfo)
                    .ThrowCode("Failed to queue present", Result.SuboptimalKhr, Result.ErrorOutOfDateKhr);
            }

            if (result == Result.ErrorOutOfDateKhr || result == Result.SuboptimalKhr || _framebufferResized)
            {
                _framebufferResized = false;
                RecreateSwapChain();
            }

            _currentFrame = (_currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;
        }

        private unsafe void RecordCommandBuffer(VulkanCommandBuffer buffer, uint imageIndex)
        {
            var beginInfo = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.None,
                PInheritanceInfo = default // Only relevant for secondary command buffers
            };

            _api.BeginCommandBuffer(buffer.CommandBuffer, &beginInfo)
                .ThrowCode("Failed to begin recording command buffer!");

            ClearValue cv = new ClearValue(color: new ClearColorValue() { Float32_0 = 0, Float32_1 = 0, Float32_2 = 0, Float32_3 = 1 });
            var renderPassInfo = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = _renderPass.RenderPass,
                Framebuffer = _swapchainFramebuffers[imageIndex].Framebuffer,
                RenderArea = new Rect2D { Offset = new Offset2D(0, 0), Extent = _swapchain.Extent },
                ClearValueCount = 1,
                PClearValues = &cv
            };
            var viewport = Viewport;
            var scissor = new Rect2D() { Extent = new Extent2D((uint)_window.Size.X, (uint)_window.Size.Y), };
            _api.CmdSetViewport(buffer.CommandBuffer, 0, 1, ref viewport);
            _api.CmdSetScissor(buffer.CommandBuffer, 0, 1, ref scissor);
            // Start of renderpass
            _api.CmdBeginRenderPass(buffer.CommandBuffer, &renderPassInfo, SubpassContents.Inline);
            _api.CmdBindPipeline(buffer.CommandBuffer, PipelineBindPoint.Graphics, _pipeline.Pipeline);
            _renderableObject.Draw(_context, buffer);

            // ending of renderpass
            _api.CmdEndRenderPass(buffer.CommandBuffer);
            _api.EndCommandBuffer(buffer.CommandBuffer)
                .ThrowCode("Failed to record command buffer!");
        }

        private async Task Window_Load()
        {
            _context = new VulkanContext(_window, "Lor9ndr");

            CreateSwapChain();
            CreateRenderPass();
            await CreateGraphicsPipeline(CancellationToken)
                .ConfigureAwait(false);
            CreateFramebuffers();
            CreateCommandPool();
            await CreateVertexBuffer();

            CreateSyncObject();

            _window.Render += DrawFrame;
        }

        private async Task CreateVertexBuffer()
        {
            _renderableObject = new RenderableObject(_triangleVertice);
            await _renderableObject.CreateBuffersAsync(_context);
        }


        private void CreateSyncObject()
        {
            VulkanSemaphoreBuilder semaphoreBuilder = new VulkanSemaphoreBuilder(_api, _context.Device);
            VulkanFenceBuilder fenceBuilder = new VulkanFenceBuilder(_api, _context.Device)
               .WithFlags(FenceCreateFlags.SignaledBit);

            _imageAvailableSemaphores = new VulkanSemaphore[MAX_FRAMES_IN_FLIGHT];
            _renderFinishedSemaphores = new VulkanSemaphore[MAX_FRAMES_IN_FLIGHT];
            _inFlightFences = new VulkanFence[MAX_FRAMES_IN_FLIGHT];

            for (int i = 0; i < MAX_FRAMES_IN_FLIGHT; i++)
            {
                _imageAvailableSemaphores[i] = semaphoreBuilder.Build();
                _renderFinishedSemaphores[i] = semaphoreBuilder.Build();
                _inFlightFences[i] = fenceBuilder.Build();
            }
        }

        private void CreateCommandPool()
        {
            using var cbBuilder = new VulkanCommandBufferBuilder(_context)
                .WithLevel(CommandBufferLevel.Primary);

            _commandBuffers = cbBuilder.Build(MAX_FRAMES_IN_FLIGHT);
        }

        private unsafe void CreateRenderPass()
        {
            VulkanRenderPassBuilder builder = new VulkanRenderPassBuilder(_context);
            AttachmentDescription attachment = new AttachmentDescription()
            {
                Format = _swapchain.Format,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.PresentSrcKhr
            };

            AttachmentReference attachmentReference = new AttachmentReference()
            {
                Attachment = 0,
                Layout = ImageLayout.ColorAttachmentOptimal
            };

            SubpassDescription description = new SubpassDescription()
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = &attachmentReference
            };

            _renderPass = builder
                 .AddSubpass(description)
                 .AddAttachment(attachment)
                 .AddDependency(new SubpassDependency()
                 {
                     SrcSubpass = Vk.SubpassExternal,
                     DstSubpass = 0,
                     SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                     SrcAccessMask = AccessFlags.None,
                     DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                     DstAccessMask = AccessFlags.ColorAttachmentWriteBit,
                 })
                 .Build();
        }

        private void CreateFramebuffers()
        {
            _swapchainFramebuffers = new VulkanFramebuffer[_swapchain.SwapChainImageViews.Length];
            for (int i = 0; i < _swapchain.Images.Length; i++)
            {
                using var builder = new VulkanFramebufferBuilder(_api, _context.Device)
                    .AddAttachment(_swapchain.SwapChainImageViews[i])
                    .WithRenderPass(_renderPass)
                    .WithWidth(_swapchain.Extent.Width)
                    .WithHeight(_swapchain.Extent.Height)
                    .WithLayersCount(1);

                _swapchainFramebuffers[i] = builder.Build();
            }
        }

        private async Task CreateGraphicsPipeline(CancellationToken token = default)
        {
            // Initialize the shader module builder
            var shaderModuleBuilder = new VulkanShaderModuleBuilder(_api, _context.Device);

            // Load the vertex shader
            var vertexShaderModuleTask =  shaderModuleBuilder.Build("..\\..\\..\\Shaders\\Shader.vert.spv", token)
                .ConfigureAwait(false);
            // Load the fragment shader
            var fragmentShaderModuleTask = shaderModuleBuilder.Build("..\\..\\..\\Shaders\\Shader.frag.spv", token)
                .ConfigureAwait(false);
            _pipelineLayout = new VulkanPipelineLayoutCreateBuilder()
                             .Build(_context);
            var colorBlendAttachment = new VulkanColorBlendAttachmentBuilder()
                  .Build(false,
                  ColorComponentFlags.RBit |
                      ColorComponentFlags.GBit |
                      ColorComponentFlags.BBit |
                      ColorComponentFlags.ABit);
           using var vertexShaderModule = await vertexShaderModuleTask;
           using var fragmentShaderModule = await fragmentShaderModuleTask;
            using GraphicsPipelineBuilder pBuilder = new GraphicsPipelineBuilder(_renderPass)
                .WithPipelineLayout(_pipelineLayout)

                .WithDynamicState(new VulkanDynamicStateBuilder()
                    .AddState(DynamicState.Viewport)
                    .AddState(DynamicState.Scissor))

                .WithMultisampleState(new VulkanMultisampleStateInfoBuilder()
                    .Configure(false, SampleCountFlags.Count1Bit))

                .WithRasterizer(new VulkanRasterizerBuilder())

                .WithColorBlendState(new VulkanColorBlendStateBuilder()
                    .Configure(LogicOp.Copy)
                    .AddAttachment(colorBlendAttachment))

                .WithInputAssembly(new VulkanInputAssemblyBuilder().Configure())

                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                    .Add(Vertex.GetBindingDescription(), Vertex.GetAttributeDescriptions()))

                .WithShaderModule(ShaderStageFlags.VertexBit, vertexShaderModule)
                .WithShaderModule(ShaderStageFlags.FragmentBit, fragmentShaderModule)

                .WithViewportState(new VulkanViewportStateInfoBuilder()
                    .AddViewport(new Viewport())
                    .AddScissors(new Rect2D()));

            _pipeline = pBuilder.Build(_context);
        }

        private void CreateSwapChain()
        {
            _swapchain = new VulkanSwapChainBuilder()
                .Build(_context);
            _swapchain.CreateImageViews();
        }

        private void RecreateSwapChain()
        {
            var glfwApi = Glfw.GetApi();
            int width = _window.Size.X, height = _window.Size.Y;
            unsafe
            {
                while (width == 0 || height == 0)
                {
                    glfwApi.GetFramebufferSize((WindowHandle*)_window.Handle, out width, out height);
                    glfwApi.WaitEvents();
                }
            }

            _api.DeviceWaitIdle(_context.Device.Device);
            CleanupSwapChain();
            CreateSwapChain();
            CreateFramebuffers();
        }

        private void CleanupSwapChain()
        {
            for (int i = 0; i < _swapchainFramebuffers.Length; i++)
            {
                _swapchainFramebuffers[i].Dispose();
            }
            _swapchain.Dispose();
        }

        public void Dispose()
        {
            CancellationTokenSource.Cancel();

            _api.DeviceWaitIdle(_context.Device.Device);

            CleanupSwapChain();

            foreach (var semaphore in _imageAvailableSemaphores)
            {
                semaphore.Dispose();
            }
            foreach (var semaphore in _renderFinishedSemaphores)
            {
                semaphore.Dispose();
            }
            foreach (var fence in _inFlightFences)
            {
                fence.Dispose();
            }
            _pipeline.Dispose();
            _pipelineLayout.Dispose();
            _renderPass.Dispose();
            _renderableObject.Dispose();
            _context.Dispose();
        }
    }
}