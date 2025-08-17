using Newtonsoft.Json;

using RockEngine.Core.Assets.Converters;
using RockEngine.Core.Assets.Serializers;
using RockEngine.Core.DI;

using SimpleInjector;

using System.Reflection;

namespace RockEngine.Core.Assets
{
    public class AssetModule : IDependencyModule
    {
        public void RegisterDependencies(Container container)
        {
            var factoryRegistry = new AssetFactoryRegistry();

            container.RegisterInstance(factoryRegistry);
            container.Register<IAssetSerializer, JsonAssetSerializer>(Lifestyle.Singleton);
            container.Register<AssetManager>(Lifestyle.Singleton);
            container.Collection.Append<JsonConverter, VertexConverter>(Lifestyle.Singleton);
            container.Collection.Append<JsonConverter, AssetPathConverter>(Lifestyle.Singleton);
            container.Register<MeshAsset>(Lifestyle.Transient);
            container.Register<TextureAsset>(Lifestyle.Transient);
            container.Register<ProjectAsset>(Lifestyle.Transient);
            container.Register<MaterialAsset>(Lifestyle.Transient);
            container.Register<ModelAsset>(Lifestyle.Transient);

        }
    }
}
