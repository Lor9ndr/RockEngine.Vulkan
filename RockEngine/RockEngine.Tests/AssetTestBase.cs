using Microsoft.Extensions.DependencyInjection;

using Moq;

using NLog;

using RockEngine.Assets;
using RockEngine.Core;
using RockEngine.Core.Assets;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;

namespace RockEngine.Tests
{
    public class AssetTestBase : IDisposable
    {
        protected readonly string TestDirectory;
        protected readonly IServiceProvider ServiceProvider;
        protected readonly Mock<IAssetSerializer> MockSerializer;
        protected readonly Mock<AssimpLoader> MockAssimpLoader;
        protected readonly Mock<IGpuResource> MockGpuResource;
        protected readonly Mock<Application> MockApplication;

        public AssetTestBase()
        {


            // Configure NLog for tests
            LogManager.Setup().LoadConfiguration(builder =>
            {
                builder.ForLogger().FilterMinLevel(NLog.LogLevel.Info).WriteToConsole();
            });
        }

        protected string CreateTestFile(string relativePath, string content = "")
        {
            var fullPath = Path.Combine(TestDirectory, relativePath);
            var directory = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, content);
            return fullPath;
        }

        protected string CreateTestAssetFile(string relativePath, IAsset asset)
        {
            var fullPath = Path.Combine(TestDirectory, relativePath);
            var directory = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Mock serialization
            var stream = new MemoryStream();
            MockSerializer.Setup(s => s.SerializeAsync(asset, It.IsAny<Stream>()))
                .Callback<IAsset, Stream>((a, s) =>
                {
                    var writer = new StreamWriter(s);
                    writer.Write("test-asset-data");
                    writer.Flush();
                })
                .Returns(Task.CompletedTask);

            // Write mock data
            File.WriteAllText(fullPath, "test-asset-data");

            // Create meta file
            var metaPath = fullPath + ".meta";
            var metadata = new AssetMetadata(asset);
            File.WriteAllBytes(metaPath, metadata.ToBytes());

            return fullPath;
        }

        public void Dispose()
        {

            if (Directory.Exists(TestDirectory))
            {
                try
                {
                    Directory.Delete(TestDirectory, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            // Clean up NLog
            LogManager.Shutdown();
        }
    }
}