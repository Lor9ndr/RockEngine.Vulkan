using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Core.Rendering.Contexts;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;
using RockEngine.Vulkan.Builders;

using Silk.NET.Vulkan;

using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering
{
    public class Renderer : IDisposable
    {
        private readonly GraphicsEngine _graphicsEngine;
        private readonly PipelineManager _pipelineManager;
        private readonly VulkanContext _vulkanContext;
        private readonly GlobalUbo _globalUbo;

        private readonly UniformBufferBinding _countLightUboBinding;
        private readonly UniformBufferBinding _globalUboBinding;
        private readonly StorageBuffer<LightData>[] _lightBuffers;
        private StorageBufferBinding<LightData>[] _lightBindings;

        private StorageBufferBinding<Matrix4x4>[] _transformBindings;

        private readonly UniformBuffer _countLightUbo;

        private readonly BindingManager _bindingManager;
        private VkPipeline _deferredLightingPipeline;
        private VkPipelineLayout _deferredPipelineLayout;
        private VkFrameBuffer[] _framebuffers;
        private readonly DescriptorPoolManager _descriptorPoolManager;

        private readonly List<Light> _activeLights = new List<Light>();


        public Camera CurrentCamera { get; set; }
        public VkCommandBuffer CurrentCommandBuffer { get; private set; }
        public VkDescriptorPool DescriptorPool { get; private set; }
        public VkCommandPool CommandPool { get; private set; }
        public GBuffer GBuffer { get; private set; }


        public PipelineManager PipelineManager => _pipelineManager;

        public EngineRenderPass RenderPass;
        private readonly bool _isDirty = true;
        private int _currentLightBufferIndex;
        private readonly ResourceLifecycleManager _lifecycleManager;
        public const ulong MAX_LIGHTS_SUPPORTED = 10_000;

        private const ulong MAX_TRANSFORMS = 10_000_000;

        private readonly List<VkCommandBuffer>[] _pendingSecondaryBuffers;
        private int _currentFrameIndex;


        private IndirectBuffer _indirectBuffer;
        private List<( VkPipeline Pipeline, Mesh mesh, uint Count, ulong Offset)> _pipelineDrawGroups = new();
        private DrawIndexedIndirectCommand[] _currentIndirectCommands;

        private readonly List<MeshRenderCommand> _meshDrawCommands = new List<MeshRenderCommand>();
        private readonly ConcurrentQueue<IRenderCommand> _otherRenderCommands = new ConcurrentQueue<IRenderCommand>();

        private readonly StorageBuffer<Matrix4x4>[] _transformBuffers;
        private readonly List<ulong> _currentFrameOffsets = new List<ulong>();

        public readonly SubmitContext SubmitContext;
        private readonly Dictionary<Pipeline,Material> GlobalMaterial;
        public unsafe Renderer(VulkanContext context, GraphicsEngine graphicsEngine, PipelineManager pipelineManager)
        {
            _vulkanContext = context;
            _graphicsEngine = graphicsEngine;
            _pipelineManager = pipelineManager;
            CommandPool = _graphicsEngine.CommandBufferPool;
            _graphicsEngine.Swapchain.OnSwapchainRecreate += CreateFramebuffers;
            SubmitContext = new SubmitContext(_vulkanContext);
            GlobalMaterial = new Dictionary<Pipeline, Material>();

            _currentFrameIndex = 0;
            _pendingSecondaryBuffers = new List<VkCommandBuffer>[context.MaxFramesPerFlight];
            for (int i = 0; i < _pendingSecondaryBuffers.Length; i++)
            {
                _pendingSecondaryBuffers[i] = new List<VkCommandBuffer>();
            }

            // Create descriptor pool with enough capacity
            var poolSizes = new[]
           {
                new DescriptorPoolSize(DescriptorType.UniformBuffer, 5_000),
                new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 5_000),
                new DescriptorPoolSize(DescriptorType.StorageBuffer, 5_000),
                new DescriptorPoolSize( DescriptorType.InputAttachment, 3)
            };
            _descriptorPoolManager = new DescriptorPoolManager(
                context,
                poolSizes,
                maxSetsPerPool: 5_000
            );

            _bindingManager = new BindingManager(context, _descriptorPoolManager, graphicsEngine);
            _globalUbo = new GlobalUbo("GlobalData", 0);
            _globalUboBinding = new UniformBufferBinding(_globalUbo, 0, 0);

            _lightBuffers = new StorageBuffer<LightData>[_vulkanContext.MaxFramesPerFlight];
            for (int i = 0; i < _lightBuffers.Length; i++)
            {
                _lightBuffers[i] = new StorageBuffer<LightData>(
                    _vulkanContext,
                    MAX_LIGHTS_SUPPORTED
                );
            }

            _countLightUbo = new UniformBuffer("LightCount", 1, sizeof(uint), sizeof(uint));

            _countLightUboBinding = new UniformBufferBinding(_countLightUbo, 1, 1);
            GBuffer = new GBuffer(_vulkanContext, _graphicsEngine.Swapchain, _graphicsEngine, _bindingManager);
            CreateRenderPass();
            CreateLightingResources();
            CreateFramebuffers(_graphicsEngine.Swapchain);

            _lifecycleManager = new ResourceLifecycleManager(context);

            _indirectBuffer = new IndirectBuffer(_vulkanContext, 1024 * (ulong)Marshal.SizeOf<DrawIndexedIndirectCommand>());
            _transformBuffers = new StorageBuffer<Matrix4x4>[_vulkanContext.MaxFramesPerFlight];
            _transformBindings = new StorageBufferBinding<Matrix4x4>[_vulkanContext.MaxFramesPerFlight];
            for (int i = 0; i < _transformBuffers.Length; i++)
            {
                // Create buffer for 1024 matrices
                _transformBuffers[i] = new StorageBuffer<Matrix4x4>(
                    _vulkanContext,
                    1024                   
                );
                _transformBindings[i] = new StorageBufferBinding<Matrix4x4>(_transformBuffers[i], 0,1);

            }

            /* var alignment = _vulkanContext.Device.PhysicalDevice.Properties.Limits.MinUniformBufferOffsetAlignment;
             ulong matrixSize = (ulong)Unsafe.SizeOf<Matrix4x4>();
             _alignedMatrixSize = matrixSize;
             if (alignment > 0)
             {
                 _alignedMatrixSize = (matrixSize + alignment - 1) & ~(alignment - 1);
             }

             // Initialize transform buffers
             _transformBuffers = new UniformBuffer[_vulkanContext.MaxFramesPerFlight];
             _transformBindings = new UniformBufferBinding[_vulkanContext.MaxFramesPerFlight];
             for (int i = 0; i < _transformBuffers.Length; i++)
             {
                 _transformBuffers[i] = new UniformBuffer("Transforms", 0, MAX_TRANSFORMS * _alignedMatrixSize, (int)_alignedMatrixSize, true);
                 _transformBindings[i] = new UniformBufferBinding(_transformBuffers[i], 0,  1, 0, true);
             }*/
        }

        private unsafe void CreateLightingResources()
        {
            var vertShader = VkShaderModule.Create(_vulkanContext, "Shaders/deferred_lighting.vert.spv", ShaderStageFlags.VertexBit);
            var fragShader = VkShaderModule.Create(_vulkanContext, "Shaders/deferred_lighting.frag.spv", ShaderStageFlags.FragmentBit);

            _deferredPipelineLayout = VkPipelineLayout.Create(_vulkanContext, vertShader, fragShader);

            var colorBlendAttachments = new PipelineColorBlendAttachmentState[1];
            colorBlendAttachments[0] = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                 ColorComponentFlags.BBit | ColorComponentFlags.ABit
            };

            using var pipelineBuilder = new GraphicsPipelineBuilder(_vulkanContext, "DeferredLighting")
                .WithShaderModule(vertShader)
                .WithShaderModule(fragShader)
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder())
                .WithInputAssembly(new VulkanInputAssemblyBuilder().Configure())
                .WithViewportState(new VulkanViewportStateInfoBuilder()
                    .AddViewport(new Viewport(0, 0, _graphicsEngine.Swapchain.Extent.Width,
                                             _graphicsEngine.Swapchain.Extent.Height, 0, 1))
                    .AddScissors(new Rect2D(new Offset2D(), _graphicsEngine.Swapchain.Extent)))
                .WithRasterizer(new VulkanRasterizerBuilder().CullFace(CullModeFlags.None))
                .WithMultisampleState(new VulkanMultisampleStateInfoBuilder().Configure(false, SampleCountFlags.Count1Bit))
                .WithColorBlendState(new VulkanColorBlendStateBuilder().AddAttachment(colorBlendAttachments))
                .AddRenderPass(RenderPass.RenderPass)
                .WithSubpass(1)
                .WithPipelineLayout(_deferredPipelineLayout)
                .WithDynamicState(new PipelineDynamicStateBuilder()
                    .AddState(DynamicState.Viewport)
                    .AddState(DynamicState.Scissor));

            _deferredLightingPipeline = _pipelineManager.Create(pipelineBuilder);

            GBuffer.CreateLightingDescriptorSets(_deferredLightingPipeline);
            _lightBindings = new StorageBufferBinding<LightData>[_lightBuffers.Length];
            GBuffer.Material.Bindings.Add(_countLightUboBinding);

            for (int i = 0; i < _lightBuffers.Length; i++)
            {
                _lightBindings[i] = new StorageBufferBinding<LightData>(_lightBuffers[i], 0, 1, 0);
            }
            GBuffer.Material.Bindings.Add(_lightBindings[_currentLightBufferIndex]);
            GBuffer.Material.Bindings.Add(_globalUboBinding);

        }


        public async Task Render(VkCommandBuffer primaryCmdBuffer)
        {
            using (PerformanceTracer.BeginSection("Frame Render"))
            {
                CurrentCommandBuffer = primaryCmdBuffer;
                var cmdBuffers = _pendingSecondaryBuffers[_currentFrameIndex];
                // Clear previous frame's buffers
                foreach (var buf in cmdBuffers)
                {
                    buf.Dispose();
                }
                cmdBuffers.Clear();
                // Allocate secondary command buffers for each subpass
                var geometryCmd = _graphicsEngine.CommandBufferPool.AllocateCommandBuffer(CommandBufferLevel.Secondary);
                var lightingCmd = _graphicsEngine.CommandBufferPool.AllocateCommandBuffer(CommandBufferLevel.Secondary);
                var imguiCmd = _graphicsEngine.CommandBufferPool.AllocateCommandBuffer(CommandBufferLevel.Secondary);
                cmdBuffers.Add(geometryCmd);
                cmdBuffers.Add(lightingCmd);
                cmdBuffers.Add(imguiCmd);

                // Record commands into secondary command buffers
                var geomTask = RecordGeometrySubpass(geometryCmd);
                var lightTask = RecordLightingSubpass(lightingCmd);
                var imguiTask = RecordImGuiSubpass(imguiCmd);

                BeginMainRenderPass(primaryCmdBuffer);

                await Task.WhenAll(geomTask, lightTask, imguiTask);
                // Execute secondary command buffers for each subpass
                primaryCmdBuffer.ExecuteSecondary([geometryCmd]);
                primaryCmdBuffer.NextSubpass(SubpassContents.SecondaryCommandBuffers);
                primaryCmdBuffer.ExecuteSecondary([lightingCmd]);
                primaryCmdBuffer.NextSubpass(SubpassContents.SecondaryCommandBuffers);
                primaryCmdBuffer.ExecuteSecondary([imguiCmd]);

                primaryCmdBuffer.EndRenderPass();


                _currentFrameIndex = (_currentFrameIndex + 1) % _vulkanContext.MaxFramesPerFlight;
            }
        }

        private void BeginMainRenderPass(VkCommandBuffer primaryCmdBuffer)
        {
            unsafe
            {
                int clearValuesLength = GBuffer.ColorAttachments.Length + 2;
                unsafe
                {
                    ClearValue* clearValues = stackalloc ClearValue[clearValuesLength];

                    // Initialize GBuffer color clear values
                    for (int i = 0; i < GBuffer.ColorAttachments.Length; i++)
                    {
                        clearValues[i] = new ClearValue
                        {
                            Color = new ClearColorValue(0.0f, 0.0f, 0.0f, 1.0f)
                        };
                    }

                    // Initialize depth clear value
                    clearValues[GBuffer.ColorAttachments.Length] = new ClearValue
                    {
                        DepthStencil = new ClearDepthStencilValue(1.0f, 0)
                    };

                    // Lighting pass clear value
                    clearValues[GBuffer.ColorAttachments.Length + 1] = new ClearValue { Color = new ClearColorValue(0.1f, 0.1f, 0.1f, 1f) };

                    var beginInfo = new RenderPassBeginInfo
                    {
                        SType = StructureType.RenderPassBeginInfo,
                        RenderPass = RenderPass.RenderPass,
                        Framebuffer = _framebuffers[_graphicsEngine.CurrentImageIndex],
                        ClearValueCount = (uint)clearValuesLength,
                        PClearValues = clearValues,
                        RenderArea = new Rect2D { Extent = _graphicsEngine.Swapchain.Extent },
                    };

                    primaryCmdBuffer.BeginRenderPass(in beginInfo, SubpassContents.SecondaryCommandBuffers);
                }
            }
        }

        private Task RecordLightingSubpass(VkCommandBuffer cmd)
        {
            using (PerformanceTracer.BeginSection("RecordLightingSubpass"))
            {
                unsafe
                {
                    var inheritanceInfo = new CommandBufferInheritanceInfo
                    {
                        SType = StructureType.CommandBufferInheritanceInfo,
                        RenderPass = RenderPass.RenderPass,
                        Subpass = 1,
                        Framebuffer = _framebuffers[_graphicsEngine.CurrentImageIndex]
                    };

                    var beginInfo = new CommandBufferBeginInfo
                    {
                        SType = StructureType.CommandBufferBeginInfo,
                        Flags = CommandBufferUsageFlags.RenderPassContinueBit,
                        PInheritanceInfo = &inheritanceInfo
                    };
                    cmd.Begin(in beginInfo);

                }


                cmd.SetViewport(new Viewport(0, 0, _graphicsEngine.Swapchain.Extent.Width, _graphicsEngine.Swapchain.Extent.Height, 0, 1));
                cmd.SetScissor(new Rect2D(new Offset2D(), _graphicsEngine.Swapchain.Extent));
                cmd.BindPipeline(_deferredLightingPipeline, PipelineBindPoint.Graphics);
                _bindingManager.BindResourcesForMaterial(GBuffer.Material, cmd);
                cmd.Draw(3, 1, 0, 0);

                cmd.End();
                return Task.CompletedTask;
            }
        }

        private Task RecordImGuiSubpass(VkCommandBuffer cmd)
        {
            using (PerformanceTracer.BeginSection("RecordImGuiSubpass"))
            {
                unsafe
                {
                    var inheritanceInfo = new CommandBufferInheritanceInfo
                    {
                        SType = StructureType.CommandBufferInheritanceInfo,
                        RenderPass = RenderPass.RenderPass,
                        Subpass = 2,
                        Framebuffer = _framebuffers[_graphicsEngine.CurrentImageIndex]
                    };

                    var beginInfo = new CommandBufferBeginInfo
                    {
                        SType = StructureType.CommandBufferBeginInfo,
                        Flags = CommandBufferUsageFlags.RenderPassContinueBit,
                        PInheritanceInfo = &inheritanceInfo
                    };

                    cmd.Begin(in beginInfo);
                }

                cmd.SetViewport(new Viewport(0, 0, _graphicsEngine.Swapchain.Extent.Width, _graphicsEngine.Swapchain.Extent.Height, 0, 1));
                cmd.SetScissor(new Rect2D(new Offset2D(), _graphicsEngine.Swapchain.Extent));
                // Process ImGui commands
                while (_otherRenderCommands.TryPeek(out var command) && command is ImguiRenderCommand imguiCmd)
                {
                    imguiCmd.RenderCommand.Invoke(cmd, _graphicsEngine.Swapchain.Extent);
                    while (!_otherRenderCommands.TryDequeue(out _))
                    {

                    }
                }

                cmd.End();
                return Task.CompletedTask;
            }
        }

        private Task RecordGeometrySubpass(VkCommandBuffer cmd)
        {
            using (PerformanceTracer.BeginSection("RecordGeometrySubpass"))
            {
                unsafe
                {
                    var inheritanceInfo = new CommandBufferInheritanceInfo
                    {
                        SType = StructureType.CommandBufferInheritanceInfo,
                        RenderPass = RenderPass.RenderPass,
                        Subpass = 0,
                        Framebuffer = _framebuffers[_graphicsEngine.CurrentImageIndex]
                    };

                    var beginInfo = new CommandBufferBeginInfo
                    {
                        SType = StructureType.CommandBufferBeginInfo,
                        Flags = CommandBufferUsageFlags.RenderPassContinueBit,
                        PInheritanceInfo = &inheritanceInfo
                    };

                    cmd.Begin(in beginInfo);
                }


                cmd.SetViewport(new Viewport(0, 0, _graphicsEngine.Swapchain.Extent.Width, _graphicsEngine.Swapchain.Extent.Height, 0, 1));
                cmd.SetScissor(new Rect2D(new Offset2D(), _graphicsEngine.Swapchain.Extent));

                // Process MeshRenderCommand
                foreach (var (pipeline, mesh, count, offset) in _pipelineDrawGroups)
                {
                    // Bind pipeline and resources
                    VulkanContext.Vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, pipeline);

                   /* if (!GlobalMaterial.ContainsKey(pipeline))
                    {
                        var mat = new Material(pipeline);
                        GlobalMaterial[pipeline] = mat;
                    
                    }
                    _bindingManager.BindResourcesForMaterial(GlobalMaterial[pipeline], cmd);*/

                    // Bind material descriptors

                    _bindingManager.BindResourcesForMaterial(mesh.Material, cmd);

                    mesh.VertexBuffer.BindVertexBuffer(cmd);
                    mesh.IndexBuffer!.BindIndexBuffer(cmd, 0, IndexType.Uint32);

                    // Handle multi-draw support
                    if (_vulkanContext.Device.PhysicalDevice.Features2.Features.MultiDrawIndirect)
                    {
                        // Use single draw call for all commands in this group
                        VulkanContext.Vk.CmdDrawIndexedIndirect(
                            cmd,
                            _indirectBuffer.Buffer,
                            offset,
                            count,
                            (uint)Marshal.SizeOf<DrawIndexedIndirectCommand>());
                    }
                    else
                    {
                        // Fallback: Draw each command individually
                        var stride = (uint)Marshal.SizeOf<DrawIndexedIndirectCommand>();
                        for (uint i = 0; i < count; i++)
                        {
                            VulkanContext.Vk.CmdDrawIndexedIndirect(
                                cmd,
                                _indirectBuffer.Buffer,
                                offset + (ulong)(i * stride),
                                1, // Single draw per call
                                stride);
                        }
                    }
                }

                cmd.End();
                return Task.CompletedTask;
            }
        }

   
     
        public void Draw(Mesh mesh)
        {
            _meshDrawCommands.Add(new MeshRenderCommand(mesh));
        }
        public void RegisterLight(Light light)
        {
            _activeLights.Add(light);
        }

        public void UnregisterLight(Light light) => _activeLights.Remove(light);

        public async ValueTask UpdateAsync()
        {
            using (PerformanceTracer.BeginSection("Frame Update"))
            {
                if (CurrentCamera != null)
                {
                    _globalUbo.GlobalData = new GlobalUbo.GlobalUboData()
                    {
                        ViewProjection = CurrentCamera.ViewProjectionMatrix,
                        Position = CurrentCamera.Entity.Transform.Position
                    };
                }

                _lifecycleManager.ProcessCompletedFrames();
                _lifecycleManager.BeginFrame();

                var uploadBatch = SubmitContext.CreateBatch();
                using (PerformanceTracer.BeginSection("Buffer Updates"))
                {
                    UpdateLightBuffer(uploadBatch);
                    UpdateTransformBuffer(uploadBatch);
                    UpdateIndirectCommands(uploadBatch);
                    await StageGlobalUbo();

                    uploadBatch.Submit(SubmitContext);
                    await SubmitContext.FlushAsync();
                }

                await _globalUbo.UpdateAsync();
                _currentFrameIndex = (_currentFrameIndex + 1) % _vulkanContext.MaxFramesPerFlight;
            }
        }

        private void UpdateLightBuffer(UploadBatch batch)
        {
            var lightData = new LightData[_activeLights.Count];
            for (int i = 0; i < _activeLights.Count; i++)
            {
                lightData[i] = _activeLights[i].GetLightData();
            }

            var currentBuffer = _lightBuffers[_currentFrameIndex];
            currentBuffer.StageData(batch, lightData);

            // Update light count
            var lightCountData = new[] { _activeLights.Count };
            batch.StageToBuffer(
                lightCountData,
                _countLightUbo.Buffer, 
                0,
                (ulong)(sizeof(int) * lightCountData.Length)
            );

            // Update descriptor bindings
            GBuffer.Material.Bindings.Remove(_lightBindings[_currentLightBufferIndex]);
            _currentLightBufferIndex = (_currentLightBufferIndex + 1) % _lightBuffers.Length;
            GBuffer.Material.Bindings.Add(_lightBindings[_currentLightBufferIndex]);
        }

        private void UpdateTransformBuffer(UploadBatch batch)
        {
            var currentBuffer = _transformBuffers[_currentFrameIndex];
            var matrices = new Matrix4x4[_meshDrawCommands.Count];

            for (int i = 0; i < _meshDrawCommands.Count; i++)
            {
                matrices[i] = _meshDrawCommands[i].Mesh.Entity.Transform.GetModelMatrix();
            }

            currentBuffer.StageData(batch, matrices);

            // Update bindings for next frame
            var nextIndex = (_currentFrameIndex + 1) % _vulkanContext.MaxFramesPerFlight;
            foreach (var meshCmd in _meshDrawCommands)
            {
                meshCmd.Mesh.Material.Bindings.Remove(_transformBindings[nextIndex]);
                meshCmd.Mesh.Material.Bindings.Add(_transformBindings[_currentFrameIndex]);

                meshCmd.Mesh.Material.Bindings.Remove(_globalUboBinding);
                meshCmd.Mesh.Material.Bindings.Add(_globalUboBinding);
            }
        }

        private void UpdateIndirectCommands(UploadBatch batch)
        {
            _pipelineDrawGroups.Clear();
            var commands = new List<DrawIndexedIndirectCommand>();

            // Group by pipeline first, then mesh
            var pipelineGroups = _meshDrawCommands
                .GroupBy(c => c.Mesh.Material.GeometryPipeline)
                .OrderBy(g => g.Key);

            foreach (var pipelineGroup in pipelineGroups)
            {
                var meshGroups = pipelineGroup.GroupBy(c => c.Mesh);

                foreach (var meshGroup in meshGroups)
                {
                    ulong offset = (ulong)commands.Count * (ulong)Marshal.SizeOf<DrawIndexedIndirectCommand>();
                    uint count = 0;

                    foreach (var cmd in meshGroup)
                    {
                        commands.Add(new DrawIndexedIndirectCommand
                        {
                            IndexCount = (uint)cmd.Mesh.Indices.Length,
                            InstanceCount = 1,
                            FirstIndex = 0,
                            VertexOffset = 0,
                            FirstInstance = cmd.TransformIndex
                        });
                        count++;
                    }

                    _pipelineDrawGroups.Add((
                        pipelineGroup.Key,
                        meshGroup.Key,
                        count,
                        offset
                    ));
                }
            }

            _indirectBuffer.StageCommands(batch, commands.ToArray());
            _meshDrawCommands.Clear();
        }

        private Task StageGlobalUbo()
        {
            return _globalUbo.UpdateAsync();
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
                // assume that here call only goes from event of swapchain
                GBuffer.Recreate(swapchain);
                GBuffer.CreateLightingDescriptorSets(_deferredLightingPipeline);
            }
            else
            {
                _framebuffers = new VkFrameBuffer[swapChainImageViews.Length];
            }

            for (int i = 0; i < swapChainImageViews.Length; i++)
            {
                // Combine GBuffer and swapchain attachments
                var attachments = GBuffer.ColorAttachments
                    .Concat([GBuffer.DepthAttachment, swapChainImageViews[i]])
                    .ToArray();

                fixed (ImageView* attachmentsPtr = attachments.Select(s => s.VkObjectNative).ToArray())
                {
                    var framebufferInfo = new FramebufferCreateInfo
                    {
                        SType = StructureType.FramebufferCreateInfo,
                        RenderPass = RenderPass.RenderPass,
                        AttachmentCount = (uint)attachments.Length,
                        PAttachments = attachmentsPtr,
                        Width = swapChainExtent.Width,
                        Height = swapChainExtent.Height,
                        Layers = 1
                    };
                    var framebuffer = VkFrameBuffer.Create(_vulkanContext, in framebufferInfo, attachments);
                    _framebuffers[i] = framebuffer;
                }
            }
        }

        public void AddCommand(IRenderCommand imguiRenderCommand)
        {
            _otherRenderCommands.Enqueue(imguiRenderCommand);
        }
        private unsafe void CreateRenderPass()
        {
            var gBufferAttachments = new AttachmentDescription[GBuffer.ColorAttachments.Length + 1];

            // GBuffer color attachments
            for (int i = 0; i < GBuffer.ColorAttachments.Length; i++)
            {
                gBufferAttachments[i] = new AttachmentDescription
                {
                    Format = GBuffer.ColorAttachments[i].Image.Format,
                    Samples = SampleCountFlags.Count1Bit,
                    LoadOp = AttachmentLoadOp.Clear,
                    StoreOp = AttachmentStoreOp.Store,
                    StencilLoadOp = AttachmentLoadOp.DontCare,
                    StencilStoreOp = AttachmentStoreOp.DontCare,
                    InitialLayout = ImageLayout.Undefined,
                    FinalLayout = ImageLayout.ShaderReadOnlyOptimal
                };
            }

            // GBuffer depth attachment
            gBufferAttachments[GBuffer.ColorAttachments.Length] = new AttachmentDescription
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


            // Swapchain color attachment
            var swapchainColorAttachment = new AttachmentDescription
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

            // References for GBuffer attachments
            var gBufferColorAttachmentReferences = stackalloc AttachmentReference[GBuffer.ColorAttachments.Length];
            for (int i = 0; i < GBuffer.ColorAttachments.Length; i++)
            {
                gBufferColorAttachmentReferences[i] = new AttachmentReference { Attachment = (uint)i, Layout = ImageLayout.ColorAttachmentOptimal };
            }
            var gBufferDepthAttachmentReference = new AttachmentReference { Attachment = (uint)GBuffer.ColorAttachments.Length, Layout = ImageLayout.DepthStencilAttachmentOptimal };

            // Reference for swapchain color attachment
            var swapchainColorAttachmentReference = new AttachmentReference
            {
                Attachment = (uint)(GBuffer.ColorAttachments.Length + 1), // Index starts after GBuffer attachments
                Layout = ImageLayout.ColorAttachmentOptimal
            };


            // GBuffer Subpass
            var gBufferSubpass = new SubpassDescription
            {
                Flags = SubpassDescriptionFlags.None,
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = (uint)GBuffer.ColorAttachments.Length,
                PColorAttachments = gBufferColorAttachmentReferences,
                PDepthStencilAttachment = &gBufferDepthAttachmentReference
            };

            var inputAttachmentReferences = stackalloc AttachmentReference[GBuffer.ColorAttachments.Length];
            for (int i = 0; i < GBuffer.ColorAttachments.Length; i++)
            {
                inputAttachmentReferences[i] = new AttachmentReference
                {
                    Attachment = (uint)i,
                    Layout = ImageLayout.ShaderReadOnlyOptimal // Correct layout for input attachments
                };
            }

            // Lighting Subpass
            var lightingSubpass = new SubpassDescription
            {
                Flags = SubpassDescriptionFlags.None,
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = &swapchainColorAttachmentReference,
                InputAttachmentCount = (uint)GBuffer.ColorAttachments.Length,
                PInputAttachments = inputAttachmentReferences // Use GBuffer outputs as inputs
            };

            var imguiSubpass = new SubpassDescription
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = &swapchainColorAttachmentReference // Use swapchain attachment
            };

            // Subpass Dependencies
            var dependencies = new SubpassDependency[]
            {
                // GBuffer to Lighting
                new SubpassDependency
                {
                    SrcSubpass = 0, // GBuffer subpass
                    DstSubpass = 1, // Lighting subpass
                    SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                    SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
                    DstStageMask = PipelineStageFlags.FragmentShaderBit,
                    DstAccessMask = AccessFlags.ShaderReadBit
                },
                // External to GBuffer 
                new SubpassDependency
                {
                    SrcSubpass = Vk.SubpassExternal,
                    DstSubpass = 0,
                    SrcStageMask = PipelineStageFlags.BottomOfPipeBit,
                    DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                    SrcAccessMask = AccessFlags.MemoryReadBit,
                    DstAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit,
                    DependencyFlags = DependencyFlags.ByRegionBit
                },
                // Lighting  to External
                new SubpassDependency
                {
                    SrcSubpass = 1,
                    DstSubpass = Vk.SubpassExternal,
                    SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                    DstStageMask = PipelineStageFlags.BottomOfPipeBit,
                    SrcAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit,
                    DstAccessMask = AccessFlags.MemoryReadBit,
                    DependencyFlags = DependencyFlags.ByRegionBit
                },
                // Transition from Lighting to ImGui
                new SubpassDependency
                {
                    SrcSubpass = 1,
                    DstSubpass = 2,
                    SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                    DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                    SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
                    DstAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit,
                    DependencyFlags = DependencyFlags.ByRegionBit
                }
            };

            // Combine attachments
            var allAttachments = gBufferAttachments.Concat([swapchainColorAttachment]).ToArray();
            var subPasses = new[] { gBufferSubpass, lightingSubpass, imguiSubpass };
            fixed (SubpassDescription* pSubpasses = subPasses)
            fixed (SubpassDependency* pDependencies = dependencies)
            fixed (AttachmentDescription* pAttachments = allAttachments)
            {
                // Create Render Pass
                var renderPassInfo = new RenderPassCreateInfo
                {
                    SType = StructureType.RenderPassCreateInfo,
                    AttachmentCount = (uint)allAttachments.Length,
                    PAttachments = pAttachments,
                    SubpassCount = (uint)subPasses.Length, // GBuffer + Lighting + IMGUI
                    PSubpasses = pSubpasses,
                    DependencyCount = (uint)dependencies.Length,
                    PDependencies = pDependencies
                };
                RenderPass = _graphicsEngine.RenderPassManager.CreateRenderPassFromInfo(in renderPassInfo);
            }
        }

        public void Dispose()
        {
            _globalUbo.Dispose();
        }

      
    }
}
