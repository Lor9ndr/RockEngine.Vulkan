using RockEngine.Core.Rendering.Commands;
using RockEngine.Core.Rendering.RenderTargets;

namespace RockEngine.Core.Rendering
{
    public class RenderLayer
    {
        public RenderLayerType Type { get; }
        public IRenderTarget Target { get; }
        public List<IRenderCommand> Commands { get; } = new List<IRenderCommand>();

        public RenderLayer(RenderLayerType type, IRenderTarget target)
        {
            Type = type;
            Target = target;
        }
    }

}
