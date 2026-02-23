using MessagePack;

namespace RockEngine.Core.Assets
{
    [MessagePackObject]
    public class ProjectData
    {
        [Key(0)]
        public string Name { get; set; } = string.Empty;
        [Key(1)]
        public string RootPath { get; set; } = string.Empty;
        [Key(2)]
        public ProjectSettings Settings { get; set; } = new ProjectSettings();
    }
}