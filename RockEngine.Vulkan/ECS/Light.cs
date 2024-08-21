using RockEngine.Vulkan.Rendering.ComponentRenderers;
using RockEngine.Vulkan.Rendering;
using System.Numerics;

namespace RockEngine.Vulkan.ECS
{
    public class LightComponent : Component, IRenderableComponent<LightComponent>
    {
        public Vector3 Color { get; set; }
        public float Intensity { get; set; }
        public LightType Type { get; set; }

        private readonly IComponentRenderer<LightComponent> _renderer;

        public LightComponent(IComponentRenderer<LightComponent> renderer)
        {
            _renderer = renderer;
            Color = Vector3.One;
            Intensity = 1.0f;
            Type = LightType.Point;
        }

        public int Order => 9999;

        public IComponentRenderer<LightComponent> Renderer => _renderer;

        public override async Task OnInitializedAsync()
        {
            await _renderer.InitializeAsync(this).ConfigureAwait(false);
            IsInitialized = true;
        }

        public ValueTask RenderAsync(FrameInfo frameInfo)
        {
            return _renderer.RenderAsync(this, frameInfo);
        }

        public override ValueTask UpdateAsync(double time)
        {
            return _renderer.UpdateAsync(this);
        }

        public enum LightType
        {
            Directional,
            Point,
            Spot
        }
    }
}
