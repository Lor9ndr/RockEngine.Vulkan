using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using RockEngine.ShaderPreprocessor;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{

    public class ShaderCompileResult : IShaderCompileResult
    {
        public required string ShaderPath { get; set; }
        public required ShaderMetadata Metadata { get; set; }
    }

    public class SpirVShader : ISpirVShader
    {
        public required byte[] ShaderData { get; set; }
        public required ShaderMetadata Metadata { get; set; }
    }

    public class ShaderManager : IShaderManager
    {
        private readonly string _basePath;
        private readonly string _includePath;
        private readonly ConcurrentDictionary<string, ISpirVShader> _compiledShaders = new();
        private readonly FeatureRegistry _featureRegistry;
        private readonly IShaderPreprocessor _shaderPreProcessor;

        public ShaderManager(FeatureRegistry featureRegistry, IShaderPreprocessor shaderPreProcessor)
        {
            _basePath = "Shaders";
            _includePath = Path.Combine(_basePath, "Include");

            if (!Directory.Exists(_includePath))
            {
                Directory.CreateDirectory(_includePath);
            }

            _featureRegistry = featureRegistry;
            _shaderPreProcessor = shaderPreProcessor;
        }

        /// <summary>
        /// Compiles all shaders found in the Shaders directory, passing preprocessor defines
        /// from the feature registry.
        /// </summary>
        public async Task CompileAllShadersAsync()
        {
            var defines = _featureRegistry?.GetAllPreprocessorDefines().ToList() ?? new List<string>();
            var tasks = new List<Task<IShaderCompileResult>>();
            var files = Directory.EnumerateFiles(_basePath, "*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".vert") || f.EndsWith(".geom") ||
                           f.EndsWith(".frag") || f.EndsWith(".comp"))
                .ToList();

            foreach (var file in files)
            {
                tasks.Add(CompileShaderAsync(file, defines));
            }

           var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            // Load all compiled .spv files

            foreach (var compileResult in results)
            {
                var shaderName = Path.GetFileName(compileResult.ShaderPath);
                shaderName = shaderName[..^4]; // Remove ".spv"

                var shaderBytes = await File.ReadAllBytesAsync(compileResult.ShaderPath).ConfigureAwait(false);
                var shader = new SpirVShader()
                {
                    ShaderData = shaderBytes,
                    Metadata = compileResult.Metadata
                };

                _compiledShaders[shaderName] = shader;
                
            }
        }

        public Task<IShaderCompileResult> CompileShaderAsync(string path)
        {
            var defines = _featureRegistry?.GetAllPreprocessorDefines().ToList() ?? new List<string>();
            return CompileShaderAsync(path, defines);
        }

        public async Task<IShaderCompileResult> CompileShaderByStringAsync(string code, ShaderStageFlags stage)
        {
            // Create a unique temporary file path with .glsl extension
            string tempDir = Path.GetTempPath();
            string extension = "glsl";
            if (stage == ShaderStageFlags.VertexBit)
            {
                extension = ".vert";
            }
            else if (stage == ShaderStageFlags.FragmentBit)
            {
                extension = ".frag";
            }
            string tempFile = Path.Combine(tempDir, $"{Guid.NewGuid()}.{extension}");

            try
            {
                await File.WriteAllTextAsync(tempFile, code).ConfigureAwait(false);

                var defines = _featureRegistry?.GetAllPreprocessorDefines().ToList() ?? new List<string>();
                return await CompileShaderAsync(tempFile, defines).ConfigureAwait(false);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        /// <summary>
        /// Compiles a single shader file with include processing and preprocessor defines.
        /// </summary>
        private async Task<IShaderCompileResult> CompileShaderAsync(string path, List<string> defines)
        {
            var compiledPath = $"{path}.spv";
            var extension = Path.GetExtension(path); // .comp, .vert, .frag, etc.

            var tempFileName = $"{Path.GetFileNameWithoutExtension(path)}_temp{extension}";
            var tempFile = Path.Combine(Path.GetDirectoryName(path), tempFileName);

            try
            {
                var preprocessResult = await PreprocessShader(path).ConfigureAwait(false);
                await File.WriteAllTextAsync(tempFile, preprocessResult.ProcessedSource).ConfigureAwait(false);

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
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                };
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorBuilder.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync().ConfigureAwait(false);

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
                return new ShaderCompileResult()
                { 
                     ShaderPath = compiledPath,
                     Metadata = preprocessResult.Metadata
                };

            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch { }
                }
            }
            return new ShaderCompileResult()
            {
                 Metadata = null,
                 ShaderPath = compiledPath
            };

        }

        private string BuildCompilerArgs(string outputPath, string inputPath, string extension, List<string> defines)
        {
            var args = new StringBuilder();
            args.Append($"-o \"{outputPath}\" ");
            args.Append($"-I \"{_includePath}\" ");

            foreach (var define in defines)
            {
                args.Append($"-D{define} ");
            }

            if (extension == ".comp")
            {
                args.Append("-std=450core ");
            }

            args.Append($"\"{inputPath}\"");
            return args.ToString();
        }

        private async Task<ShaderPreProcessResult> PreprocessShader(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Shader file not found: {path}");
            }

            var source = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            var defines = _featureRegistry?.GetAllPreprocessorDefines().ToList() ?? new List<string>();
            var extensions = _featureRegistry?.GetAllShaderExtensions().ToList() ?? new List<string>();

            // Let the preprocessor handle includes, material annotations, and defines
            var result = await _shaderPreProcessor.PreprocessAsync(source, path, defines, extensions).ConfigureAwait(false);

            return result;
        }


        public ISpirVShader GetShader(string name, bool removeAfterGet = false)
        {
            if (_compiledShaders.TryGetValue(name, out var bytes))
            {
                if (removeAfterGet)
                {
                    _compiledShaders.Remove(name, out _);
                }

                return bytes;
            }

            var key = _compiledShaders.Keys.FirstOrDefault(k =>
                string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

            if (key != null && _compiledShaders.TryGetValue(key, out bytes))
            {
                if (removeAfterGet)
                {
                    _compiledShaders.Remove(key, out _);
                }

                return bytes;
            }

            var availableShaders = string.Join(", ", _compiledShaders.Keys.OrderBy(k => k));
            throw new KeyNotFoundException($"Shader '{name}' not found. Available shaders: {availableShaders}");
        }

        public ISpirVShader GetShaderByPath(string path, bool removeAfterGet = true)
        {
            var fileName = Path.GetFileName(path);

            if (_compiledShaders.TryGetValue(fileName, out var bytes))
            {
                if (removeAfterGet)
                {
                    _compiledShaders.Remove(fileName, out _);
                }

                return bytes;
            }

            throw new KeyNotFoundException($"Shader '{path}' not found.");
        }

        public void ClearShaderCache()
        {
            _compiledShaders.Clear();
        }

        public IEnumerable<string> GetAvailableShaders()
        {
            return _compiledShaders.Keys.OrderBy(k => k).ToList();
        }

        public Dictionary<string, ISpirVShader> GetAllShaders()
        {
            return new Dictionary<string, ISpirVShader>(_compiledShaders);
        }
    }
}