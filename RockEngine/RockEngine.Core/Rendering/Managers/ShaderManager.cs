using System.Diagnostics;

namespace RockEngine.Core.Rendering.Managers
{
    public class ShaderManager : IShaderManager
    {
        private readonly string _basePath;
        private readonly Dictionary<string, byte[]> _compiledShaders = new();

        public ShaderManager()
        {
            _basePath = "Shaders";
        }

        public async Task CompileAllShadersAsync()
        {
            var tasks = new List<Task>();
            Console.WriteLine(Directory.EnumerateFiles(_basePath).Count());
            foreach (var file in Directory.EnumerateFiles(_basePath, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".vert") || file.EndsWith(".frag") || file.EndsWith(".comp"))
                {
                    tasks.Add(CompileShader(file));
                }
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
            DeleteShaderSource();
            foreach (var file in Directory.EnumerateFiles(_basePath, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".spv"))
                {
                    _compiledShaders[Path.GetFileName(file.Replace(".spv", ""))] = await File.ReadAllBytesAsync(file);
                }
            }
        }

        private void DeleteShaderSource()
        {
            foreach (var file in Directory.EnumerateFiles(_basePath, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".vert") || file.EndsWith(".frag") || file.EndsWith(".comp"))
                {
                    File.Delete(file);
                }
            }
        }

        /// <summary>
        /// Compiles shader with glslc
        /// </summary>
        /// <param name="path">output compiled path</param>
        /// <returns></returns>
        public async Task<string> CompileShader(string path)
        {
            var compiledPath = $"{path}.spv";
            await Process.Start("glslc", $"-o {compiledPath} {path}").WaitForExitAsync();
            return compiledPath;
        }

        public byte[] GetShader(string name, bool removeAfterGet = true)
        {
            var bytes = _compiledShaders[name];
            _compiledShaders.Remove(name);
            return bytes;
        }
    }
}
