using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.RockEngine.Core.Rendering;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace RockEngine.Core.Rendering
{
    public class Renderer
    {
        private readonly GraphicsEngine _graphicsEngine;
        private readonly PipelineManager _pipelineManager;
        private readonly RenderingContext _vulkanContext;
        private GlobalUbo _globalUbo;
        private BindingManager _bindingManager;


        public Camera CurrentCamera { get; set; }
        public VkCommandBuffer CurrentCommandBuffer { get; private set; }
        public VkDescriptorPool DescriptorPool { get; internal set; }
        public VkCommandPool CommandPool { get; internal set; }

        public PipelineManager PipelineManager => _pipelineManager;

        public EngineRenderPass RenderPass;
        private VkFramebuffer[] _framebuffers;
        private readonly VkDescriptorPool _descriptorPool;

        private readonly Queue<IRenderCommand> _renderCommands = new Queue<IRenderCommand>();
        private uint _dynamicOffset = 0;

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
            var poolSizes = stackalloc DescriptorPoolSize[]
            {
                new DescriptorPoolSize()
                {
                    DescriptorCount = 10_000_000,
                    Type = DescriptorType.UniformBuffer,
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 10_000_000,
                }
            };
            _descriptorPool = VkDescriptorPool.Create(context, new DescriptorPoolCreateInfo()
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                PoolSizeCount = 2,
                MaxSets = 10_000_000,
                PPoolSizes = poolSizes

            });
            _bindingManager = new BindingManager(context, _descriptorPool, graphicsEngine);
            _globalUbo = new GlobalUbo("GlobalData", 0, (ulong)Unsafe.SizeOf<Matrix4x4>());

        }

        public unsafe void Render(VkCommandBuffer vkCommandBuffer)
        {
            CurrentCommandBuffer = vkCommandBuffer;

            var clearValues = stackalloc ClearValue[]
            {
                new ClearValue() { Color = new ClearColorValue(0.1f, 0.1f, 0.1f, 1f) },
                new ClearValue() { DepthStencil = new ClearDepthStencilValue(1f, 0u) }
            };

            RenderPassBeginInfo renderPassBeginInfo = new RenderPassBeginInfo()
            {
                SType = StructureType.RenderPassBeginInfo,
                ClearValueCount = 2,
                Framebuffer = _framebuffers[_graphicsEngine.CurrentImageIndex],
                PClearValues = clearValues,
                RenderArea = new Rect2D(new Offset2D(), _graphicsEngine.Swapchain.Extent),
                RenderPass = RenderPass.RenderPass
            };

            var vp = new Viewport(0, 0, _graphicsEngine.Swapchain.Extent.Width, _graphicsEngine.Swapchain.Extent.Height, 0, 1);
            var scissor = new Rect2D(new Offset2D(0, 0), _graphicsEngine.Swapchain.Extent);

            CurrentCommandBuffer.BeginRenderPass(in renderPassBeginInfo, SubpassContents.Inline);
            CurrentCommandBuffer.SetViewport(in vp);
            CurrentCommandBuffer.SetScissor(in scissor);

            VkPipeline? prevPipeline = null;
            if (CurrentCamera is not null)
            {
                 _globalUbo.ViewProjection = CurrentCamera.ViewProjectionMatrix;

                    // BAD, REPLACE TO SOMEWHERE ELSE THAT SHIT
                 _globalUbo.UpdateAsync().GetAwaiter().GetResult();
            }
                
            var globalUboBinding = new UniformBufferBinding(_globalUbo, 0, 0);


            // Process render commands
            while (_renderCommands.Count > 0)
            {
                var command = _renderCommands.Dequeue();
                switch (command)
                {
                    case MeshRenderCommand meshCommand:
                        {
                            var mesh = meshCommand.Mesh;
                            var material = mesh.Material;
                            bool isBinded = false;
                            if (prevPipeline != material.Pipeline)
                            {
                                RenderingContext.Vk.CmdBindPipeline(CurrentCommandBuffer, PipelineBindPoint.Graphics, material.Pipeline);
                                prevPipeline = material.Pipeline;
                                 mesh.Material.Bindings.Add(globalUboBinding);
                                isBinded = true;
                            }
                            _bindingManager.BindResourcesForMaterial(material, vkCommandBuffer);

                            // BAD WAY TO CLEAN LIST OF BINDINGS, as we touch list of bindings every render frame

                            mesh.VertexBuffer.BindVertexBuffer(vkCommandBuffer);
                            if (mesh.HasIndices)
                            {
                                mesh.IndexBuffer.BindIndexBuffer(vkCommandBuffer, 0, IndexType.Uint32);
                                vkCommandBuffer.DrawIndexed((uint)mesh.Indices.Length, 1, 0, 0, 0);
                            }
                            else
                            {
                                vkCommandBuffer.Draw((uint)mesh.Vertices.Length, 1, 0, 0);
                            }
                            if (isBinded)
                            {
                                material.Bindings.Remove(globalUboBinding);
                            }

                            break;
                        }
                }
            }

            CurrentCommandBuffer.EndRenderPass();
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
                _framebuffers = new VkFramebuffer[swapChainImageViews.Length];
            }


            for (int i = 0; i < swapChainImageViews.Length; i++)
            {
                var attachments = new ImageView[] { swapChainImageViews[i].VkObjectNative, swapChainDepthImageView };
                fixed (ImageView* attachmentsPtr = attachments)
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
                    var framebuffer = VkFramebuffer.Create(_vulkanContext, in framebufferInfo);
                    _framebuffers[i] = framebuffer;
                }
            }
        }
    }
}
