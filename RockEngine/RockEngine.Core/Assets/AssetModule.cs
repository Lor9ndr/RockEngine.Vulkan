using Newtonsoft.Json;

using RockEngine.Core.Assets.Converters;
using RockEngine.Core.Assets.Serializers;
using RockEngine.Core.DI;

using SimpleInjector;

namespace RockEngine.Core.Assets
{
    public class AssetModule : IDependencyModule
    {
        public void RegisterDependencies(Container container)
        {
            container.Register<IAssetSerializer, OptimizedJsonAssetSerializer>(Lifestyle.Singleton);
            container.Register<AssetManager>(Lifestyle.Singleton);

            container.Collection.Append<JsonConverter, VertexConverter>(Lifestyle.Singleton);
            container.Collection.Append<JsonConverter, AssetPathConverter>(Lifestyle.Singleton);
            container.Collection.Append<JsonConverter, AssetReferenceConverter>(Lifestyle.Singleton);
            container.Collection.Append<JsonConverter, MaterialResourceProviderConverter>(Lifestyle.Singleton);
            container.Collection.Append<JsonConverter, MeshResourceProviderConverter>(Lifestyle.Singleton);
            container.Collection.Append<JsonConverter, RenderLayerMaskConverter>(Lifestyle.Singleton);

            container.Register<MeshAsset>(Lifestyle.Transient);
            container.Register<TextureAsset>(Lifestyle.Transient);
            container.Register<ProjectAsset>(Lifestyle.Transient);
            container.Register<MaterialAsset>(Lifestyle.Transient);
            container.Register<ModelAsset>(Lifestyle.Transient);
            container.Register<SceneAsset>(Lifestyle.Transient);

        }
    }
}
