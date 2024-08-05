using RockEngine.Vulkan.DI;
using RockEngine.Vulkan.Rendering.ComponentRenderers;
using RockEngine.Vulkan.VkObjects;

namespace RockEngine.Vulkan.ECS
{
    /// <summary>
    /// Represents a material component that can be rendered in the Vulkan engine.
    /// Implements the <see cref="IRenderableComponent{T}"/> interface for rendering capabilities.
    /// </summary>
    public class MaterialComponent : Component, IRenderableComponent<MaterialComponent>, IDisposable
    {

        /// <summary>
        /// The renderer responsible for rendering this material component.
        /// </summary>
        private IComponentRenderer<MaterialComponent> _renderer;

        /// <summary>
        /// The texture associated with this material component.
        /// </summary>
        private Texture _texture;

        /// <summary>
        /// Gets the rendering order of this material component.
        /// </summary>
        public int Order => 0;

        /// <summary>
        /// Gets the renderer responsible for rendering this material component.
        /// </summary>
        public IComponentRenderer<MaterialComponent> Renderer => _renderer;

        /// <summary>
        /// Gets the texture associated with this material component.
        /// </summary>
        public Texture Texture => _texture;

        /// <summary>
        /// Sets the texture for this material component.
        /// </summary>
        /// <param name="texture">The texture to be set for this material component.</param>
        public void SetTexture(Texture texture) => _texture = texture;

        /// <summary>
        /// Asynchronously initializes the material component.
        /// Ensures that the texture is not null and retrieves the appropriate renderer.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public override async Task OnInitializedAsync()
        {
            ArgumentNullException.ThrowIfNull(Texture); // Throws an exception if the texture is null.

            _renderer = IoC.Container.GetRenderer<MaterialComponent>(); // Retrieves the renderer from the IoC container.
            await _renderer.InitializeAsync(this).ConfigureAwait(false); // Initializes the renderer asynchronously.
            IsInitialized = true; // Marks the component as initialized.
        }

        /// <summary>
        /// Renders the material component using the provided command buffer.
        /// </summary>
        /// <param name="commandBuffer">The command buffer used for rendering.</param>
        /// <returns>A task representing the asynchronous rendering operation.</returns>
        public Task RenderAsync(CommandBufferWrapper commandBuffer)
        {
            return _renderer.RenderAsync(this, commandBuffer); // Delegates the rendering to the renderer.
        }

        /// <summary>
        /// Disposes of the resources used by this material component.
        /// Cleans up the texture if it exists.
        /// </summary>
        public void Dispose()
        {
            _texture?.Dispose(); // Disposes of the texture if it is not null.
        }
    }
}
