using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public class ShaderManager : IShaderManager
    {
        private readonly string _basePath;
        private readonly string _includePath;
        private readonly ConcurrentDictionary<string, byte[]> _compiledShaders = new();
        private readonly ConcurrentDictionary<string, string> _includeCache = new();
        private readonly FeatureRegistry _featureRegistry;

        public ShaderManager(FeatureRegistry featureRegistry)
        {
            _basePath = "Shaders";
            _includePath = Path.Combine(_basePath, "Include");

            if (!Directory.Exists(_includePath))
                Directory.CreateDirectory(_includePath);

            _featureRegistry = featureRegistry;
        }

        /// <summary>
        /// Compiles all shaders found in the Shaders directory, passing preprocessor defines
        /// from the feature registry.
        /// </summary>
        public async Task CompileAllShadersAsync()
        {
            var defines = _featureRegistry?.GetAllPreprocessorDefines().ToList() ?? new List<string>();
            var tasks = new List<Task>();
            var files = Directory.EnumerateFiles(_basePath, "*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".vert") || f.EndsWith(".geom") ||
                           f.EndsWith(".frag") || f.EndsWith(".comp"))
                .ToList();

            foreach (var file in files)
            {
                tasks.Add(CompileShaderWithIncludes(file, defines));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Load all compiled .spv files
            var spvFiles = Directory.EnumerateFiles(_basePath, "*.spv", SearchOption.AllDirectories);
            foreach (var file in spvFiles)
            {
                var shaderName = Path.GetFileName(file);
                shaderName = shaderName.Substring(0, shaderName.Length - 4); // Remove ".spv"

                var shaderNameWithoutExt = Path.GetFileNameWithoutExtension(shaderName);
                var shaderBytes = await File.ReadAllBytesAsync(file);

                _compiledShaders[shaderName] = shaderBytes;
                if (!_compiledShaders.ContainsKey(shaderNameWithoutExt))
                    _compiledShaders[shaderNameWithoutExt] = shaderBytes;
            }
        }

        /// <summary>
        /// Compiles a single shader file with include processing and preprocessor defines.
        /// </summary>
        public async Task<string> CompileShaderWithIncludes(string path, List<string> defines = null)
        {
            var compiledPath = $"{path}.spv";
            var extension = Path.GetExtension(path); // .comp, .vert, .frag, etc.

            var tempFileName = $"{Path.GetFileNameWithoutExtension(path)}_temp{extension}";
            var tempFile = Path.Combine(Path.GetDirectoryName(path), tempFileName);

            try
            {
                var processedSource = await PreprocessShader(path);
                await File.WriteAllTextAsync(tempFile, processedSource);

                var args = BuildCompilerArgs(compiledPath, tempFile, extension, defines);

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "glslc",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory()
                };

                using var process = new Process();
                process.StartInfo = processStartInfo;

                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        outputBuilder.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        errorBuilder.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"Shader compilation failed for {Path.GetFileName(path)}:\n" +
                        $"Exit Code: {process.ExitCode}\n" +
                        $"Output: {outputBuilder}\n" +
                        $"Error: {errorBuilder}\n" +
                        $"Command: glslc {args}");
                }

                if (errorBuilder.Length > 0)
                {
                    Console.WriteLine($"Shader compilation warnings for {Path.GetFileName(path)}:\n{errorBuilder}");
                }
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }

            return compiledPath;
        }

        private string BuildCompilerArgs(string outputPath, string inputPath, string extension, List<string> defines)
        {
            var args = new StringBuilder();
            args.Append($"-o \"{outputPath}\" ");
            args.Append($"-I \"{_includePath}\" ");

            if (defines != null)
            {
                foreach (var define in defines)
                    args.Append($"-D{define} ");
            }

            if (extension == ".comp")
                args.Append("-std=450core ");

            args.Append($"\"{inputPath}\"");
            return args.ToString();
        }

        // PreprocessShader and ProcessIncludes remain unchanged
        private async Task<string> PreprocessShader(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Shader file not found: {path}");

            var source = await File.ReadAllTextAsync(path);
            var directory = Path.GetDirectoryName(path);

            var includePattern = @"#include\s+[""'](.+?)[""']";
            var matches = Regex.Matches(source, includePattern);

            foreach (Match match in matches)
            {
                var includeFile = match.Groups[1].Value;
                string includePath;

                var relativePath = Path.Combine(directory, includeFile);
                if (File.Exists(relativePath))
                    includePath = relativePath;
                else if (File.Exists(includeFile))
                    includePath = includeFile;
                else
                {
                    includePath = Path.Combine(_includePath, includeFile);
                    if (!File.Exists(includePath))
                        throw new FileNotFoundException($"Include file not found: {includeFile} (searched in {relativePath} and {includePath})");
                }

                if (!_includeCache.TryGetValue(includePath, out var includeSource))
                {
                    includeSource = await File.ReadAllTextAsync(includePath);
                    _includeCache[includePath] = includeSource;
                }

                includeSource = await ProcessIncludes(includeSource, Path.GetDirectoryName(includePath));
                source = source.Replace(match.Value, includeSource);
            }

            return source;
        }

        private async Task<string> ProcessIncludes(string source, string baseDirectory)
        {
            var includePattern = @"#include\s+[""'](.+?)[""']";
            var matches = Regex.Matches(source, includePattern);

            foreach (Match match in matches)
            {
                var includeFile = match.Groups[1].Value;
                string includePath;

                var relativePath = Path.Combine(baseDirectory, includeFile);
                if (File.Exists(relativePath))
                    includePath = relativePath;
                else if (File.Exists(includeFile))
                    includePath = includeFile;
                else
                {
                    includePath = Path.Combine(_includePath, includeFile);
                    if (!File.Exists(includePath))
                        throw new FileNotFoundException($"Include file not found: {includeFile}");
                }

                if (!_includeCache.TryGetValue(includePath, out var includeSource))
                {
                    includeSource = await File.ReadAllTextAsync(includePath);
                    _includeCache[includePath] = includeSource;
                }

                includeSource = await ProcessIncludes(includeSource, Path.GetDirectoryName(includePath));
                source = source.Replace(match.Value, includeSource);
            }

            return source;
        }

        // Backward compatibility
        public async Task<string> CompileShader(string path)
        {
            return await CompileShaderWithIncludes(path, null);
        }

        public byte[] GetShader(string name, bool removeAfterGet = true)
        {
            if (_compiledShaders.TryGetValue(name, out var bytes))
            {
                if (removeAfterGet)
                    _compiledShaders.Remove(name, out _);
                return bytes;
            }

            var nameWithoutExtension = Path.GetFileNameWithoutExtension(name);
            if (_compiledShaders.TryGetValue(nameWithoutExtension, out bytes))
            {
                if (removeAfterGet)
                    _compiledShaders.Remove(nameWithoutExtension, out _);
                return bytes;
            }

            var key = _compiledShaders.Keys.FirstOrDefault(k =>
                string.Equals(k, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileNameWithoutExtension(k), nameWithoutExtension, StringComparison.OrdinalIgnoreCase));

            if (key != null && _compiledShaders.TryGetValue(key, out bytes))
            {
                if (removeAfterGet)
                    _compiledShaders.Remove(key, out _);
                return bytes;
            }

            var availableShaders = string.Join(", ", _compiledShaders.Keys.OrderBy(k => k));
            throw new KeyNotFoundException($"Shader '{name}' not found. Available shaders: {availableShaders}");
        }

        public byte[] GetShaderByPath(string path, bool removeAfterGet = true)
        {
            var fileName = Path.GetFileName(path);
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);

            if (_compiledShaders.TryGetValue(fileName, out var bytes))
            {
                if (removeAfterGet)
                    _compiledShaders.Remove(fileName, out _);
                return bytes;
            }

            if (_compiledShaders.TryGetValue(fileNameWithoutExt, out bytes))
            {
                if (removeAfterGet)
                    _compiledShaders.Remove(fileNameWithoutExt, out _);
                return bytes;
            }

            throw new KeyNotFoundException($"Shader '{path}' not found.");
        }

        public void ClearShaderCache()
        {
            _compiledShaders.Clear();
            _includeCache.Clear();
        }

        public IEnumerable<string> GetAvailableShaders()
        {
            return _compiledShaders.Keys.OrderBy(k => k).ToList();
        }

        public Dictionary<string, byte[]> GetAllShaders()
        {
            return new Dictionary<string, byte[]>(_compiledShaders);
        }
    }
}