using MessagePack;

using RockEngine.Assets;
using RockEngine.Core.Assets;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Passes.SubPasses;

namespace RockEngine.Core.ECS.Components
{
    [MessagePackObject]
    public partial class Skybox : Component
    {
        public Skybox()
        {
        }

        [Key(7)]
        public AssetReference<TextureAsset> Cubemap { get; set; }

        public override async ValueTask OnStart(WorldRenderer renderer)
        {
            var assetFactory = IoC.Container.GetInstance<AssetFactory>();
            var assetManager = IoC.Container.GetInstance<IAssetManager>();


            var tmpAsset = assetFactory.Create<MeshAsset>(new AssetPath("tmp", "tmpMesh"));
            var tmpMatAsset = assetFactory.Create<MaterialAsset>(new AssetPath("tmp", "tmpMeshMat"));
            var texAsset = await Cubemap.GetAssetAsync();
            await texAsset.LoadDataAsync();
            await assetManager.SaveAsync(texAsset);
            tmpMatAsset.SetData(new MaterialData()
            {
                PipelineName = "Skybox",
                Textures = [Cubemap]
            });
            tmpAsset.SetGeometry(DefaultMeshes.Cube.Vertices, DefaultMeshes.Cube.Indices);
           // await assetManager.SaveAsync(tmpAsset);
           // await assetManager.SaveAsync(tmpMatAsset);
            var mesh = Entity.AddComponent<MeshRenderer>();
            mesh.SetProviders(tmpAsset, tmpMatAsset);

            var lightingPass = renderer.RenderPass.SubPasses.OfType<LightingPass>().FirstOrDefault();
            if (lightingPass == null)
            {
                return;
            }
            await Cubemap.Asset.LoadGpuResourcesAsync().ConfigureAwait(false);
            // Ожидаем генерацию всех IBL текстур
            var irradiance = await renderer.IBLManager.GenerateIrradianceMap(Cubemap.Asset.Texture, 128);
            var prefilter = await renderer.IBLManager.GeneratePrefilterMap(Cubemap.Asset.Texture, 512);
            var brdfLUT = await renderer.IBLManager.GenerateBRDFLUT(512);

            irradiance.Image.LabelObject("Irradiance");
            prefilter.Image.LabelObject("Prefilter");
            brdfLUT.Image.LabelObject("BRDFLut");

            // Store references in lighting pass
            lightingPass.SetIBLTextures(irradiance, prefilter, brdfLUT);
        }

        public override ValueTask Update(WorldRenderer renderer)
        {
            Entity.Transform.Scale = new System.Numerics.Vector3(10000, 10000, 10000);

            return default;
        }
    }
}
