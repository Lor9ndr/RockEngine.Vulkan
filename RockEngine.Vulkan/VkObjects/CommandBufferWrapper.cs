﻿using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

using SkiaSharp;

using System.Diagnostics;

namespace RockEngine.Vulkan.VkObjects
{
    public class CommandBufferWrapper : VkObject<CommandBuffer>, IBegginable<CommandBufferBeginInfo>
    {
        private readonly VulkanContext _context;
        private readonly CommandPoolWrapper _commandPool;

        public CommandBufferWrapper(VulkanContext context, in CommandBuffer commandBuffer, CommandPoolWrapper commandPool)
            :base(in commandBuffer)
        {
            _context = context;
            _commandPool = commandPool;
        }

        public void Begin(CommandBufferBeginInfo beginInfo)
        {
            _context.Api.BeginCommandBuffer(_vkObject, ref beginInfo)
                .ThrowCode("Failed to begin command buffer!");
        }

        public void End()
        {
            _context.Api.EndCommandBuffer(_vkObject)
                .ThrowCode("Failed to end command buffer!");
        }

        public void CopyBuffer(BufferWrapper srcBuffer, BufferWrapper dstBuffer, ulong size)
        {
            BufferCopy bufferCopy = new BufferCopy()
            {
                SrcOffset = 0,
                DstOffset = 0,
                Size = size
            };
            _context.Api.CmdCopyBuffer(_vkObject, srcBuffer, dstBuffer, 1, ref bufferCopy);
        }

        public void SetViewport(Viewport viewport)
        {
            _context.Api.CmdSetViewport(_vkObject, 0, 1, ref viewport);
        }

        public void SetScissor(Rect2D scissor)
        {
            _context.Api.CmdSetScissor(_vkObject, 0, 1, ref scissor);
        }

        public void BindPipeline(PipelineBindPoint pipelineBindPoint, Pipeline pipeline)
        {
            _context.Api.CmdBindPipeline(_vkObject, pipelineBindPoint, pipeline);
        }

        public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
        {
            _context.Api.CmdDraw(_vkObject, vertexCount, instanceCount, firstVertex, firstInstance);
        }

        public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
        {
            _context.Api.CmdDrawIndexed(_vkObject, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        }

        public void BindVertexBuffer(BufferWrapper vertexBuffer, ulong offset = 0)
        {
            var buffer = vertexBuffer.VkObjectNative;
            _context.Api.CmdBindVertexBuffers(_vkObject, 0, 1, ref buffer, ref offset);
        }

        public void BindIndexBuffer(BufferWrapper indexBuffer, ulong offset = 0, IndexType indexType = IndexType.Uint32)
        {
            _context.Api.CmdBindIndexBuffer(_vkObject, indexBuffer, offset, indexType);
        }

        public static CommandBufferWrapper Create(VulkanContext context, in CommandBufferAllocateInfo ai, CommandPoolWrapper commandPool)
        {
            context.Api.AllocateCommandBuffers(context.Device, in ai, out CommandBuffer cb)
                .ThrowCode("Failed to allocate command buffer");
            Debugger.Log(1, "Allocation", $"Allocated a command buffer with handle: {cb.Handle}");

            return new CommandBufferWrapper(context, in cb, commandPool);
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                // Dispose managed state (managed objects).
            }
            //_context.Api.FreeCommandBuffers(_context.Device, _commandPool, 1, in _vkObject);
/*            _vkObject = default;
            _disposed = true;*/
        }
    }
}