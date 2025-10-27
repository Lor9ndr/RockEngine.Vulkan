using RockEngine.Core.ECS;
using RockEngine.Core.Rendering;
using RockEngine.Editor.EditorComponents;
using RockEngine.Vulkan;

namespace RockEngine.Editor.Layers
{
    internal class DebugCamLayer : ILayer
    {
        private readonly World _world;
        private bool _isAttached;

        public DebugCamLayer(World world)
        {
            _world = world;
        }

        public Task OnAttach()
        {
            if (_isAttached)
            {
                return Task.CompletedTask;
            }
            var cam = _world.CreateEntity();
            var debugCam = cam.AddComponent<DebugCamera>();
            cam.Transform.Position = new System.Numerics.Vector3(0,10, 0);
            _isAttached = true;

            return Task.CompletedTask;
        }

        public void OnDetach()
        {
        }

        public Task OnImGuiRender(VkCommandBuffer vkCommandBuffer)
        {
            return Task.CompletedTask;
        }

        public void OnRender(VkCommandBuffer vkCommandBuffer)
        {
        }

        public void OnUpdate()
        {
        }
    }
}
