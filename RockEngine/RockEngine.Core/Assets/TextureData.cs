namespace RockEngine.Core.Assets
{
    namespace RockEngine.Core.Assets
    {
        public enum TextureType
        {
            Unknown,
            Texture2D,
            TextureCube
        }

        public class TextureData
        {
            public TextureType Type { get; set; } = TextureType.Unknown;
            public List<string> FilePaths { get; set; } = new List<string>();
            public bool GenerateMipmaps { get; set; } = true;
        }
    }
}