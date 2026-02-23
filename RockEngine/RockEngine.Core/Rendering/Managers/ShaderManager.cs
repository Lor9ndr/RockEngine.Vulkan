using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace RockEngine.Core.Rendering.Managers
{
    public class ShaderManager : IShaderManager
    {
        private readonly string _basePath;
        private readonly string _includePath;
        private readonly ConcurrentDictionary<string, byte[]> _compiledShaders = new();
        private readonly ConcurrentDictionary<string, string> _includeCache = new();

        public ShaderManager()
        {
            _basePath = "Shaders";
            _includePath = Path.Combine(_basePath, "Include");

            // Create include directory if it doesn't exist
            if (!Directory.Exists(_includePath))
            {
                Directory.CreateDirectory(_includePath);
            }
        }

        public async Task CompileAllShadersAsync()
        {
            var tasks = new List<Task>();
            var files = Directory.EnumerateFiles(_basePath, "*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".vert") || f.EndsWith(".geom") ||
                           f.EndsWith(".frag") || f.EndsWith(".comp"))
                .ToList();

            // Compile each shader
            foreach (var file in files)
            {
                tasks.Add(CompileShaderWithIncludes(file));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Load compiled shaders - store with full name including stage
            var spvFiles = Directory.EnumerateFiles(_basePath, "*.spv", SearchOption.AllDirectories);
            foreach (var file in spvFiles)
            {
                // Get the original shader name without .spv extension
                // Example: "Picking.vert.spv" -> "Picking.vert"
                var shaderName = Path.GetFileName(file);
                shaderName = shaderName.Substring(0, shaderName.Length - 4); // Remove ".spv"

                // Also store without extension for backward compatibility
                var shaderNameWithoutExt = Path.GetFileNameWithoutExtension(shaderName);

                var shaderBytes = await File.ReadAllBytesAsync(file);

                // Store with full name (e.g., "Picking.vert")
                _compiledShaders[shaderName] = shaderBytes;

                // Also store without extension (e.g., "Picking") for backward compatibility
                // But only if not already stored (to avoid overwriting)
                if (!_compiledShaders.ContainsKey(shaderNameWithoutExt))
                {
                    _compiledShaders[shaderNameWithoutExt] = shaderBytes;
                }
            }
        }

        /// <summary>
        /// Preprocess shader to resolve includes before compilation
        /// </summary>
        private async Task<string> PreprocessShader(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Shader file not found: {path}");

            var source = await File.ReadAllTextAsync(path);
            var directory = Path.GetDirectoryName(path);

            // Pattern to match #include statements
            var includePattern = @"#include\s+[""'](.+?)[""']";
            var matches = Regex.Matches(source, includePattern);

            foreach (Match match in matches)
            {
                var includeFile = match.Groups[1].Value;
                string includePath;

                // Try relative path first
                var relativePath = Path.Combine(directory, includeFile);
                if (File.Exists(relativePath))
                {
                    includePath = relativePath;
                }
                // Try from include directory
                else if (File.Exists(includeFile))
                {
                    includePath = includeFile;
                }
                else
                {
                    includePath = Path.Combine(_includePath, includeFile);
                    if (!File.Exists(includePath))
                        throw new FileNotFoundException($"Include file not found: {includeFile} (searched in {relativePath} and {includePath})");
                }

                // Cache includes to avoid redundant disk reads
                if (!_includeCache.TryGetValue(includePath, out var includeSource))
                {
                    includeSource = await File.ReadAllTextAsync(includePath);
                    _includeCache[includePath] = includeSource;
                }

                // Recursively process includes in the include file
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
                {
                    includePath = relativePath;
                }
                else if (File.Exists(includeFile))
                {
                    includePath = includeFile;
                }
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

                // Recursive processing
                includeSource = await ProcessIncludes(includeSource, Path.GetDirectoryName(includePath));
                source = source.Replace(match.Value, includeSource);
            }

            return source;
        }

        /// <summary>
        /// Compiles shader with includes resolved
        /// </summary>
        public async Task<string> CompileShaderWithIncludes(string path)
        {
            var compiledPath = $"{path}.spv";
            var extension = Path.GetExtension(path); // .comp, .vert, .frag, etc.

            // Create a temporary filename with the correct shader extension
            var tempFileName = $"{Path.GetFileNameWithoutExtension(path)}_temp{extension}";
            var tempFile = Path.Combine(Path.GetDirectoryName(path), tempFileName);

            try
            {
                // Preprocess to resolve includes
                var processedSource = await PreprocessShader(path);

                // Write processed source to a temporary file WITH CORRECT EXTENSION
                await File.WriteAllTextAsync(tempFile, processedSource);

                // Build glslc arguments
                var args = new StringBuilder();
                args.Append($"-o \"{compiledPath}\" ");

                // Add include directory
                args.Append($"-I \"{_includePath}\" ");

                // For compute shaders, we might need specific options
                if (extension == ".comp")
                {
                    args.Append("-std=450core "); // Vulkan 1.1/GLSL 450 core
                }

                args.Append($"\"{tempFile}\"");

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "glslc",
                    Arguments = args.ToString(),
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

                var output = outputBuilder.ToString();
                var error = errorBuilder.ToString();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"Shader compilation failed for {Path.GetFileName(path)}:\n" +
                        $"Exit Code: {process.ExitCode}\n" +
                        $"Output: {output}\n" +
                        $"Error: {error}\n" +
                        $"Command: glslc {args}");
                }

                if (!string.IsNullOrEmpty(error))
                {
                    Console.WriteLine($"Shader compilation warnings for {Path.GetFileName(path)}:\n{error}");
                }
            }
            finally
            {
                // Clean up temporary file
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }

            return compiledPath;
        }

        /// <summary>
        /// Simple compilation without includes (for backward compatibility)
        /// </summary>
        public async Task<string> CompileShader(string path)
        {
            var compiledPath = $"{path}.spv";
            var extension = Path.GetExtension(path);

            var args = new StringBuilder();
            args.Append($"-o \"{compiledPath}\" ");

            // Add include directory if it exists
            if (Directory.Exists(_includePath))
            {
                args.Append($"-I \"{_includePath}\" ");
            }

            if (extension == ".comp")
            {
                args.Append("-std=450core ");
            }

            args.Append($"\"{path}\"");

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "glslc",
                Arguments = args.ToString(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

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
                var error = errorBuilder.ToString();
                throw new InvalidOperationException($"Shader compilation failed: {error}");
            }

            return compiledPath;
        }

        public byte[] GetShader(string name, bool removeAfterGet = true)
        {
            // Try exact match first (e.g., "Picking.vert")
            if (_compiledShaders.TryGetValue(name, out var bytes))
            {
                if (removeAfterGet)
                    _compiledShaders.Remove(name, out _);
                return bytes;
            }

            // Try without extension (e.g., "Picking" for "Picking.vert")
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(name);
            if (_compiledShaders.TryGetValue(nameWithoutExtension, out bytes))
            {
                if (removeAfterGet)
                    _compiledShaders.Remove(nameWithoutExtension, out _);
                return bytes;
            }

            // Try case-insensitive search
            var key = _compiledShaders.Keys.FirstOrDefault(k =>
                string.Equals(k, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileNameWithoutExtension(k), nameWithoutExtension, StringComparison.OrdinalIgnoreCase));

            if (key != null && _compiledShaders.TryGetValue(key, out bytes))
            {
                if (removeAfterGet)
                    _compiledShaders.Remove(key, out _);
                return bytes;
            }

            // Try partial match for debugging
            var availableShaders = string.Join(", ", _compiledShaders.Keys.OrderBy(k => k));
            throw new KeyNotFoundException($"Shader '{name}' not found. Available shaders: {availableShaders}");
        }

        public byte[] GetShaderByPath(string path, bool removeAfterGet = true)
        {
            // Extract shader name from path
            var fileName = Path.GetFileName(path); // e.g., "Picking.vert"
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName); // e.g., "Picking"

            // Try with extension first, then without
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