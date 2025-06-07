using Newtonsoft.Json;

using RockEngine.Vulkan;

namespace RockEngine.Core.DI
{
    internal class ConfigLoader
    {
        internal static AppSettings LoadConfig()
        {
            var file = File.ReadAllText(Directory.GetCurrentDirectory() + "\\appsettings.json");
            return JsonConvert.DeserializeObject<AppSettings>(file);
        }
    }
}