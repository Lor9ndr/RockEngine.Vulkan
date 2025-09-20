using RockEngine.Core.Assets;
using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;

namespace RockEngine.Core.ECS.Components
{
    public class Skybox : Component
    {
        public AssetReference<TextureAsset> Cubemap { get; set; }

        public override async ValueTask OnStart(Renderer renderer)
        {
            var assetManager = IoC.Container.GetInstance<AssetManager>();

            Entity.Layer = RenderLayerType.Solid;

            var tmpAsset = assetManager.Create<MeshAsset>(new AssetPath("tmp", "tmpMesh"));
            var tmpMatAsset = assetManager.Create<MaterialAsset>(new AssetPath("tmp", "tmpMeshMat"));
            tmpMatAsset.SetData(new MaterialData()
            {
                PipelineName = "Skybox",
                TextureAssetIDs = [Cubemap]
            });
            tmpAsset.SetGeometry(DefaultMeshes.Cube.Vertices, DefaultMeshes.Cube.Indices);
            await assetManager.SaveAsync(tmpAsset);
            await assetManager.SaveAsync(tmpMatAsset);
            await assetManager.SaveAsync(Cubemap.Asset);
            var mesh = Entity.AddComponent<MeshRenderer>();
            mesh.SetAssets(tmpAsset, tmpMatAsset);
        }

        public override ValueTask Update(Renderer renderer)
        {
            Entity.Transform.Scale = new System.Numerics.Vector3(10000, 10000, 10000);

            return default;
        }
    }
}
