using Assimp;

using RockEngine.Core.Rendering.Texturing;

namespace RockEngine.Core.Extensions
{
    public static class AssimpExtensions
    {
        extension(TextureWrapMode assimpWrap)
        {
            public TextureWrap ToEngineWrap()
            {
                return assimpWrap switch
                {
                    TextureWrapMode.Wrap => TextureWrap.Repeat,
                    TextureWrapMode.Mirror => TextureWrap.MirroredRepeat,
                    TextureWrapMode.Clamp => TextureWrap.ClampToEdge,
                    TextureWrapMode.Decal => TextureWrap.ClampToBorder, // or decide based on your engine
                    _ => TextureWrap.Repeat
                };
            }
        }
    }
}
