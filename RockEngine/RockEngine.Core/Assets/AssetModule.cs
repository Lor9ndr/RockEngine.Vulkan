
using RockEngine.Core.Assets.Converters;
using RockEngine.Core.Assets.Serializers;
using RockEngine.Core.DI;

using SimpleInjector;

using System.Text.Json.Serialization;

namespace RockEngine.Core.Assets
{
    public class AssetModule : IDependencyModule
    {
        public void RegisterDependencies(Container container)
        {
            container.Register<IAssetSerializer, SystemTextJsonAssetSerializer>(Lifestyle.Singleton);
            container.Register<AssetManager>(Lifestyle.Singleton);
            container.Register<IAssetFactory, AssetFactory>(Lifestyle.Singleton);
            container.Register<IAssetRepository, AssetRepository>(Lifestyle.Singleton);

            container.Collection.Append<JsonConverter, VertexConverter2>(Lifestyle.Singleton);
            container.Collection.Append<JsonConverter, AssetPathConverter2>(Lifestyle.Singleton);
            container.Collection.Append<JsonConverter, AssetReferenceConverter2>(Lifestyle.Singleton);
            container.Collection.Append<JsonConverter, MaterialResourceProviderConverter2>(Lifestyle.Singleton);
            container.Collection.Append<JsonConverter, MeshResourceProviderConverter2>(Lifestyle.Singleton);
            container.Collection.Append<JsonConverter, RenderLayerMaskConverter2>(Lifestyle.Singleton);
            container.Collection.Append<JsonConverter, TypeConverter>(Lifestyle.Singleton);
            //container.Collection.Append<JsonConverter, TypeHandlingConverter>(Lifestyle.Singleton);
            //container.Collection.Append<JsonConverter, AssetWrapperConverter>(Lifestyle.Singleton);

            container.Register<MeshAsset>(Lifestyle.Transient);
            container.Register<TextureAsset>(Lifestyle.Transient);
            container.Register<ProjectAsset>(Lifestyle.Transient);
            container.Register<MaterialAsset>(Lifestyle.Transient);
            container.Register<ModelAsset>(Lifestyle.Transient);
            container.Register<SceneAsset>(Lifestyle.Transient);

        }
    }
}
