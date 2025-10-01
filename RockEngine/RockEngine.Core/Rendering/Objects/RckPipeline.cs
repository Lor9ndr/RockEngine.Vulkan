using RockEngine.Core.Rendering.Passes;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Objects
{
    public enum PipelineType
    {
        Graphics,
        Compute
    }

    public class RckPipeline : IDisposable
    {
        public VkPipeline VkPipeline { get; }
        public string Name { get; }
        public PipelineType Type { get; }
        public RckRenderPass? RenderPass { get; } // Null for compute pipelines
        public SubPassMetadata SubpassMetadata { get; } // Default for compute pipelines
        public VkPipelineLayout Layout { get; }
        private bool _disposed = false;

        // Graphics pipeline constructor
        public RckPipeline(VkPipeline pipeline, string name, RckRenderPass renderPass, SubPassMetadata subpassMetadata, VkPipelineLayout layout)
        {
            VkPipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Type = PipelineType.Graphics;
            RenderPass = renderPass ?? throw new ArgumentNullException(nameof(renderPass));
            SubpassMetadata = subpassMetadata;
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        }

        // Compute pipeline constructor
        public RckPipeline(VkPipeline pipeline, string name, VkPipelineLayout layout)
        {
            VkPipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Type = PipelineType.Compute;
            RenderPass = null;
            SubpassMetadata = default;
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        }

        public uint SubpassIndex => Type == PipelineType.Graphics ? SubpassMetadata.Order : 0;
        public string SubpassName => Type == PipelineType.Graphics ? SubpassMetadata.Name : "compute";

        public void Bind(VkCommandBuffer commandBuffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var bindPoint = Type == PipelineType.Graphics
                ? PipelineBindPoint.Graphics
                : PipelineBindPoint.Compute;

            commandBuffer.BindPipeline(VkPipeline, bindPoint);
        }

        public void Dispatch(VkCommandBuffer commandBuffer, uint groupCountX, uint groupCountY, uint groupCountZ)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (Type != PipelineType.Compute)
                throw new InvalidOperationException("Dispatch can only be called on compute pipelines");

            commandBuffer.Dispatch(groupCountX, groupCountY, groupCountZ);
        }

        public void Dispose()
        {
            if (_disposed) return;

            VkPipeline?.Dispose();
            _disposed = true;
        }

        public static implicit operator VkPipeline(RckPipeline rckPipeline) => rckPipeline.VkPipeline;
    }
}