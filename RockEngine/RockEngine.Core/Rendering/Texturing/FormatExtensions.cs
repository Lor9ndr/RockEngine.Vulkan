using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Texturing
{
    /// <summary>
    /// Extension methods for Vulkan format handling
    /// </summary>
    public static class FormatExtensions
    {
        /// <summary>
        /// Checks if format is block compressed
        /// </summary>
        public static bool IsBlockCompressed(this Format format)
        {
            return format.ToString().StartsWith("BC");
        }

        /// <summary>
        /// Gets block size for compressed formats
        /// </summary>
        public static (int Width, int Height) GetBlockSize(this Format format)
        {
            return format.ToString() switch
            {
                string s when s.StartsWith("BC") => (4, 4),
                _ => (1, 1)
            };
        }
    }

}
