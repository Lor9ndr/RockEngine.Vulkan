using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.RockEngine.Core.Rendering;
using RockEngine.Vulkan;
using RockEngine.Vulkan.Builders;

using Silk.NET.Vulkan;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace RockEngine.Core.Rendering
{
    public class Renderer:IDisposable
    {
        private readonly GraphicsEngine _graphicsEngine;
        private readonly PipelineManager _pipelineManager;
        private readonly RenderingContext _vulkanContext;
        private readonly GlobalUbo _globalUbo;
        private readonly UniformBufferBinding _globalUboBinding;
        private readonly BindingManager _bindingManager;
        private VkPipeline _deferredLightingPipeline;
        private VkPipelineLayout _deferredPipelineLayout;
        private VkFrameBuffer[] _framebuffers;
        private readonly DescriptorPoolManager _descriptorPoolManager;

        private readonly Queue<IRenderCommand> _renderCommands = new Queue<IRenderCommand>();

        public readonly GBuffer GBuffer;
        public Camera CurrentCamera { get; set; }
        public VkCommandBuffer CurrentCommandBuffer { get; private set; }
        public VkDescriptorPool DescriptorPool { get; internal set; }
        public VkCommandPool CommandPool { get; internal set; }

        public PipelineManager PipelineManager => _pipelineManager;

        public EngineRenderPass RenderPass;
        

        public unsafe Renderer(RenderingContext context, GraphicsEngine graphicsEngine, PipelineManager pipelineManager)
        {
            _vulkanContext = context;
            _graphicsEngine = graphicsEngine;
            _pipelineManager = pipelineManager;
            CommandPool = _graphicsEngine.CommandBufferPool;
            _graphicsEngine.Swapchain.OnSwapchainRecreate += CreateFramebuffers;


            CreateRenderPass();
            CreateFramebuffers(_graphicsEngine.Swapchain);

            // Create descriptor pool with enough capacity
            var poolSizes = new[]
           {
                new DescriptorPoolSize(DescriptorType.UniformBuffer, 5_000),
                new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 5_000)
            };
            _descriptorPoolManager = new DescriptorPoolManager(
                context,
                poolSizes,
                maxSetsPerPool: 5_000
            );

            _bindingManager = new BindingManager(context, _descriptorPoolManager, graphicsEngine);
            _globalUbo = new GlobalUbo("GlobalData", 0, (ulong)Unsafe.SizeOf<Matrix4x4>());
            _globalUboBinding = new UniformBufferBinding(_globalUbo, 0, 0);
            GBuffer = new GBuffer(context, graphicsEngine.Swapchain, graphicsEngine);
            CreateLightingResources();
        }

        private unsafe void CreateLightingResources()
        {
            var vertShader = VkShaderModule.Create(_vulkanContext, "Shaders/deferred_lighting.vert.spv", ShaderStageFlags.VertexBit);
            var fragShader = VkShaderModule.Create(_vulkanContext, "Shaders/deferred_lighting.frag.spv", ShaderStageFlags.FragmentBit);

            _deferredPipelineLayout = VkPipelineLayout.Create(_vulkanContext, vertShader, fragShader);
            
             GBuffer.CreateLightingDescriptorSets(_bindingManager, _deferredPipelineLayout);

            // Create pipeline
            var colorBlendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                               ColorComponentFlags.BBit | ColorComponentFlags.ABit
            };

            using var pipelineBuilder = new GraphicsPipelineBuilder(_vulkanContext, "DeferredLighting")
                .WithShaderModule(vertShader)
                .WithShaderModule(fragShader)
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder())
                .WithInputAssembly(new VulkanInputAssemblyBuilder()
                                                            .Configure())
                .WithViewportState(new VulkanViewportStateInfoBuilder()
                    .AddViewport(new Viewport(0, 0, _graphicsEngine.Swapchain.Extent.Width,
                                             _graphicsEngine.Swapchain.Extent.Height, 0, 1))
                    .AddScissors(new Rect2D(new Offset2D(), _graphicsEngine.Swapchain.Extent)))
                .WithRasterizer(new VulkanRasterizerBuilder().CullFace(CullModeFlags.None))
                .WithMultisampleState(new VulkanMultisampleStateInfoBuilder().Configure(false, SampleCountFlags.Count1Bit))
                .WithColorBlendState(new VulkanColorBlendStateBuilder().AddAttachment(colorBlendAttachment))
                .AddRenderPass(RenderPass.RenderPass)
                .WithPipelineLayout(_deferredPipelineLayout)
                .WithDynamicState(new PipelineDynamicStateBuilder()
                    .AddState(DynamicState.Viewport)
                    .AddState(DynamicState.Scissor));

            _deferredLightingPipeline = _pipelineManager.Create(pipelineBuilder);
        }

        public async Task Render(VkCommandBuffer vkCommandBuffer)
        {
            CurrentCommandBuffer = vkCommandBuffer;
            await RenderGeometryPass();
            RenderLightingPass();
        }

        private async Task RenderGeometryPass()
        {
            GBuffer.BeginGeometryPass(CurrentCommandBuffer, _graphicsEngine.Swapchain.Extent);

            CurrentCommandBuffer.SetViewport(new Viewport(0, 0, _graphicsEngine.Swapchain.Extent.Width, _graphicsEngine.Swapchain.Extent.Height, 0, 1));
            CurrentCommandBuffer.SetScissor(new Rect2D(new Offset2D(), _graphicsEngine.Swapchain.Extent));

            if (CurrentCamera != null)
            {
                _globalUbo.ViewProjection = CurrentCamera.ViewProjectionMatrix;
                await _globalUbo.UpdateAsync();
            }

            while (_renderCommands.Count > 0 && _renderCommands.Peek() is MeshRenderCommand)
            {
                var command = (MeshRenderCommand)_renderCommands.Dequeue();
                ProcessGeometryCommand(command);
            }

            CurrentCommandBuffer.EndRenderPass();
        }

        private unsafe void RenderLightingPass()
        {
            var clearValues = stackalloc ClearValue[2];
            clearValues[0] = new ClearValue { Color = new ClearColorValue(0.1f, 0.1f, 0.1f, 1f) };
            clearValues[1] = new ClearValue { DepthStencil = new ClearDepthStencilValue(1f, 0) }; // Valid depth value

            var renderPassBeginInfo = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = RenderPass.RenderPass,
                Framebuffer = _framebuffers[_graphicsEngine.CurrentImageIndex],
                ClearValueCount = 2, 
                PClearValues = clearValues,
                RenderArea = new Rect2D { Extent = _graphicsEngine.Swapchain.Extent }
            };


            CurrentCommandBuffer.BeginRenderPass(in renderPassBeginInfo, SubpassContents.Inline);
            CurrentCommandBuffer.SetViewport(new Viewport(0, 0,
                _graphicsEngine.Swapchain.Extent.Width,
                _graphicsEngine.Swapchain.Extent.Height, 0, 1));
            CurrentCommandBuffer.SetScissor(new Rect2D(new Offset2D(), _graphicsEngine.Swapchain.Extent));

            CurrentCommandBuffer.BindPipeline(_deferredLightingPipeline, PipelineBindPoint.Graphics);

            GBuffer.BindResources(_bindingManager, _deferredPipelineLayout, CurrentCommandBuffer);
            
            CurrentCommandBuffer.Draw(3, 1, 0, 0);

            while (_renderCommands.Count > 0)
            {
                var command = _renderCommands.Dequeue();
                if (command is ImguiRenderCommand imguiCommand)
                {
                    imguiCommand.RenderCommand.Invoke(CurrentCommandBuffer, _graphicsEngine.Swapchain.Extent);
                }
            }

            CurrentCommandBuffer.EndRenderPass();
        }

        private unsafe void ProcessGeometryCommand(MeshRenderCommand command)
        {
            var mesh = command.Mesh;
            var material = mesh.Material;

            // Use geometry-specific pipeline
            RenderingContext.Vk.CmdBindPipeline(CurrentCommandBuffer,
                PipelineBindPoint.Graphics,
                material.GeometryPipeline);
            mesh.Material.Bindings.Add(_globalUboBinding);
            _bindingManager.BindResourcesForMaterial(material, CurrentCommandBuffer);
            mesh.Material.Bindings.Remove(_globalUboBinding);

            mesh.VertexBuffer.BindVertexBuffer(CurrentCommandBuffer);
            if (mesh.HasIndices)
            {
                mesh.IndexBuffer.BindIndexBuffer(CurrentCommandBuffer, 0, IndexType.Uint32);
                CurrentCommandBuffer.DrawIndexed((uint)mesh.Indices.Length, 1, 0, 0, 0);
            }
            else
            {
                CurrentCommandBuffer.Draw((uint)mesh.Vertices.Length, 1, 0, 0);
            }
        }

        private unsafe void CreateRenderPass()
        {
            var colorAttachment = new AttachmentDescription
            {
                Format = _graphicsEngine.Swapchain.Format,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.Clear,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.PresentSrcKhr
            };

            var colorAttachmentReference = new AttachmentReference
            {
                Attachment = 0,
                Layout = ImageLayout.ColorAttachmentOptimal,

            };

            var depthAttachment = new AttachmentDescription
            {
                Format = _graphicsEngine.Swapchain.DepthFormat,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.DontCare,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
            };

            var depthAttachmentReference = new AttachmentReference
            {
                Attachment = 1,
                Layout = ImageLayout.DepthStencilAttachmentOptimal
            };

            var description = new SubpassDescription
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = &colorAttachmentReference,
                PDepthStencilAttachment = &depthAttachmentReference,
            };

            var dependency = new SubpassDependency[]
            {
                new SubpassDependency
                {
                    SrcSubpass = Vk.SubpassExternal,
                    DstSubpass = 0,
                    SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
                    SrcAccessMask = AccessFlags.None,
                    DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
                    DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
                },
            };

            RenderPass = _graphicsEngine.RenderPassManager.CreateRenderPass(RenderPassType.ColorDepth, [description], [colorAttachment, depthAttachment], dependency);
        }


        public void Draw(Mesh mesh)
        {
            _renderCommands.Enqueue(new MeshRenderCommand(mesh));
        }


        private unsafe void CreateFramebuffers(VkSwapchain swapchain)
        {
            var swapChainImageViews = swapchain.SwapChainImageViews;
            var swapChainDepthImageView = swapchain.DepthImageView;
            var swapChainExtent = swapchain.Extent;

            if (_framebuffers is not null)
            {
                foreach (var item in _framebuffers)
                {
                    item.Dispose();
                }
            }
            else
            {
                _framebuffers = new VkFrameBuffer[swapChainImageViews.Length];
            }


            for (int i = 0; i < swapChainImageViews.Length; i++)
            {
                var vkAttachments = new VkImageView[] { swapChainImageViews[i], swapChainDepthImageView };
                fixed (ImageView* attachmentsPtr = vkAttachments.Select(s=>s.VkObjectNative).ToArray())
                {
                    var framebufferInfo = new FramebufferCreateInfo
                    {
                        SType = StructureType.FramebufferCreateInfo,
                        RenderPass = RenderPass.RenderPass,
                        AttachmentCount = 2,
                        PAttachments = attachmentsPtr,
                        Width = swapChainExtent.Width,
                        Height = swapChainExtent.Height,
                        Layers = 1
                    };
                    var framebuffer = VkFrameBuffer.Create(_vulkanContext, in framebufferInfo, vkAttachments);
                    _framebuffers[i] = framebuffer;
                }
            }
        }

        public void AddCommand(IRenderCommand imguiRenderCommand)
        {
            _renderCommands.Enqueue(imguiRenderCommand);
        }

        public void Dispose()
        {
            _globalUbo.Dispose();
        }
    }
}
