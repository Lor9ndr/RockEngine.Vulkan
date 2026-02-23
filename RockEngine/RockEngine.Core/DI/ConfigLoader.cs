using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using RockEngine.Assets;
using RockEngine.Vulkan;

namespace RockEngine.Core.DI
{
    internal static class ConfigLoader
    {
        internal static async Task<AppSettings> LoadConfigAsync(IServiceProvider serviceProvider)
        {
            using var file = File.OpenRead(Directory.GetCurrentDirectory() + "\\appsettings.yaml");
            var serializer = serviceProvider.GetRequiredService<IYamlSerializer>();
            return  (AppSettings) await serializer.DeserializeAsync(file, typeof(AppSettings));
        }
    }
}