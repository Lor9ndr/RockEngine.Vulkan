using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using RockEngine.Core.Assets.Serializers;
using RockEngine.Vulkan;

namespace RockEngine.Core.DI
{
    internal class ConfigLoader
    {
        internal static AppSettings LoadConfig(IServiceProvider serviceProvider)
        {
            var file = File.ReadAllText(Directory.GetCurrentDirectory() + "\\appsettings.json");
            return JsonSerializer.Deserialize<AppSettings>(file, (serviceProvider.GetService<IAssetSerializer>()).Options);
        }
    }
}