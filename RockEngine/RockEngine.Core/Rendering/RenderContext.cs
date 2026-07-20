using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering
{
    public class RenderContext
    {
        public UploadBatch GraphicsBatch { get; }
        public SubmitContext GraphicsContext { get; }
        public SubmitContext TransferContext { get; }
        public SubmitContext ComputeContext { get; }
        public WorldRenderer WorldRenderer { get; }
        public uint FrameIndex { get; }

        public RenderContext(uint frameIndex, SubmitContext graphicsContext, SubmitContext transferContext, SubmitContext computeContext, WorldRenderer worldRenderer, UploadBatch graphicsBatch)
        {
            FrameIndex = frameIndex;
            GraphicsContext = graphicsContext;
            TransferContext = transferContext;
            ComputeContext = computeContext;
            WorldRenderer = worldRenderer;
            GraphicsBatch = graphicsBatch;
        }
    }
}
