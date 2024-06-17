using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkBuilders;
using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.Rendering
{
    internal class BaseRenderer:IDisposable
    {
        public VulkanContext Context;
        private readonly ISurfaceHandler _surfaceHandler;
        private SwapchainWrapper _swapchain;
        private RenderPassWrapper _renderPass;
        private PipelineLayoutWrapper _pipelineLayout;
        private PipelineWrapper _pipeline;
        private FramebufferWrapper[] _swapchainFramebuffers;
        private CommandBufferWrapper[] _commandBuffers;
        private SemaphoreWrapper[] _imageAvailableSemaphores;
        private SemaphoreWrapper[] _renderFinishedSemaphores;
        private FenceWrapper[] _inFlightFences;
        private DescriptorPoolWrapper _descriptorPool;

        public BaseRenderer(VulkanContext context, ISurfaceHandler surfaceHandler)
        {
            Context = context;
            _surfaceHandler = surfaceHandler;
        }

        public async Task InitializeAsync()
        {
            CreateSwapChain(_surfaceHandler);
            CreateRenderPass();
            await CreateGraphicsPipeline().ConfigureAwait(false);
            CreateFramebuffers();
            CreateCommandPool();
            CreateSyncObject();
        }

        public async void Render(double obj, Project project)
        {
            float width = Context.Surface.Size.X, height = Context.Surface.Size.Y;
            if (width == 0 || height == 0)
            {
                return; // Skip rendering if the window is minimized
            }

            var fence = _inFlightFences[Context.CurrentFrame].VkObjectNative;
            var commandBuffer = _commandBuffers[Context.CurrentFrame];
            var imageAvailableSemaphore = _imageAvailableSemaphores[Context.CurrentFrame];
            var renderFinishedSemaphore = _renderFinishedSemaphores[Context.CurrentFrame];

            Context.Api.WaitForFences(Context.Device, 1, in fence, true, ulong.MaxValue);

            uint imageIndex = 0;
            var result = _swapchain.SwapchainApi.AcquireNextImage(
                Context.Device,
                _swapchain.Swapchain,
                ulong.MaxValue,
                imageAvailableSemaphore,
                default,
                ref imageIndex)
                    .ThrowCode("failed to acquire swap chain image!", Result.SuboptimalKhr, Result.ErrorOutOfDateKhr);
            if (result == Result.ErrorOutOfDateKhr)
            {
                RecreateSwapChain();
                return;
            }

            // Reset fence only if we are submitting work
            Context.Api.ResetFences(Context.Device, 1, in fence);
            Context.QueueMutex.WaitOne();
            try
            {
                Context.Api.ResetCommandBuffer(commandBuffer, CommandBufferResetFlags.None);
                await RecordCommandBuffer(commandBuffer, imageIndex, project).ConfigureAwait(false);

            }
            finally
            {
                Context.QueueMutex.ReleaseMutex();
            }

            Wait(fence, commandBuffer, imageAvailableSemaphore, renderFinishedSemaphore, imageIndex);

            Context.SwapFrame();
        }

        private void Wait(Fence fence, CommandBufferWrapper commandBuffer, SemaphoreWrapper imageAvailableSemaphore, SemaphoreWrapper renderFinishedSemaphore, uint imageIndex)
        {
            using var pwaitSemaphores = new Memory<Silk.NET.Vulkan.Semaphore>([imageAvailableSemaphore.VkObjectNative]).Pin();
            using var pSignalSemaphores = new Memory<Silk.NET.Vulkan.Semaphore>([renderFinishedSemaphore.VkObjectNative]).Pin();
            using var pwaitStages = new Memory<PipelineStageFlags>([PipelineStageFlags.ColorAttachmentOutputBit]).Pin();
            var buffer = commandBuffer.VkObjectNative;
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

                // Lock the mutex before submitting to the queue
                Context.QueueMutex.WaitOne();
                try
                {
                    Context.Api.QueueSubmit(Context.Device.GraphicsQueue, 1, ref submitInfo, fence)
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
                    var result = _swapchain.SwapchainApi.QueuePresent(Context.Device.PresentQueue, &presentInfo)
                        .ThrowCode("Failed to queue present", Result.SuboptimalKhr, Result.ErrorOutOfDateKhr);
                    if (result == Result.ErrorOutOfDateKhr || result == Result.SuboptimalKhr)
                    {
                        RecreateSwapChain();
                    }
                }
                finally
                {
                    // Release the mutex
                    Context.QueueMutex.ReleaseMutex();
                }

               
            }
        }

        private async Task RecordCommandBuffer(CommandBufferWrapper commandBuffer, uint imageIndex, Project project)
        {
            BeginRenderPass(commandBuffer, imageIndex);
            var descriptors = _pipeline.DescriptorSets.Values.ToArray();
            unsafe
            {
                fixed (DescriptorSet* set = descriptors)
                    Context.Api.CmdBindDescriptorSets(commandBuffer,
                                                      PipelineBindPoint.Graphics,
                                                      _pipeline.Layout,
                                                      0,
                                                      (uint)descriptors.Length,
                                                      set,
                                                      null);

            }


            await project.CurrentScene.RenderAsync(Context, commandBuffer);

            EndRenderPass(commandBuffer);
        }

        private void EndRenderPass(CommandBufferWrapper commandBuffer)
        {
            // Ending of renderpass
            Context.Api.CmdEndRenderPass(commandBuffer);

            Context.Api.EndCommandBuffer(commandBuffer)
                .ThrowCode("Failed to record command buffer!");
        }

        private unsafe void BeginRenderPass(CommandBufferWrapper commandBuffer, uint imageIndex)
        {
            var beginInfo = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.None,
                PInheritanceInfo = default // Only relevant for secondary command buffers
            };
            Context.Api.BeginCommandBuffer(commandBuffer, &beginInfo)
                .ThrowCode("Failed to begin recording command buffer!");

            ClearValue cv = new ClearValue(color: new ClearColorValue() { Float32_0 = 0, Float32_1 = 0, Float32_2 = 0, Float32_3 = 1 });
            var renderPassInfo = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = _renderPass,
                Framebuffer = _swapchainFramebuffers[imageIndex],
                RenderArea = new Rect2D { Offset = new Offset2D(0, 0), Extent = _swapchain.Extent },
                ClearValueCount = 1,
                PClearValues = &cv
            };

            var viewport = new Viewport() { Width = Context.Surface.Size.X, Height = Context.Surface.Size.Y, MaxDepth = 1.0f };
            var scissor = new Rect2D() { Extent = new Extent2D((uint)Context.Surface.Size.X, (uint)Context.Surface.Size.Y) };

            Context.Api.CmdSetViewport(commandBuffer, 0, 1, ref viewport);
            Context.Api.CmdSetScissor(commandBuffer, 0, 1, ref scissor);

            // Start of renderpass
            Context.Api.CmdBeginRenderPass(commandBuffer, &renderPassInfo, SubpassContents.Inline);
            Context.Api.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _pipeline);
            Context.PipelineManager.CurrentPipeline = _pipeline;
        }

        private void CreateSyncObject()
        {
            var fenceci = new FenceCreateInfo()
            {
                SType = StructureType.FenceCreateInfo,
                Flags = FenceCreateFlags.SignaledBit,
            };

            _imageAvailableSemaphores = new SemaphoreWrapper[VulkanContext.MAX_FRAMES_IN_FLIGHT];
            _renderFinishedSemaphores = new SemaphoreWrapper[VulkanContext.MAX_FRAMES_IN_FLIGHT];
            _inFlightFences = new FenceWrapper[VulkanContext.MAX_FRAMES_IN_FLIGHT];

            for (int i = 0; i < VulkanContext.MAX_FRAMES_IN_FLIGHT; i++)
            {
                _imageAvailableSemaphores[i] = SemaphoreWrapper.Create(Context);
                _renderFinishedSemaphores[i] = SemaphoreWrapper.Create(Context);
                _inFlightFences[i] = FenceWrapper.Create(Context, in fenceci);
            }
        }

        private void CreateCommandPool()
        {
            var commandPool = Context.GetOrCreateCommandPool();
            var allocInfo = new CommandBufferAllocateInfo()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = commandPool,
                CommandBufferCount = 1,
                Level = CommandBufferLevel.Primary
            };
            _commandBuffers = new CommandBufferWrapper[VulkanContext.MAX_FRAMES_IN_FLIGHT];
            for (int i = 0; i < _commandBuffers.Length; i++)
            {
                _commandBuffers[i] = CommandBufferWrapper.Create(Context, ref allocInfo, commandPool);
            }
        }

        private unsafe void CreateRenderPass()
        {
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
            var subpass = new SubpassDependency()
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                SrcAccessMask = AccessFlags.None,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit,
            };

            _renderPass = RenderPassWrapper.Create(Context, [description], [attachment], [subpass]);
        }

        private unsafe void CreateFramebuffers()
        {
            _swapchainFramebuffers = new FramebufferWrapper[_swapchain.SwapChainImageViews.Length];
            for (int i = 0; i < _swapchain.Images.Length; i++)
            {
                var attachment = _swapchain.SwapChainImageViews[i].VkObjectNative;
                FramebufferCreateInfo fci = new FramebufferCreateInfo()
                {
                     SType = StructureType.FramebufferCreateInfo,
                     RenderPass = _renderPass,
                     Width = _swapchain.Extent.Width,
                     Height = _swapchain.Extent.Height,
                     AttachmentCount = 1,
                     PAttachments = &attachment,
                     Layers = 1
                };
                _swapchainFramebuffers[i] = FramebufferWrapper.Create(Context, in fci);
            }
        }

        private async Task CreateGraphicsPipeline(CancellationToken token = default)
        {
            // Load the vertex shader
            using var vertexShaderModule = await ShaderModuleWrapper.CreateAsync(Context, "..\\..\\..\\Shaders\\Shader.vert.spv", ShaderStageFlags.VertexBit, token)
                .ConfigureAwait(false);

            // Load the fragment shader
            using var fragmentShaderModule = await ShaderModuleWrapper.CreateAsync(Context, "..\\..\\..\\Shaders\\Shader.frag.spv", ShaderStageFlags.FragmentBit, token)
                .ConfigureAwait(false);

            PipelineColorBlendAttachmentState colorBlendAttachmentState = new PipelineColorBlendAttachmentState()
            {
                ColorWriteMask = ColorComponentFlags.RBit |
                    ColorComponentFlags.GBit |
                    ColorComponentFlags.BBit |
                    ColorComponentFlags.ABit
            };

            var pushConstants = vertexShaderModule.ConstantRanges.Union(fragmentShaderModule.ConstantRanges).ToArray();

            // Define descriptor set layout bindings for both camera and model
            DescriptorSetLayoutBinding[] camBindings = new DescriptorSetLayoutBinding[1]
            {
                new DescriptorSetLayoutBinding
                {
                    Binding = 0,
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType.UniformBuffer,
                    StageFlags = ShaderStageFlags.VertexBit
                }
            };
            var camLayout = CreateDescriptorLayout(camBindings);

            DescriptorSetLayoutBinding[] modelBindings = new DescriptorSetLayoutBinding[1]
            {
                new DescriptorSetLayoutBinding
                {
                    Binding = 0,
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType.UniformBuffer,
                    StageFlags = ShaderStageFlags.VertexBit
                }
            };
            var modelLayout = CreateDescriptorLayout(modelBindings);

            // Create the pipeline layout with both descriptor set layouts
            _pipelineLayout = PipelineLayoutWrapper.Create(Context, new[] { camLayout, modelLayout }, pushConstants);

            // Create Uniform Buffers

            using GraphicsPipelineBuilder pBuilder = new GraphicsPipelineBuilder(Context, "Base")
                .AddRenderPass(_renderPass)
                .WithPipelineLayout(_pipelineLayout)
                .WithDynamicState(new PipelineDynamicStateBuilder()
                    .AddState(DynamicState.Viewport)
                    .AddState(DynamicState.Scissor))
                .WithMultisampleState(new VulkanMultisampleStateInfoBuilder()
                    .Configure(false, SampleCountFlags.Count1Bit))
                .WithRasterizer(new VulkanRasterizerBuilder())
                .WithColorBlendState(new VulkanColorBlendStateBuilder()
                    .Configure(LogicOp.Copy)
                    .AddAttachment(colorBlendAttachmentState))
                .WithInputAssembly(new VulkanInputAssemblyBuilder().Configure())
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                    .Add(Vertex.GetBindingDescription(), Vertex.GetAttributeDescriptions()))
                .WithShaderModule(vertexShaderModule)
                .WithShaderModule(fragmentShaderModule)
                .WithViewportState(new VulkanViewportStateInfoBuilder()
                    .AddViewport(new Viewport())
                    .AddScissors(new Rect2D()));

            _pipeline = pBuilder.Build();

            var poolSize = new DescriptorPoolSize()
            {
                DescriptorCount = 2,
                Type = DescriptorType.UniformBuffer
            };
            var descPool = Context.DescriptorPoolFactory.GetOrCreatePool(2, new[] { poolSize });

            _pipeline.CreateDescriptorSet("CameraData", descPool, camLayout);
            _pipeline.CreateDescriptorSet("Model", descPool, modelLayout);
        }

        private unsafe DescriptorSetLayout CreateDescriptorLayout(DescriptorSetLayoutBinding[] bindings)
        {
            fixed(DescriptorSetLayoutBinding* binding = bindings)
            {
                DescriptorSetLayoutCreateInfo ci = new DescriptorSetLayoutCreateInfo()
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = (uint)bindings.Length,
                    PBindings = binding,
                };
                Context.Api.CreateDescriptorSetLayout(Context.Device, ref ci, default, out DescriptorSetLayout layout)
                    .ThrowCode("Failed to create descriptorSetLayout");
                return layout;
            }
        }


        private void CreateSwapChain(ISurfaceHandler surfacehandler)
        {
            _swapchain = SwapchainWrapper.Create(Context, surfacehandler, (uint)surfacehandler.Size.X, (uint)surfacehandler.Size.Y);
        }

        private void RecreateSwapChain()
        {
            unsafe
            {
                while (Context.Surface.Size.X == 0 || Context.Surface.Size.Y == 0)
                {
                   /* glfwApi.GetFramebufferSize((WindowHandle*)Context.Window.Handle, out width, out height);
                    glfwApi.WaitEvents();*/
                }
            }

            Context.Api.DeviceWaitIdle(Context.Device);
            CleanupSwapChain();
            CreateSwapChain(_surfaceHandler);
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
            CleanupSwapChain();
            _pipeline.Dispose();
            _pipelineLayout.Dispose();
            _renderPass.Dispose();
            _swapchain.Dispose();
        }
    }
}