using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Texturing;

namespace RockEngine.Core.ECS.Components
{
    public class Skybox : Component
    {
        public Texture Cubemap { get; set; }
        public Material Material { get; private set;}

        public override ValueTask OnStart(Renderer renderer)
        {
            Material = new Material(renderer.PipelineManager.GetPipelineByName("Skybox"), Cubemap);
            return base.OnStart(renderer);
        }
    }
}
