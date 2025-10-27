using RockEngine.Core.Assets;
using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Passes.SubPasses;

namespace RockEngine.Core.ECS.Components
{
    public partial class Skybox : Component
    {
        public AssetReference<TextureAsset> Cubemap { get; set; }

        public override async ValueTask OnStart(Renderer renderer)
        {
            var assetManager = IoC.Container.GetInstance<AssetManager>();


            var tmpAsset = assetManager.Create<MeshAsset>(new AssetPath("tmp", "tmpMesh"));
            var tmpMatAsset = assetManager.Create<MaterialAsset>(new AssetPath("tmp", "tmpMeshMat"));
            await assetManager.SaveAsync(await Cubemap.GetAssetAsync());
            tmpMatAsset.SetData(new MaterialData()
            {
                PipelineName = "Skybox",
                Textures = [Cubemap]
            });
            tmpAsset.SetGeometry(DefaultMeshes.Cube.Vertices, DefaultMeshes.Cube.Indices);
            await assetManager.SaveAsync(tmpAsset);
            await assetManager.SaveAsync(tmpMatAsset);
            var mesh = Entity.AddComponent<MeshRenderer>();
            mesh.SetProviders(tmpAsset, tmpMatAsset);

            var lightingPass = renderer.RenderPass.SubPasses.OfType<LightingPass>().FirstOrDefault();
            if (lightingPass == null)
            {
                return;
            }

            await Cubemap.Asset.LoadGpuResourcesAsync().ConfigureAwait(false);
            // Ожидаем генерацию всех IBL текстур
            var textures = await Task.WhenAll(
                renderer.IBLManager.GenerateIrradianceMap(Cubemap.Asset.Texture, 128),
                renderer.IBLManager.GeneratePrefilterMap(Cubemap.Asset.Texture, 512),
                renderer.IBLManager.GenerateBRDFLUT(512)
            ).ConfigureAwait(false);


            var irradiance = textures[0];
            var prefilter = textures[1];
            var brdfLUT = textures[2];

            irradiance.Image.LabelObject("Irradiance");
            prefilter.Image.LabelObject("Prefilter");
            brdfLUT.Image.LabelObject("BRDFLut");

            // Store references in lighting pass
            lightingPass.SetIBLTextures(irradiance, prefilter, brdfLUT);
        }

        public override ValueTask Update(Renderer renderer)
        {
            Entity.Transform.Scale = new System.Numerics.Vector3(10000, 10000, 10000);

            return default;
        }
    }
}
