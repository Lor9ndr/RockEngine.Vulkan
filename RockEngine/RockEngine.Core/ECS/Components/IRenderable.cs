using RockEngine.Core.Rendering;
using RockEngine.Vulkan;

namespace RockEngine.Core.ECS.Components
{
    public interface IRenderable
    {
        ValueTask Init(RenderingContext context, Renderer renderer);

        ValueTask Render(Renderer renderer);
    }
}
