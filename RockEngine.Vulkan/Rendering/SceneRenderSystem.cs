using RockEngine.Vulkan.ECS;
using RockEngine.Vulkan.VulkanInitilizers;

using System.Diagnostics;

namespace RockEngine.Vulkan.Rendering
{
    public class SceneRenderSystem : RenderSystem
    {
        public SceneRenderSystem(VulkanContext context)
            :base(context)
        {
        }

        public override async Task RenderAsync(Project p, FrameInfo frameInfo)
        {
            Debug.Assert(frameInfo.CommandBuffer?.VkObjectNative.Handle != default, "Command buffer is null");

            var groups = p.CurrentScene.GetEntities().GroupBy(s=> s.GetComponent<MeshComponent>()?.Material.Original);
            foreach (var group in groups)
            {
                foreach (var item in group)
                {
                    await item.RenderAsync(frameInfo);
                }
            }
        }

        public override void Dispose()
        {
        }
    }
}