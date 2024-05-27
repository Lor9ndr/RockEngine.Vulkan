using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.ECS
{
    public class MeshComponent : Component, IRenderableComponent, IDisposable
    {
        private readonly RenderableObject _renderableObject;
        public MeshComponent(Vertex[] vertices, uint[]? indices = null)
        {
            _renderableObject = new RenderableObject(vertices,indices);
        }
       

        public override async Task OnInitializedAsync(VulkanContext context)
        {
            await _renderableObject.CreateBuffersAsync(context).ConfigureAwait(false);
        }

        public void Render(VulkanContext context, CommandBufferWrapper commandBuffer)
        {
            _renderableObject.Draw(context, commandBuffer);
        }

        public override Task UpdateAsync(double time, VulkanContext context, CommandBufferWrapper commandBuffer)
        {
            return Task.CompletedTask;
        }
        public void Dispose()
        {
            _renderableObject.Dispose();
        }
    }
}
