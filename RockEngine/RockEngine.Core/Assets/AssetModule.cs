using Microsoft.Extensions.Options;

using RockEngine.Assets;
using RockEngine.Core.DI;

using SimpleInjector;

using System.Text;

using YamlDotNet.Serialization;

namespace RockEngine.Core.Assets
{
    public class AssetModule : IDependencyModule
    {
        public void RegisterDependencies(Container container)
        {
            // Register options
            container.Register<AssetManagerOptions>(() =>
                new AssetManagerOptions
                {
                    MaxConcurrentLoads = 4,
                    MaxCacheSize = 1024 * 1024 * 100, // 100MB
                    EnableAssetCaching = true,
                    CacheDuration = TimeSpan.FromMinutes(10),
                }, Lifestyle.Singleton);

            container.RegisterSingleton<AssimpLoader>();

            // Register serializers
            container.Register<IYamlSerializer, YamlDotNetSerializer>(Lifestyle.Singleton);
            container.Register<IBinarySerializer, MessagePackBinarySerializer>(Lifestyle.Singleton);


            // Register composite serializer (main implementation)
            container.Register<IAssetSerializer, CompositeAssetSerializer>();
            //container.Collection.Append<IAssetSerializationStrategy, YamlAssetSerializationStrategy>();
            container.Collection.Append<IAssetSerializationStrategy, BinaryAssetSerializationStrategy>();

            // Register load strategies
            container.Register<MemoryMappedLoadStrategy>(Lifestyle.Scoped);
            container.Register<StreamLoadStrategy>(Lifestyle.Scoped);

            // Register loader that uses both strategies
            container.Register<IAssetLoader, AssetLoader>(Lifestyle.Scoped);

            // Register factory and repository
            container.Register<IAssetFactory, AssetFactory>(Lifestyle.Scoped);
            container.Register<IAssetRepository, AssetRepository>(Lifestyle.Scoped);

            container.Register<AssetManagerOptions>(() =>
            new AssetManagerOptions
            {
                MaxConcurrentLoads = 4,
                MaxCacheSize = 1024 * 1024 * 100, // 100MB
                EnableAssetCaching = true,
                CacheDuration = TimeSpan.FromMinutes(10),
            }, Lifestyle.Singleton);
            container.Register<IOptions<AssetManagerOptions>>(() =>
           new OptionsWrapper<AssetManagerOptions>(container.GetInstance<AssetManagerOptions>()),
           Lifestyle.Singleton);
            // Register main manager
            container.Register<IAssetManager, AssetManager>(Lifestyle.Scoped);
            container.Register<AssetManager>(Lifestyle.Scoped);
            container.Register<IProjectManager>(() =>
                container.GetInstance<AssetManager>(), Lifestyle.Scoped);

             container.Register<TextureAsset>(Lifestyle.Transient);
             container.Register<MaterialAsset>(Lifestyle.Transient);
             container.Register<MeshAsset>(Lifestyle.Transient);
             container.Register<ModelAsset>(Lifestyle.Transient);
             container.Register<ProjectAsset>(Lifestyle.Transient);
        }
    }
}