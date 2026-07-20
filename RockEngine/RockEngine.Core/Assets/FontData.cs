using MemoryPack;
using RockEngine.Assets;

namespace RockEngine.Core.Assets
{
    [MemoryPackable]
    public partial class FontData
    {
        public AssetPath FontFilePath { get; set; } = "";
        public float FontSize { get; set; } = 32f;
        public string Characters { get; set; } = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.!? :;\"'()[]{}%&$#@+-*/=<>,";
        public int AtlasWidth { get; set; } = 1024;
        public int AtlasHeight { get; set; } = 1024;
    }
}