// RenderContext.cs
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;

namespace RockEngine.Core.Rendering.Contexts
{
    public sealed class RenderContext
    {
        private readonly GraphicsEngine _graphicsEngine;
        private readonly VulkanContext _context;
        private readonly BindingManager _bindingManager;
        private readonly StateTracker _state = new StateTracker();
        private VkCommandBuffer? _cmdBuffer;

        public VkCommandBuffer? CommandBuffer => _cmdBuffer;

        internal RenderContext(GraphicsEngine graphicsEngine, VulkanContext context, BindingManager bindingManager)
        {
            _graphicsEngine = graphicsEngine;
            _context = context;
            _bindingManager = bindingManager;
        }

        public void BeginFrame(VkCommandBuffer commandBuffer, EngineRenderPass renderPass, VkFrameBuffer framebuffer, ClearValue[] clearValues)
        {
            _cmdBuffer = commandBuffer;
            _state.Reset();
            BeginRenderPass(renderPass, framebuffer, clearValues);
        }

        private unsafe void BeginRenderPass(EngineRenderPass renderPass, VkFrameBuffer framebuffer, ClearValue[] clearValues)
        {
            ArgumentNullException.ThrowIfNull(_cmdBuffer);
            fixed (ClearValue* clearValuesPtr = clearValues)
            {
                var passBeginInfo = new RenderPassBeginInfo
                {
                    SType = StructureType.RenderPassBeginInfo,
                    RenderPass = renderPass.RenderPass,
                    Framebuffer = framebuffer,
                    ClearValueCount = (uint)clearValues.Length,
                    PClearValues = clearValuesPtr,
                    RenderArea = new Rect2D { Extent =_graphicsEngine.Swapchain.Extent }
                };
                _cmdBuffer.BeginRenderPass(in passBeginInfo, SubpassContents.Inline);
            }
        }

        public void BindMaterial(Material material)
        {
            ArgumentNullException.ThrowIfNull(_cmdBuffer);
            if (_state.CurrentMaterial == material) return;

            _bindingManager.BindResourcesForMaterial(material, _cmdBuffer);
            _state.CurrentMaterial = material;
        }

        public void DrawMesh(Mesh mesh)
        {
            ArgumentNullException.ThrowIfNull(_cmdBuffer);
            mesh.VertexBuffer.BindVertexBuffer(_cmdBuffer);

            if (mesh.HasIndices)
            {
                mesh.IndexBuffer!.BindIndexBuffer(_cmdBuffer,0, IndexType.Uint32);
                _cmdBuffer.DrawIndexed((uint)mesh.Indices!.Length, 1, 0, 0, 0);
            }
            else
            {
                _cmdBuffer.Draw((uint)mesh.Vertices.Length, 1, 0, 0);
            }
        }

        public void DrawIndirect(IndirectBuffer indirectBuffer, uint drawCount)
        {
            ArgumentNullException.ThrowIfNull(_cmdBuffer);
            _cmdBuffer.DrawIndirect(
                indirectBuffer.Buffer,
                drawCount,
                (uint)Unsafe.SizeOf<DrawIndexedIndirectCommand>()
            );
        }

        public void EndFrame()
        {
            _cmdBuffer = null;
        }


        private class StateTracker
        {
            public Material? CurrentMaterial;
            public VkPipeline? CurrentPipeline;

            public void Reset()
            {
                CurrentMaterial = null;
                CurrentPipeline = null;
            }
        }
    }
}