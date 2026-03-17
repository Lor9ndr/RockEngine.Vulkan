using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RockEngine.ShaderPreprocessor;

namespace ShaderValidator
{
    class Program
    {
        public static async Task<int> Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("Usage: ShaderValidator.exe <shader-file> [--compiler glslc|glslang] [--original-file <path>] [--defines DEFINE1;DEFINE2...]");
                return 1;
            }

            string filePath = args[0];
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"File not found: {filePath}");
                return 1;
            }

            string compiler = "glslang";
            string originalFilePath = null;
            List<string> defines = new List<string>();

            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--compiler" && i + 1 < args.Length)
                {
                    compiler = args[++i];
                }
                else if (args[i] == "--original-file" && i + 1 < args.Length)
                {
                    originalFilePath = args[++i];
                }
                else if (args[i] == "--defines" && i + 1 < args.Length)
                {
                    defines.AddRange(args[++i].Split(';', StringSplitOptions.RemoveEmptyEntries));
                }
            }

            string basePathForIncludes = originalFilePath ?? filePath;

            var preprocessor = new MainShaderPreprocessor();
            string originalSource = await File.ReadAllTextAsync(filePath);
            ShaderPreProcessResult processedSource;
            try
            {
                processedSource = await preprocessor.PreprocessAsync(originalSource, basePathForIncludes, defines);
            }
            catch (Exception ex)
            {
                var errorMsg = new ValidationMessage
                {
                    File = filePath,
                    Line = 0,
                    Column = 0,
                    Level = "error",
                    Message = $"Preprocessing failed: {ex.Message}"
                };
                Console.WriteLine(JsonSerializer.Serialize(new[] { errorMsg }));
                return 1;
            }

            string tempPreprocessed = Path.GetTempFileName() + Path.GetExtension(filePath);
            await File.WriteAllTextAsync(tempPreprocessed, processedSource.ProcessedSource);

            List<ValidationMessage> messages;
            try
            {
                messages = await RunValidator(tempPreprocessed, compiler);
            }
            catch (Exception ex)
            {
                messages = new List<ValidationMessage>
                {
                    new ValidationMessage
                    {
                        File = filePath,
                        Line = 0,
                        Column = 0,
                        Level = "error",
                        Message = $"Validator error: {ex.Message}"
                    }
                };
            }

            foreach (var msg in messages)
            {
                // msg.Line is 0‑based from ParseMessages, mapping uses 1‑based
                int preprocessedLine = msg.Line + 1;

                // Find the mapping entry for this preprocessed line
                var mapping = processedSource.LineMappings
                    .LastOrDefault(m => m.PreprocessedLine == preprocessedLine);

                if (mapping != null)
                {
                    msg.Line = mapping.OriginalLine - 1; // back to 0‑based
                    // Optionally update the file path if the error originated in an include
                    if (!string.Equals(mapping.SourceFilePath, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        msg.File = mapping.SourceFilePath;
                    }
                }
                // else: leave msg.Line as is (may be out of range, will be filtered by tagger)
            }

            Console.WriteLine(JsonSerializer.Serialize(messages));

            try { File.Delete(tempPreprocessed); } catch { }

            return messages.Any(m => m.Level == "error") ? 1 : 0;
        }

        private static async Task<List<ValidationMessage>> RunValidator(string filePath, string compiler)
        {
            string arguments;
            string executable;

            if (compiler.Equals("glslc", StringComparison.OrdinalIgnoreCase))
            {
                executable = "glslc";
                arguments = $"-o nul --target-env=vulkan1.2 -I \"{Path.GetDirectoryName(filePath)}\" \"{filePath}\"";
            }
            else
            {
                executable = "glslangValidator";
                arguments = $"-V \"{filePath}\"";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            var output = new StringBuilder();
            var error = new StringBuilder();

            process.OutputDataReceived += (s, e) => output.AppendLine(e.Data);
            process.ErrorDataReceived += (s, e) => error.AppendLine(e.Data);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            // Combine both streams – some tools write everything to stderr
            string allOutput = output.ToString() + error.ToString();
            return ParseMessages(allOutput, filePath);
        }

        private static List<ValidationMessage> ParseMessages(string errorText, string filePath)
        {
            var list = new List<ValidationMessage>();
            var lines = errorText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                // glslc format: file:line:col: level: message
                var match = Regex.Match(line, @"^(?<file>.+?):(?<line>\d+):(?<col>\d+):\s+(?<level>error|warning):\s+(?<message>.+)$", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    list.Add(new ValidationMessage
                    {
                        File = match.Groups["file"].Value,
                        Line = int.Parse(match.Groups["line"].Value),
                        Column = int.Parse(match.Groups["col"].Value) - 1,
                        Level = match.Groups["level"].Value.ToLower(),
                        Message = match.Groups["message"].Value
                    });
                    continue;
                }

                // glslang format: "ERROR: file:line: message"  or  "WARNING: file:line: message"
                match = Regex.Match(line, @"^(?<level>ERROR|WARNING):\s+(?<file>.+?):(?<line>\d+):\s+(?<message>.+)$", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    list.Add(new ValidationMessage
                    {
                        File = match.Groups["file"].Value,
                        Line = int.Parse(match.Groups["line"].Value),
                        Column = 0,
                        Level = match.Groups["level"].Value.ToLower(),
                        Message = match.Groups["message"].Value
                    });
                    continue;
                }

                // glslang also outputs summary lines like "ERROR: 2 compilation errors." – ignore them.
            }
            return list;
        }

        private class ValidationMessage
        {
            public string File { get; set; }
            public int Line { get; set; }
            public int Column { get; set; }
            public string Level { get; set; }
            public string Message { get; set; }
        }
    }
}