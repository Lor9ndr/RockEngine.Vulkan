using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Managers.RockEngine.Core.Rendering.Managers;
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
        private readonly DescriptorSetManager _descriptorSetManager;
        private readonly RenderingContext _vulkanContext;
        private GlobalUbo _globalUbo;
        private BindingManager _bindingManager;

        // Use a dictionary to store descriptor sets for each frame
        private readonly Dictionary<uint, DescriptorSet>[] _descriptorSets;

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

            _descriptorSets = new Dictionary<uint, DescriptorSet>[context.MaxFramesPerFlight];
            for (int i = 0; i < _descriptorSets.Length; i++)
            {
                _descriptorSets[i] = new Dictionary<uint, DescriptorSet>();
            }

            CreateRenderPass();
            CreateFramebuffers(_graphicsEngine.Swapchain);

            // Create descriptor pool with enough capacity
            var poolSizes = stackalloc DescriptorPoolSize[]
            {
                new DescriptorPoolSize()
                {
                    DescriptorCount = (uint)graphicsEngine.Swapchain.Images.Length,
                    Type = DescriptorType.UniformBuffer,
                },
                new DescriptorPoolSize()
                {
                    Type = DescriptorType.CombinedImageSampler,
                    DescriptorCount = (uint)graphicsEngine.Swapchain.Images.Length,
                }
            };
            _descriptorPool = VkDescriptorPool.Create(context, new DescriptorPoolCreateInfo()
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                PoolSizeCount = 2,
                MaxSets = 500,
                PPoolSizes = poolSizes

            });
            _descriptorSetManager = new DescriptorSetManager(context, _descriptorPool);
            _bindingManager = new BindingManager(context, _descriptorSetManager, graphicsEngine);
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
                RenderArea = new Rect2D() { Extent = _graphicsEngine.Swapchain.Extent, Offset = new Offset2D() },
                RenderPass = RenderPass.RenderPass
            };

            CurrentCommandBuffer.BeginRenderPass(in renderPassBeginInfo, SubpassContents.Inline);
            var vp = new Viewport(0, 0, _graphicsEngine.Swapchain.Extent.Width, _graphicsEngine.Swapchain.Extent.Height, 0, 1);
            CurrentCommandBuffer.SetViewport(in vp);
            var scissor = new Rect2D(new Offset2D(0, 0), _graphicsEngine.Swapchain.Extent);
            CurrentCommandBuffer.SetScissor(in scissor);

            VkPipeline? prevPipeline = null;
            if (CurrentCamera is not null)
            {
                 _globalUbo.ViewProjection = CurrentCamera.ViewProjectionMatrix;

                    // BAD REPLACE TO SOMEWHERE ELSE THAT SHIT
                 _globalUbo.UpdateAsync(_globalUbo.ViewProjection).GetAwaiter().GetResult();
            }
                
            var globalUboBinding = new UniformBufferBinding(_globalUbo, 0, 0);


            // Process render commands
            while (_renderCommands.Count > 0)
            {
                var command = _renderCommands.Dequeue();
                switch (command)
                {
                    case DescriptorBindingCommand descriptorBindingCommand:
                        BindDescriptorSet(in descriptorBindingCommand);
                        break;
                    case MeshRenderCommand meshCommand:
                        {
                            var mesh = meshCommand.Mesh;
                            var material = mesh.Material;

                            if (prevPipeline != material.Pipeline)
                            {
                                RenderingContext.Vk.CmdBindPipeline(CurrentCommandBuffer, PipelineBindPoint.Graphics, material.Pipeline);
                                prevPipeline = material.Pipeline;
                            }
                            mesh.Material.AddBinding(globalUboBinding);
                            _bindingManager.BindResourcesForMaterial(material, vkCommandBuffer, material.Pipeline.Layout);

                            // BAD WAY TO CLEAN LIST OF BINDINGS, as we touch list of bindings every render frame
                            mesh.Material.RemoveBinding(globalUboBinding);

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

                            break;
                        }
                }
            }

            CurrentCommandBuffer.EndRenderPass();
        }

        private unsafe void BindDescriptorSet(in DescriptorBindingCommand descriptorBindingCommand)
        {
            var layout = _pipelineManager.GetPipelineByName("Main").Layout;
            RenderingContext.Vk.CmdBindDescriptorSets(
                CurrentCommandBuffer,
                PipelineBindPoint.Graphics,
                layout,
                descriptorBindingCommand.SetLocation,
                1,
                [descriptorBindingCommand.DescriptorSet],
                0,
                []
            );
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


        public void BindUniformBuffer(UniformBuffer ubo, uint set = 0)
        {
            if (ubo.IsDynamic)
            {
                ubo.DynamicOffset = _dynamicOffset;
                _dynamicOffset += (uint)ubo.Size; // Increment for the next dynamic buffer
            }

            var layout = _pipelineManager.TryGetLayout(ubo);

            if (layout != default)
            {
                // Allocate descriptor set for the current frame if it doesn't exist
                var currentFrameIndex = _graphicsEngine.CurrentImageIndex;
                if (!_descriptorSets[currentFrameIndex].TryGetValue(set, out var descriptorSet))
                {
                    descriptorSet = _descriptorSetManager.AllocateDescriptorSet(layout);
                    _descriptorSetManager.UpdateDescriptorSet(descriptorSet, ubo);
                    _descriptorSets[currentFrameIndex][set] = descriptorSet;
                }

                // Enqueue the descriptor binding command
                var command = new DescriptorBindingCommand(descriptorSet, layout.SetLocation, ubo.BindingLocation);
                _renderCommands.Enqueue(command);
            }
            else
            {
                throw new Exception("Failed to find some layout");
            }
        }

        internal void RegisterBuffer(UniformBuffer buffer, uint set = 0)
        {
            var layout = _pipelineManager.TryGetLayout(buffer);
            if (layout == default)
            {
                throw new Exception("Unable to find layout by buffer");
            }
            var currentFrameIndex = _graphicsEngine.CurrentImageIndex;

            if (!_descriptorSets[currentFrameIndex].TryGetValue(set, out var descriptorSet))
            {
                descriptorSet = _descriptorSetManager.AllocateDescriptorSet(layout);
                _descriptorSets[currentFrameIndex][set] = descriptorSet;
            }

            _descriptorSetManager.UpdateDescriptorSet(descriptorSet, buffer);
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
