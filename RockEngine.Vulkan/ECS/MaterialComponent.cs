using RockEngine.Vulkan.Rendering;
using RockEngine.Vulkan.Rendering.ComponentRenderers;
using RockEngine.Vulkan.Rendering.MaterialRendering;
using RockEngine.Vulkan.VkObjects;

namespace RockEngine.Vulkan.ECS
{
    /// <summary>
    /// Represents a material component that can be rendered in the Vulkan engine.
    /// Implements the <see cref="IRenderableComponent{T}"/> interface for rendering capabilities.
    /// </summary>
    public class MaterialComponent : Component, IRenderableComponent<MaterialComponent>
    {
        public Material Material;
        /// <summary>
        /// The renderer responsible for rendering this material component.
        /// </summary>
        private readonly IComponentRenderer<MaterialComponent> _renderer;

        public MaterialComponent(IComponentRenderer<MaterialComponent> renderer)
        {
            _renderer = renderer;
        }

        /// <summary>
        /// Gets the rendering order of this material component.
        /// </summary>
        public int Order => -10000000;

        /// <summary>
        /// Gets the renderer responsible for rendering this material component.
        /// </summary>
        public IComponentRenderer<MaterialComponent> Renderer => _renderer;

        /// <summary>
        /// Asynchronously initializes the material component.
        /// Ensures that the texture is not null and retrieves the appropriate renderer.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public override async Task OnInitializedAsync()
        {
            await _renderer.InitializeAsync(this)
                .ConfigureAwait(false); // Initializes the renderer asynchronously.
            IsInitialized = true; // Marks the component as initialized.
        }

        /// <summary>
        /// Renders the material component using the provided command buffer.
        /// </summary>
        /// <param name="frameInfo">The frameInfo used for rendering.</param>
        /// <returns>A task representing the asynchronous rendering operation.</returns>
        public Task RenderAsync(FrameInfo frameInfo)
        {
            return _renderer.RenderAsync(this, frameInfo); // Delegates the rendering to the renderer.
        }
    }
}
