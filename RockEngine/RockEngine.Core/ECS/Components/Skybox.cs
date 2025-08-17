using RockEngine.Core.Assets;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;

namespace RockEngine.Core.ECS.Components
{
    public class Skybox : Component
    {
        public TextureAsset Cubemap { get; set; }

        public override async ValueTask OnStart(Renderer renderer)
        {
            var mesh = Entity.AddComponent<MeshRenderer>();
            var assetManager = IoC.Container.GetInstance<AssetManager>();


            Entity.Layer = RenderLayerType.Solid;
            mesh.SetEntity(this.Entity);

            var tmpAsset = assetManager.Create<MeshAsset>(new AssetPath("tmp", "tmpMesh"));
            var tmpMatAsset = assetManager.Create<MaterialAsset>(new AssetPath("tmp", "tmpMesh"));
            tmpMatAsset.SetData(new MaterialData()
            {
                PipelineName = "Skybox",
                TextureAssetIDs = [Cubemap.ID]
            });
            tmpAsset.SetGeometry(DefaultMeshes.Cube.Vertices, DefaultMeshes.Cube.Indices);
            mesh.SetAssets(tmpAsset, tmpMatAsset);
        }
        public override ValueTask Update(Renderer renderer)
        {
            Entity.Transform.Scale = new System.Numerics.Vector3(10000, 10000, 10000);

            return default;
        }
    }
}
