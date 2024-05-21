using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkBuilders;
using RockEngine.Vulkan.VkObjects;

using Silk.NET.Core;
using Silk.NET.GLFW;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;

using System.Numerics;
using System.Runtime.InteropServices;


namespace RockEngine.Vulkan
{
    public class Application : IDisposable
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Стили именования", Justification = "<Ожидание>")]
        private const int MAX_FRAMES_IN_FLIGHT = 2;
        private int _currentFrame = 0;

        private readonly Vk _api = Vk.GetApi();
        private IWindow _window;
        private VulkanInstance _vkInstance;
        private VulkanPhysicalDevice _device;
        private VulkanLogicalDevice _logicalDevice;
        private VulkanSurface _surface;
        private VulkanSwapchain _swapchain;
        private VulkanRenderPass _renderPass;
        private VulkanPipelineLayout _pipelineLayout;
        private VulkanPipeline _pipeline;
        private VulkanFramebuffer[] _swapchainFramebuffers;
        private VulkanCommandPool _commandPool;
        private VulkanCommandBuffer[] _commandBuffers;
        private VulkanSemaphore[] _imageAvailableSemaphores;
        private VulkanSemaphore[] _renderFinishedSemaphores;
        private VulkanFence[] _inFlightFences;
        private bool _framebufferResized = false;
        private Vertex[] _triangleVertice = new Vertex[]
        {
            new Vertex(new Vector2(0.0f, -0.5f), new Vector3(1.0f, 1.0f, 1.0f)),
            new Vertex(new Vector2(0.5f, 0.5f), new Vector3(0.0f, 1.0f, 0.0f)),
            new Vertex(new Vector2(-0.5f, 0.5f), new Vector3(1.0f, 0.0f, 1.0f))
        };
        private VulkanBuffer _vertexBuffer;

        private Viewport Viewport => new Viewport() { Width = _window.Size.X, Height = _window.Size.Y, MaxDepth = 1.0f };

        public CancellationTokenSource CancellationTokenSource { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

#if DEBUG
        private const bool _enableValidationLayers = true;
#else
        private const bool _enableValidationLayers = false;
#endif

        private readonly string[] _validationLayers = ["VK_LAYER_KHRONOS_validation"];

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

            _api.WaitForFences(_logicalDevice.Device, 1, in fence, true, ulong.MaxValue);

            uint imageIndex = 0;
            var result = _swapchain.SwapchainApi.AcquireNextImage(
                _logicalDevice.Device,
                _swapchain.Swapchain,
                ulong.MaxValue,
                imageAvailableSemaphore.Semaphore,
                default,
                ref imageIndex)
                    .ThrowCode("failed to acquire swap chain image!", Result.SuboptimalKhr, Result.ErrorOutOfDateKhr);
            if (result == Result.ErrorOutOfDateKhr )
            {
                RecreateSwapChain();
                return;
            }
            
            // Reset fence only if we are submiting work
            _api.ResetFences(_logicalDevice.Device, 1, in fence);

            _api.ResetCommandBuffer(commandBuffer.CommandBuffer, CommandBufferResetFlags.None);
            RecordCommandBuffer(commandBuffer, imageIndex);

            using var pwaitSemaphores = new Memory<Silk.NET.Vulkan.Semaphore>([imageAvailableSemaphore.Semaphore]).Pin();
            using var pSignalSemaphores = new Memory<Silk.NET.Vulkan.Semaphore>([renderFinishedSemaphore.Semaphore]).Pin();

            using var pwaitStages = new Memory<PipelineStageFlags>([PipelineStageFlags.ColorAttachmentOutputBit]).Pin();
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
                    // Warning, here is the buffer may be changed as we pass it as reference, but for now okey
                    PCommandBuffers = &buffer,
                    SignalSemaphoreCount = 1,
                    PSignalSemaphores = (Silk.NET.Vulkan.Semaphore*)pSignalSemaphores.Pointer

                };
                _api.QueueSubmit(_logicalDevice.GraphicsQueue, 1, ref submitInfo, fence)
                    .ThrowCode("Failed to submit draw command buffer!");

                using var pswapchains = new Memory<SwapchainKHR>([_swapchain.Swapchain]).Pin();

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
                result = _swapchain.SwapchainApi.QueuePresent(_logicalDevice.PresentQueue, &presentInfo)
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
                Flags = CommandBufferUsageFlags.None, // Adjust based on your needs
                PInheritanceInfo = (CommandBufferInheritanceInfo*)IntPtr.Zero // Only relevant for secondary command buffers
            };

            _api.BeginCommandBuffer(buffer.CommandBuffer, &beginInfo)
                .ThrowCode("Failed to begin recording command buffer!");

            ClearValue cv = new ClearValue(color: new ClearColorValue() { Float32_0 = 0, Float32_1 = 0, Float32_2 = 0, Float32_3 = 1 });
            var renderPassInfo = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = _renderPass.RenderPass, // Your VulkanRenderPass object
                Framebuffer = _swapchainFramebuffers[imageIndex].Framebuffer, // Array or collection of VulkanFramebuffer objects
                RenderArea = new Rect2D { Offset = new Offset2D(0, 0), Extent = _swapchain.Extent },
                ClearValueCount = 1,
                PClearValues = &cv // Define clearColor as in the example
            };
            var viewport = Viewport;
            var scissor = new Rect2D() { Extent = new Extent2D((uint?)_window.Size.X, (uint?)_window.Size.Y), };
            _api.CmdSetViewport(buffer.CommandBuffer, 0, 1, ref viewport);
            _api.CmdSetScissor(buffer.CommandBuffer, 0, 1, ref scissor);

            _api.CmdBeginRenderPass(buffer.CommandBuffer, &renderPassInfo, SubpassContents.Inline);
            _api.CmdBindPipeline(buffer.CommandBuffer, PipelineBindPoint.Graphics, _pipeline.Pipeline);
            var vertexBuffer = _vertexBuffer.Buffer;
            ulong offset = 0;
            _api.CmdBindVertexBuffers(buffer.CommandBuffer, 0, 1, in vertexBuffer, in offset);
            _api.CmdDraw(buffer.CommandBuffer, vertexCount: (uint)_triangleVertice.Length, instanceCount: 1, firstVertex: 0, firstInstance: 0);
            _api.CmdEndRenderPass(buffer.CommandBuffer);
            _api.EndCommandBuffer(buffer.CommandBuffer)
                .ThrowCode("Failed to record command buffer!");
        }


        private async Task Window_Load()
        {
            CreateInstance();
            CreateSurface();
            CreateDevice();
            CreateSwapChain();
            CreateRenderPass();
            await CreateGraphicsPipeline(CancellationToken)
                .ConfigureAwait(false);
            CreateFramebuffers();
            CreateCommandPool();
            CreateVertexBuffer();

            CreateSyncObject();

            _window.Render += DrawFrame;
        }

        private void CreateVertexBuffer()
        {
            ulong vertexBufferSize = (ulong)(Vertex.Size * _triangleVertice.Length);

            var stagingBuffer = new VulkanBufferBuilder(_api, _logicalDevice)
                .Configure(SharingMode.Exclusive,
                vertexBufferSize,
                BufferUsageFlags.TransferSrcBit | BufferUsageFlags.VertexBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
                .Build();

            stagingBuffer.SendData(_triangleVertice);

            _vertexBuffer = new VulkanBufferBuilder(_api, _logicalDevice)
                .Configure(SharingMode.Exclusive, vertexBufferSize
                    , 
                    BufferUsageFlags.TransferDstBit | BufferUsageFlags.VertexBufferBit,
                    MemoryPropertyFlags.DeviceLocalBit)
                        .Build();
            CopyBuffer(stagingBuffer, _vertexBuffer, vertexBufferSize);
            stagingBuffer.Dispose();
            
        }
        private void CopyBuffer(VulkanBuffer src, VulkanBuffer dst, ulong size)
        {
            var commandBuffer = new VulkanCommandBufferBuilder(_api, _logicalDevice, _commandPool)
                .WithLevel(CommandBufferLevel.Primary)
                .Build();
            var commandBufferBeginInfo = new CommandBufferBeginInfo()
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit
            };
            _api.BeginCommandBuffer(commandBuffer.CommandBuffer,ref commandBufferBeginInfo);

            BufferCopy bufferCopy = new BufferCopy()
            {
                SrcOffset = 0,
                DstOffset = 0,
                Size = size
            };
            _api.CmdCopyBuffer(commandBuffer.CommandBuffer, src.Buffer, dst.Buffer, 1, ref bufferCopy);

            _api.EndCommandBuffer(commandBuffer.CommandBuffer);
            unsafe
            {
                var buffer = commandBuffer.CommandBuffer;
                SubmitInfo submitInfo = new SubmitInfo()
                {
                    SType = StructureType.SubmitInfo,
                    CommandBufferCount = 1,
                    PCommandBuffers = &buffer
                };

                _api.QueueSubmit(_logicalDevice.GraphicsQueue,1 , ref submitInfo, default);
                _api.QueueWaitIdle(_logicalDevice.GraphicsQueue);
                _api.FreeCommandBuffers(_logicalDevice.Device, _commandPool.CommandPool, 1, ref buffer);
            }
        }

        private void CreateSyncObject()
        {

            VulkanSemaphoreBuilder semaphoreBuilder = new VulkanSemaphoreBuilder(_api, _logicalDevice);
            VulkanFenceBuilder fenceBuilder = new VulkanFenceBuilder(_api, _logicalDevice)
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
            using var cpBuilder = new VulkanCommandPoolBuilder(_api, _logicalDevice);
            _commandPool = cpBuilder
                .WithFlags(CommandPoolCreateFlags.ResetCommandBufferBit)
                .WithQueueFamilyIndex(_logicalDevice.QueueFamilyIndices.GraphicsFamily.Value)
                .Build();

            using var cbBuilder = new VulkanCommandBufferBuilder(_api, _logicalDevice, _commandPool)
                .WithLevel(CommandBufferLevel.Primary);

            _commandBuffers = cbBuilder.Build(MAX_FRAMES_IN_FLIGHT);
        }


        private unsafe void CreateRenderPass()
        {
            VulkanRenderPassBuilder builder = new VulkanRenderPassBuilder(_api, _logicalDevice);
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
                using var builder = new VulkanFramebufferBuilder(_api, _logicalDevice)
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
            var shaderModuleBuilder = new VulkanShaderModuleBuilder(_api, _logicalDevice);

            // Load the vertex shader
            using var vertexShaderModule = await shaderModuleBuilder.Build("..\\..\\..\\Shaders\\Shader.vert.spv", token)
                .ConfigureAwait(false);
            // Load the fragment shader
            using var fragmentShaderModule = await shaderModuleBuilder.Build("..\\..\\..\\Shaders\\Shader.frag.spv", token)
                .ConfigureAwait(false);
            _pipelineLayout = new VulkanPipelineLayoutCreateBuilder(_api, _logicalDevice)
                             .Build();
            var colorBlendAttachment = new VulkanColorBlendAttachmentBuilder()
                  .Build(false,
                  ColorComponentFlags.RBit |
                      ColorComponentFlags.GBit |
                      ColorComponentFlags.BBit |
                      ColorComponentFlags.ABit);

            using GraphicsPipelineBuilder pBuilder = new GraphicsPipelineBuilder(_api, _logicalDevice, _renderPass)
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

            _pipeline = pBuilder.Build();
        }

        private void CreateSwapChain()
        {
            _swapchain = new VulkanSwapChainBuilder(_api, _logicalDevice, _device, _surface)
                .Build();
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

            _api.DeviceWaitIdle(_logicalDevice.Device);
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

        private void CreateSurface()
        {
            _surface = new VulkanSurfaceBuilder(_vkInstance, _api, _window)
                .Build();
        }

        private void CreateDevice()
        {
            _device = new VulkanPhysicalDeviceBuilder(_api, _vkInstance.Instance)
                .Build();
            _logicalDevice = new VulkanLogicalDeviceBuilder(_api, _device, _surface)
                .WithExtensions(KhrSwapchain.ExtensionName)
                .Build();
        }

        private unsafe void CreateInstance()
        {
            var name = "RockEngine\0"; // Ensure null-termination
            var appname = (byte*)Marshal.StringToHGlobalAnsi(name);
            var appInfo = new ApplicationInfo()
            {
                ApiVersion = Vk.Version13,
                ApplicationVersion = Vk.MakeVersion(1, 0, 0),
                EngineVersion = Vk.MakeVersion(1, 0, 0),
                PApplicationName = appname,
                PEngineName = appname,
                SType = StructureType.ApplicationInfo,
            };

            var ci = new InstanceCreateInfo()
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,
            };


            IntPtr callbackPtr = Marshal.GetFunctionPointerForDelegate(DebugCallback);


            PfnDebugUtilsMessengerCallbackEXT dbcallback = new PfnDebugUtilsMessengerCallbackEXT(
                (delegate* unmanaged[Cdecl]<DebugUtilsMessageSeverityFlagsEXT, DebugUtilsMessageTypeFlagsEXT, DebugUtilsMessengerCallbackDataEXT*, void*, Bool32>)callbackPtr);

            var extensions = _window.VkSurface.GetRequiredExtensions(out uint countExtensions);
            ci.PpEnabledExtensionNames = extensions;
            ci.EnabledExtensionCount = countExtensions;

            _vkInstance = new VulkanInstanceBuilder(_api)
                .UseValidationLayers(_validationLayers)
                .UseDebugUtilsMessenger(DebugUtilsMessageSeverityFlagsEXT.WarningBitExt | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
                                        DebugUtilsMessageTypeFlagsEXT.GeneralBitExt | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt,
                                        dbcallback, (void*)nint.Zero)
                .Build(ref ci);

            Marshal.FreeHGlobal((nint)appname);
        }

        unsafe Bool32 DebugCallback(DebugUtilsMessageSeverityFlagsEXT severity,
                DebugUtilsMessageTypeFlagsEXT messageType,
                DebugUtilsMessengerCallbackDataEXT* callbackData,
                void* userData)
        {
            var message = Marshal.PtrToStringUTF8((nint)callbackData->PMessage);

            // Change console color based on severity
            switch (severity)
            {
                case DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt:
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
                case DebugUtilsMessageSeverityFlagsEXT.WarningBitExt:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;
                case DebugUtilsMessageSeverityFlagsEXT.InfoBitExt:
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    break;
                case DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    break;
                default:
                    Console.ResetColor();
                    break;
            }

            Console.WriteLine($"{severity} ||| {message}");

            // Reset console color to default
            Console.ResetColor();

            // Throw an exception if severity is ErrorBitEXT
            if (severity == DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt)
            {
                throw new Exception($"Vulkan Error: {message}");
            }

            return new Bool32(true);
        }

        public void Dispose()
        {
            _api.DeviceWaitIdle(_logicalDevice.Device);
            _commandPool.Dispose();

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
            _vertexBuffer.Dispose();
            _pipeline.Dispose();
            _pipelineLayout.Dispose();
            _renderPass.Dispose();

            _device.Dispose();
            _logicalDevice.Dispose();
            _surface.Dispose();
            _vkInstance.Dispose();
            CancellationTokenSource.Cancel();
        }
    }
}
