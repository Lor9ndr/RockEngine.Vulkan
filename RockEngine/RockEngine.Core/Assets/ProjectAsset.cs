
namespace RockEngine.Core.Assets
{
    public sealed class ProjectAsset : Asset<ProjectData>
    {
        public override string Type => "Project";
    }

    public class ProjectData
    {
        public string Name { get; set; }
        public string RootPath { get; set; }
    }
}
