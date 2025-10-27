using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.IO;
using System.Diagnostics;

namespace RockEngine.CodeGenerator
{
   /* [Generator]
    public class ShaderStructGenerator : ISourceGenerator
    {
        private static readonly HashSet<string> _samplerTypes = new HashSet<string>
        {
            "sampler2D", "sampler3D", "samplerCube", "sampler2DArray",
            "sampler2DShadow", "samplerCubeShadow", "isampler2D", "usampler2D",
            "subpassInput"
        };

        public void Initialize(GeneratorInitializationContext context)
        {
            // No syntax receiver needed for additional files
        }

        public void Execute(GeneratorExecutionContext context)
        {
            try
            {
                // Get all shader files
                var shaderFiles = context.AdditionalFiles
                    .Where(f => f.Path.EndsWith(".vert") || f.Path.EndsWith(".frag") ||
                               f.Path.EndsWith(".comp") || f.Path.EndsWith(".glsl"))
                    .ToList();

                if (!shaderFiles.Any())
                    return;

                var allStructs = new Dictionary<string, ShaderStruct>();
                var shaderBindings = new List<ShaderBindingInfo>();

                // Process all shader files
                foreach (var shaderFile in shaderFiles)
                {
                    try
                    {
                        var shaderContent = shaderFile.GetText(context.CancellationToken)?.ToString();
                        if (string.IsNullOrEmpty(shaderContent))
                            continue;

                        var fileName = Path.GetFileNameWithoutExtension(shaderFile.Path);
                        var fileStructs = ParseShaderStructs(shaderContent, fileName);

                        foreach (var shaderStruct in fileStructs)
                        {
                            // Skip if it's a sampler type
                            if (IsSamplerStruct(shaderStruct))
                                continue;

                            // Add to global structs
                            var uniqueKey = $"{shaderStruct.Name}";
                            if (!allStructs.ContainsKey(uniqueKey))
                            {
                                allStructs[uniqueKey] = shaderStruct;
                            }
                            else
                            {
                                // Merge members if struct with same name exists
                                var existing = allStructs[uniqueKey];
                                foreach (var member in shaderStruct.Members)
                                {
                                    if (!existing.Members.Any(m => m.Name == member.Name && m.Type == member.Type))
                                    {
                                        existing.Members.Add(member);
                                    }
                                }
                            }

                            // Add to bindings
                            shaderBindings.Add(new ShaderBindingInfo
                            {
                                ShaderFileName = fileName,
                                ShaderFilePath = shaderFile.Path,
                                StructName = shaderStruct.Name,
                                IsPushConstant = shaderStruct.IsPushConstant,
                                Set = shaderStruct.Set,
                                Binding = shaderStruct.Binding
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "SHADER001",
                                "Shader Parsing Error",
                                $"Error parsing shader {shaderFile.Path}: {ex.Message}",
                                "Generation",
                                DiagnosticSeverity.Warning,
                                true),
                            Location.None));
                    }
                }

                // Generate global structs file (all unique structs)
                if (allStructs.Any())
                {
                    var globalSource = GenerateGlobalStructs(allStructs.Values.ToList());
                    context.AddSource("ShaderStructs.Global.g.cs", SourceText.From(globalSource, Encoding.UTF8));
                }

                // Generate single global binding registry
                if (shaderBindings.Any())
                {
                    var bindingSource = GenerateGlobalBindings(shaderBindings);
                    context.AddSource("ShaderBindings.Global.g.cs", SourceText.From(bindingSource, Encoding.UTF8));
                }
            }
            catch (Exception ex)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "SHADER000",
                        "Shader Generator Error",
                        $"Fatal error in shader generator: {ex.Message}",
                        "Generation",
                        DiagnosticSeverity.Error,
                        true),
                    Location.None));
            }
        }

        private bool IsSamplerStruct(ShaderStruct shaderStruct)
        {
            return shaderStruct.Members.All(m => _samplerTypes.Contains(m.Type)) ||
                   shaderStruct.Name.ToLower().Contains("sampler");
        }

        private List<ShaderStruct> ParseShaderStructs(string shaderContent, string fileName)
        {
            var structs = new List<ShaderStruct>();
            var lines = shaderContent.Split('\n');

            ShaderStruct currentStruct = null;
            var inStruct = false;
            var braceCount = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                // Skip empty lines, comments, and preprocessor directives
                if (string.IsNullOrEmpty(line) || line.StartsWith("//") || line.StartsWith("#"))
                    continue;

                // Look for push_constant blocks - handle multiple formats
                if ((line.Contains("layout(push_constant)") || line.Contains("push_constant")) &&
                    line.Contains("uniform"))
                {
                    currentStruct = ExtractStructFromLine(line, true, -1, -1);
                    if (currentStruct != null)
                    {
                        inStruct = true;
                        braceCount = 0;
                        continue;
                    }
                }

                // Look for uniform blocks with set/binding - handle multiple layout formats
                if ((line.Contains("layout(set") || line.Contains("layout(std430") ||
                     line.Contains("layout(input_attachment_index") || line.Contains("layout(binding")) &&
                    (line.Contains("uniform") || line.Contains("buffer")))
                {
                    ExtractSetAndBinding(line, out int set, out int binding);
                    currentStruct = ExtractStructFromLine(line, false, set, binding);
                    if (currentStruct != null)
                    {
                        inStruct = true;
                        braceCount = 0;
                        continue;
                    }
                }

                if (inStruct && currentStruct != null)
                {
                    // Count braces to handle nested structs
                    foreach (char c in line)
                    {
                        if (c == '{') braceCount++;
                        if (c == '}') braceCount--;
                    }

                    // Look for struct members (skip sampler types and empty lines)
                    // Only parse members when we're inside the struct body (braceCount == 1)
                    if (line.EndsWith(";") && braceCount == 1 && !string.IsNullOrWhiteSpace(line.Replace(";", "")))
                    {
                        var member = ParseStructMember(line);
                        if (member != null && !_samplerTypes.Contains(member.Type))
                        {
                            currentStruct.Members.Add(member);
                        }
                    }

                    // End of struct - when we hit the closing brace and braceCount goes back to 0
                    if (line.Contains("}") && braceCount == 0 && currentStruct != null)
                    {
                        // Extract the instance name if present (like "} push;")
                        var instanceName = ExtractInstanceName(line);
                        if (!string.IsNullOrEmpty(instanceName))
                        {
                            currentStruct.InstanceName = instanceName;
                        }

                        // Only add struct if it has non-sampler members
                        if (currentStruct.Members.Any(m => !_samplerTypes.Contains(m.Type)))
                        {
                            // Check if we already have a struct with this name
                            var existingStruct = structs.FirstOrDefault(s => s.Name == currentStruct.Name);
                            if (existingStruct != null)
                            {
                                // Merge members
                                foreach (var member in currentStruct.Members)
                                {
                                    if (!existingStruct.Members.Any(m => m.Name == member.Name && m.Type == member.Type))
                                    {
                                        existingStruct.Members.Add(member);
                                    }
                                }
                            }
                            else
                            {
                                structs.Add(currentStruct);
                            }
                        }
                        currentStruct = null;
                        inStruct = false;
                    }
                }
            }

            return structs;
        }

        private ShaderStruct ExtractStructFromLine(string line, bool isPushConstant, int set, int binding)
        {
            // Handle different GLSL uniform/buffer formats:
            // 1. "layout(push_constant) uniform PushConstants {"
            // 2. "layout(set = 0, binding = 0) uniform GlobalUbo_Dynamic {"
            // 3. "layout(std430, set = 1, binding = 0) readonly buffer ModelData {"
            // 4. "layout(set = 1, binding = 0) uniform LightCount {"

            // Find the uniform/buffer keyword and the struct name before the opening brace
            var uniformIndex = line.IndexOf("uniform");
            var bufferIndex = line.IndexOf("buffer");
            var braceIndex = line.IndexOf('{');

            if ((uniformIndex < 0 && bufferIndex < 0) || braceIndex < 0)
                return null;

            var keywordIndex = uniformIndex >= 0 ? uniformIndex : bufferIndex;

            // Extract the text between the uniform/buffer keyword and the opening brace
            var between = line.Substring(keywordIndex, braceIndex - keywordIndex).Trim();
            var parts = between.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            // The struct name should be the second part (after "uniform" or "buffer")
            if (parts.Length < 2)
                return null;

            var structName = parts[1];

            return new ShaderStruct
            {
                Name = structName,
                IsPushConstant = isPushConstant,
                Set = set,
                Binding = binding
            };
        }

        private string ExtractInstanceName(string line)
        {
            // Extract instance name from lines like:
            // "} push;"
            // "} ubo;"
            var parts = line.Split(new[] { ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0] == "}")
            {
                return parts[1];
            }
            return null;
        }

        private void ExtractSetAndBinding(string line, out int set, out int binding)
        {
            set = -1;
            binding = -1;

            try
            {
                // Find layout content between parentheses
                var layoutStart = line.IndexOf("layout(");
                if (layoutStart < 0) return;

                layoutStart += 7; // Move past "layout("
                var layoutEnd = line.IndexOf(')', layoutStart);
                if (layoutEnd < 0) return;

                var layoutContent = line.Substring(layoutStart, layoutEnd - layoutStart);

                // Split by commas and process each part
                var parts = layoutContent.Split(',');
                foreach (var part in parts)
                {
                    var trimmed = part.Trim();

                    if (trimmed.StartsWith("set = "))
                    {
                        var setStr = trimmed.Substring(6).Trim();
                        int.TryParse(setStr, out set);
                    }
                    else if (trimmed.StartsWith("binding = "))
                    {
                        var bindingStr = trimmed.Substring(10).Trim();
                        int.TryParse(bindingStr, out binding);
                    }
                    else if (trimmed.StartsWith("input_attachment_index = "))
                    {
                        // For subpass inputs, binding might be specified differently
                        var bindingStr = trimmed.Substring(25).Trim();
                        int.TryParse(bindingStr, out binding);
                    }
                }
            }
            catch
            {
                // If parsing fails, use defaults
            }
        }

        private ShaderStructMember ParseStructMember(string line)
        {
            var cleanLine = line.Trim().TrimEnd(';');
            if (string.IsNullOrWhiteSpace(cleanLine)) return null;

            // Handle arrays
            var arrayStart = cleanLine.IndexOf('[');
            var arraySize = 0;
            if (arrayStart >= 0)
            {
                var arrayEnd = cleanLine.IndexOf(']', arrayStart);
                if (arrayEnd > arrayStart)
                {
                    var arraySizeStr = cleanLine.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
                    cleanLine = cleanLine.Substring(0, arrayStart) + cleanLine.Substring(arrayEnd + 1);
                    int.TryParse(arraySizeStr, out arraySize);
                }
            }

            var parts = cleanLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;

            // The last part is the name, everything before is the type
            var name = parts[parts.Length - 1];
            var type = string.Join(" ", parts.Take(parts.Length - 1));

            return new ShaderStructMember
            {
                Type = type.Trim(),
                Name = name.Trim(),
                ArraySize = arraySize
            };
        }

        private string GenerateGlobalStructs(List<ShaderStruct> structs)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#pragma warning disable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Runtime.InteropServices;");
            sb.AppendLine("using System.Numerics;");
            sb.AppendLine();
            sb.AppendLine("namespace GeneratedShaders");
            sb.AppendLine("{");

            foreach (var shaderStruct in structs)
            {
                if (!shaderStruct.Members.Any()) continue;

                sb.AppendLine($"    [StructLayout(LayoutKind.Sequential)]");
                sb.AppendLine($"    public struct {shaderStruct.Name}");
                sb.AppendLine("    {");

                foreach (var member in shaderStruct.Members)
                {
                    var csharpType = GlslToCSharpType(member.Type, member.ArraySize);
                    sb.AppendLine($"        public {csharpType} {ToPascalCase(member.Name)};");
                }

                sb.AppendLine("    }");
                sb.AppendLine();
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        private string GenerateGlobalBindings(List<ShaderBindingInfo> bindings)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#pragma warning disable");
            sb.AppendLine("using System;");
            sb.AppendLine();
            sb.AppendLine("namespace GeneratedShaders");
            sb.AppendLine("{");
            sb.AppendLine("    public static class ShaderBindings");
            sb.AppendLine("    {");

            // Group by shader file name
            var shaderGroups = bindings.GroupBy(b => b.ShaderFileName);

            foreach (var shaderGroup in shaderGroups)
            {
                var safeShaderName = GetSafeClassName(shaderGroup.Key);
                sb.AppendLine($"        public static class {safeShaderName}");
                sb.AppendLine("        {");

                // Group by struct name to avoid duplicates
                var uniqueStructs = shaderGroup.GroupBy(b => b.StructName).Select(g => g.First());

                foreach (var binding in uniqueStructs)
                {
                    if (binding.IsPushConstant)
                    {
                        sb.AppendLine($"            public static class {binding.StructName}");
                        sb.AppendLine("            {");
                        sb.AppendLine($"                public const bool IsPushConstant = true;");
                        sb.AppendLine($"                public const string ShaderFile = \"{shaderGroup.Key}\";");
                        sb.AppendLine("            }");
                    }
                    else
                    {
                        sb.AppendLine($"            public static class {binding.StructName}");
                        sb.AppendLine("            {");
                        sb.AppendLine($"                public const int Set = {binding.Set};");
                        sb.AppendLine($"                public const int Binding = {binding.Binding};");
                        sb.AppendLine($"                public const string ShaderFile = \"{shaderGroup.Key}\";");
                        sb.AppendLine("            }");
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("        }");
                sb.AppendLine();
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private string GlslToCSharpType(string glslType, int arraySize)
        {
            var baseType = glslType.ToLower() switch
            {
                "float" => "float",
                "double" => "double",
                "int" => "int",
                "uint" => "uint",
                "bool" => "bool",
                "vec2" => "Vector2",
                "vec3" => "Vector3",
                "vec4" => "Vector4",
                "mat4" => "Matrix4x4",
                "mat3" => "Matrix4x4",
                "mat4x4" => "Matrix4x4",
                "mat3x3" => "Matrix4x4",
                "ivec2" => "Vector2",
                "ivec3" => "Vector3",
                "ivec4" => "Vector4",
                "uvec2" => "Vector2",
                "uvec3" => "Vector3",
                "uvec4" => "Vector4",
                "bvec2" => "Vector2",
                "bvec3" => "Vector3",
                "bvec4" => "Vector4",
                _ => "float"
            };

            if (arraySize > 0)
            {
                return $"{baseType}[{arraySize}]";
            }

            return baseType;
        }

        private string ToPascalCase(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            if (name.StartsWith("u_") || name.StartsWith("v_") || name.StartsWith("a_"))
            {
                name = name.Substring(2);
            }

            if (name.Length == 1)
                return name.ToUpper();

            return char.ToUpper(name[0]) + name.Substring(1);
        }

        private string GetSafeClassName(string fileName)
        {
            var invalidChars = new char[] { ' ', '-', '.', ',', ';', ':', '/', '\\' };
            var className = new string(fileName.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());

            if (className.Length > 0 && !char.IsLetter(className[0]))
            {
                className = "Shader_" + className;
            }

            return className;
        }*/
    }

    internal class ShaderStruct
    {
        public string Name { get; set; }
        public bool IsPushConstant { get; set; }
        public int Set { get; set; } = -1;
        public int Binding { get; set; } = -1;
        public string InstanceName { get; set; }
        public List<ShaderStructMember> Members { get; set; } = new List<ShaderStructMember>();
    }

    internal class ShaderStructMember
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public int ArraySize { get; set; }
    }

    internal class ShaderBindingInfo
    {
        public string ShaderFileName { get; set; }
        public string ShaderFilePath { get; set; }
        public string StructName { get; set; }
        public bool IsPushConstant { get; set; }
        public int Set { get; set; }
        public int Binding { get; set; }
    }
