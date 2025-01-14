using Silk.NET.Vulkan;
using Silk.NET.SPIRV.Reflect;
using DescriptorType = Silk.NET.SPIRV.Reflect.DescriptorType;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;

namespace RockEngine.Vulkan
{
    public record VkShaderModule : VkObject<ShaderModule>
    {
        private const string UNDEFINED_NAME = "UNDEFINED";
        private const string DYNAMIC_UBO_END = "_Dynamic";
        private readonly RenderingContext _context;
        private readonly ShaderStageFlags _stage;

        private readonly List<ShaderVariableReflected> _variables = new List<ShaderVariableReflected>();
        private readonly List<UniformBufferObjectReflected> _reflectedUbos = new List<UniformBufferObjectReflected>();
        private readonly List<SamplerObjectReflected> _samplers = new List<SamplerObjectReflected>();
        private readonly List<ImageObjectReflected> _images = new List<ImageObjectReflected>();
        private readonly List<PushConstantRange> _constantRanges = new List<PushConstantRange>();
        private readonly List<DescriptorSetLayoutReflected> _descriptorSetLayouts = new List<DescriptorSetLayoutReflected>();

        private readonly ReflectShaderModule _reflectShaderModule;

        /// <summary>
        /// Shader stage (e.g. fragment, vertex)
        /// </summary>
        public ShaderStageFlags Stage => _stage;

        /// <summary>
        /// Input variables
        /// </summary>
        public IReadOnlyList<ShaderVariableReflected> Variables => _variables;
        public IReadOnlyList<UniformBufferObjectReflected> ReflectedUbos => _reflectedUbos;
        public IReadOnlyList<SamplerObjectReflected> Samplers => _samplers;
        public IReadOnlyList<ImageObjectReflected> Images => _images;
        public IReadOnlyList<PushConstantRange> ConstantRanges => _constantRanges;
        internal IReadOnlyList<DescriptorSetLayoutReflected> DescriptorSetLayouts => _descriptorSetLayouts;

        public VkShaderModule(RenderingContext context, ShaderModule module, ShaderStageFlags stage, ref ReflectShaderModule reflectShaderModule)
            : base(module)
        {
            _context = context;
            _stage = stage;

            _reflectShaderModule = reflectShaderModule;

            // Initialize shader reflection to populate variables and UBOs
            ReflectShader();
        }

        private unsafe void ReflectShader()
        {
            var reflectorApi = Reflect.GetApi();

            // Extract shader variables
            uint variableCount = 0;
            reflectorApi.EnumerateInputVariables(in _reflectShaderModule, &variableCount, null);
            var variables = new InterfaceVariable*[variableCount];
            fixed (InterfaceVariable** iVariable = variables)
            {
                reflectorApi.EnumerateInputVariables(in _reflectShaderModule, &variableCount, iVariable);

                for (int i = 0; i < variableCount; i++)
                {
                    var variable = variables[i];
                    _variables.Add(new ShaderVariableReflected
                    {
                        Name = Marshal.PtrToStringAnsi((nint)variable->Name),
                        Location = variable->Location,
                        ShaderStage = Stage,
                        Type = MapShaderVariableType(variable->TypeDescription)
                    });
                }
            }

            // Extract descriptor sets
            uint descriptorSetCount = 0;
            ReflectDescriptorSet** descriptorSets = null;
            reflectorApi.EnumerateDescriptorSets(in _reflectShaderModule, ref descriptorSetCount, descriptorSets);
            // Allocate memory for the descriptor sets
            descriptorSets = (ReflectDescriptorSet**)SilkMarshal.Allocate((int)descriptorSetCount * sizeof(ReflectDescriptorSet*));
            reflectorApi.EnumerateDescriptorSets(in _reflectShaderModule, ref descriptorSetCount, descriptorSets);


            for (int i = 0; i < descriptorSetCount; i++)
            {
                var descriptorSet = descriptorSets[i];
                var bindings = new List<DescriptorSetLayoutBindingReflected>();
                for (int j = 0; j < descriptorSet->BindingCount; j++)
                {
                    var binding = descriptorSet->Bindings[j];

                    string name;
                    if (binding->DescriptorType == DescriptorType.UniformBuffer)
                    {
                        name = SilkMarshal.PtrToString((nint)reflectorApi.BlockVariableTypeName(ref binding->Block)) ?? UNDEFINED_NAME;
                        _reflectedUbos.Add(new UniformBufferObjectReflected
                        {
                            Name = name,
                            Binding = binding->Binding,
                            Size = binding->Block.Size,
                            Set = binding->Set,
                            ShaderStage = _stage,
                        });
                    }
                    else if (binding->DescriptorType == DescriptorType.Sampler)
                    {
                        name = SilkMarshal.PtrToString((nint)binding->Name) ?? UNDEFINED_NAME;

                        _samplers.Add(new SamplerObjectReflected
                        {
                            Name = name,
                            Binding = binding->Binding,
                            Set = binding->Set,
                            ShaderStage = _stage
                        });
                    }
                    else if (binding->DescriptorType == DescriptorType.CombinedImageSampler)
                    {
                        name = SilkMarshal.PtrToString((nint)binding->Name) ?? UNDEFINED_NAME;

                        _images.Add(new ImageObjectReflected
                        {
                            Name = name,
                            Binding = binding->Binding,
                            Set = binding->Set,
                            ShaderStage = _stage
                        });
                    }
                    else
                    {
                        name = SilkMarshal.PtrToString((nint)binding->Name) ?? UNDEFINED_NAME;
                    }
                    var bindingReflected = new DescriptorSetLayoutBindingReflected(null,
                                                                                  binding->Binding,
                                                                                   ShouldBeDynamicUniformBuffer(binding, name) ?
                                                                                            Silk.NET.Vulkan.DescriptorType.UniformBufferDynamic :
                                                                                            (Silk.NET.Vulkan.DescriptorType)binding->DescriptorType,
                                                                                  binding->Count,
                                                                                  _stage,
                                                                                  default);
                    bindingReflected.Name = name;
                    bindings.Add(bindingReflected);
                }

                _descriptorSetLayouts.Add(new DescriptorSetLayoutReflected
                {
                    Set = descriptorSet->Set,
                    Bindings = bindings.ToArray()
                });
            }

            SilkMarshal.Free((nint)descriptorSets);

            // Extract push constants
            uint pushConstantCount = 0;
            reflectorApi.EnumeratePushConstants(in _reflectShaderModule, &pushConstantCount, null);
            var pushConstants = new BlockVariable*[pushConstantCount];
            fixed (BlockVariable** iPushConstant = pushConstants)
            {
                reflectorApi.EnumeratePushConstants(in _reflectShaderModule, &pushConstantCount, iPushConstant);

                for (int i = 0; i < pushConstantCount; i++)
                {
                    var pushConstant = pushConstants[i];
                    _constantRanges.Add(new PushConstantRange
                    {
                        StageFlags = _stage,
                        Offset = pushConstant->Offset,
                        Size = pushConstant->Size
                    });
                }
            }
        }
        private unsafe bool ShouldBeDynamicUniformBuffer(DescriptorBinding* binding, string name)
        {
            return binding->DescriptorType == DescriptorType.UniformBuffer && name.EndsWith(DYNAMIC_UBO_END);
        }

        static unsafe string ConvertBytePointerToString(byte* bytePointer)
        {
            if (bytePointer == null)
            {
                return string.Empty;
            }

            int length = 16;
            while (bytePointer[length] != 0)
            {
                length++;
            }

            return new string((sbyte*)bytePointer, 0, length);
        }

        public static async Task<VkShaderModule> CreateAsync(RenderingContext context, string path, ShaderStageFlags stage, CancellationToken cancellationToken = default)
        {
            var shaderCode = await File.ReadAllBytesAsync(path, cancellationToken)
               .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            return Create(context, shaderCode, stage);
        }

        public static VkShaderModule Create(RenderingContext context, string path, ShaderStageFlags stage)
        {
            var shaderCode = File.ReadAllBytes(path);
            return Create(context, shaderCode, stage);
        }

        public unsafe static VkShaderModule Create(RenderingContext context, byte[] bytes, ShaderStageFlags stage)
        {
            fixed (byte* pshaderCode = bytes)
            {
                var shaderModuleCreateInfo = new ShaderModuleCreateInfo
                {
                    SType = StructureType.ShaderModuleCreateInfo,
                    CodeSize = (nuint)bytes.Length,
                    PCode = (uint*)pshaderCode
                };
                RenderingContext.Vk.CreateShaderModule(
                    context.Device, 
                    in shaderModuleCreateInfo,
                    in RenderingContext.CustomAllocator<VkShaderModule>(),
                    out var shaderModule).VkAssertResult($"Failed to create shader module: {stage}");

                var reflectorApi = Reflect.GetApi();
                var reflected = new ReflectShaderModule(Generator.KhronosSpirvToolsAssembler);
                reflectorApi.CreateShaderModule((nuint)bytes.Length, pshaderCode, ref reflected);

                return new VkShaderModule(context, shaderModule, stage, ref reflected);
            }
        }
        public unsafe static VkShaderModule Create(RenderingContext context, uint[] data, ShaderStageFlags stage)
        {
            fixed (uint* pshaderCode = data)
            {
                var shaderModuleCreateInfo = new ShaderModuleCreateInfo
                {
                    SType = StructureType.ShaderModuleCreateInfo,
                    CodeSize = (nuint)(data.Length * sizeof(uint)),
                    PCode = pshaderCode
                };
                RenderingContext.Vk.CreateShaderModule(context.Device, in shaderModuleCreateInfo, in RenderingContext.CustomAllocator<VkShaderModule>(), out var shaderModule)
                    .VkAssertResult($"Failed to create shader module: {stage}");
                var reflectorApi = Reflect.GetApi();
                var reflected = new ReflectShaderModule(Generator.KhronosSpirvToolsAssembler);
                reflectorApi.CreateShaderModule((nuint)data.Length, pshaderCode, ref reflected);

                return new VkShaderModule(context, shaderModule, stage, ref reflected);
            }
        }

        private unsafe ShaderVariableType MapShaderVariableType(TypeDescription* typeDescription)
        {
            // Check the traits to determine the type of the variable
            var traits = typeDescription->Traits;

            if (traits.Numeric.Matrix.ColumnCount > 0 && traits.Numeric.Matrix.RowCount > 0)
            {
                if (traits.Numeric.Matrix.ColumnCount == 4 && traits.Numeric.Matrix.RowCount == 4)
                {
                    return ShaderVariableType.Mat4;
                }
            }
            else if (traits.Numeric.Vector.ComponentCount > 0)
            {
                switch (traits.Numeric.Vector.ComponentCount)
                {
                    case 2:
                        return ShaderVariableType.Vec2;
                    case 3:
                        return ShaderVariableType.Vec3;
                    case 4:
                        return ShaderVariableType.Vec4;
                }
            }
            // Check if the type is numeric
            else if (traits.Numeric.Scalar.Width > 0)
            {
                // Check if the type is an integer
                if (traits.Numeric.Scalar.Signedness == 1)
                {
                    return ShaderVariableType.Int;
                }
                else
                {
                    return ShaderVariableType.Float;
                }
            }

            // Add other cases as needed
            return ShaderVariableType.Custom;
            throw new NotSupportedException($"Unsupported shader variable type with traits: {traits}");
        }
        protected unsafe override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                if (_vkObject.Handle != default)
                {
                    RenderingContext.Vk.DestroyShaderModule(_context.Device, _vkObject, in RenderingContext.CustomAllocator<VkShaderModule>());
                }

                _disposed = true;
            }
        }
    }
}