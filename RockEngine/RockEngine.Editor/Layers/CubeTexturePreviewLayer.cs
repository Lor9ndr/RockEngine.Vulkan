using RockEngine.Core;
using RockEngine.Core.Assets;
using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;
using RockEngine.Vulkan;

namespace RockEngine.Editor.Layers
{
    internal class CubeTexturePreviewLayer : ILayer
    {
        private readonly World _world;
        private readonly AssetManager _assetManager;

        public CubeTexturePreviewLayer(World world, AssetManager assetManager)
        {
            _world = world;
            _assetManager = assetManager;
        }

        public Task OnAttach()
        {
           /* var cubeEntity = _world.CreateEntity();
            var mr = cubeEntity.AddComponent<MeshRenderer>();
            var meshAsset = _assetManager.GetAsset<MeshAsset>(DefaultMeshes.CubeAssetID);
            var matAsset = _assetManager.Create<MaterialAsset>("")
            mr.SetAssets(meshAsset, )*/
           return Task.CompletedTask;

        }

        public void OnDetach()
        {
        }

        public void OnImGuiRender(VkCommandBuffer vkCommandBuffer)
        {
        }

        public void OnRender(VkCommandBuffer vkCommandBuffer)
        {
        }

        public void OnUpdate()
        {
        }
    }
}
