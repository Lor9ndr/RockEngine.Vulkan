using MemoryPack;

namespace RockEngine.Core.Assets
{
    [MemoryPackable]
    public partial class ProjectData
    {
        public string Name { get; set; } = string.Empty;
        public string RootPath { get; set; } = string.Empty;
        public ProjectSettings Settings { get; set; } = new ProjectSettings();
    }
}