using Silk.NET.Vulkan;
using Silk.NET.SPIRV.Reflect;
using DescriptorType = Silk.NET.SPIRV.Reflect.DescriptorType;

namespace RockEngine.Vulkan.VkObjects
{
    internal class ShaderModuleWrapper : VkObject
    {
        private readonly Vk _api;
        private readonly ShaderModule _module;
        private readonly LogicalDeviceWrapper _device;
        private readonly ShaderStageFlags _stage;
        private readonly List<ShaderVariable> _variables;
        private readonly List<UniformBufferObject> _ubos;
        private readonly List<SamplerObject> _samplers;
        private readonly List<ImageObject> _images;
        private readonly ReflectShaderModule _reflectShaderModule;

        public ShaderModule Module => _module;
        public ShaderStageFlags Stage => _stage;
        public IReadOnlyList<ShaderVariable> Variables => _variables;
        public IReadOnlyList<UniformBufferObject> UBOs => _ubos;
        public IReadOnlyList<SamplerObject> Samplers => _samplers;
        public IReadOnlyList<ImageObject> Images => _images;

        public ShaderModuleWrapper(Vk api, ShaderModule module, LogicalDeviceWrapper device, ShaderStageFlags stage, ref ReflectShaderModule reflectShaderModule)
        {
            _api = api;
            _module = module;
            _device = device;
            _stage = stage;
            _variables = new List<ShaderVariable>();
            _ubos = new List<UniformBufferObject>();
            _samplers = new List<SamplerObject>();
            _images = new List<ImageObject>();
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
                    _variables.Add(new ShaderVariable
                    {
                        Name = new string((sbyte*)variable->Name),
                        Location = variable->Location,
                        Type = MapShaderVariableType(variable->TypeDescription)
                    });
                }
            }

            // Extract descriptor sets
            uint descriptorSetCount = 0;
            reflectorApi.EnumerateDescriptorSets(in _reflectShaderModule, &descriptorSetCount, null);
            var descriptorSets = new ReflectDescriptorSet*[descriptorSetCount];
            fixed (ReflectDescriptorSet** iDescriptorSet = descriptorSets)
            {
                reflectorApi.EnumerateDescriptorSets(in _reflectShaderModule, &descriptorSetCount, iDescriptorSet);

                for (int i = 0; i < descriptorSetCount; i++)
                {
                    var descriptorSet = descriptorSets[i];
                    for (int j = 0; j < descriptorSet->BindingCount; j++)
                    {
                        var binding = descriptorSet->Bindings[j];
                        switch (binding->DescriptorType)
                        {
                            case DescriptorType.UniformBuffer:
                                _ubos.Add(new UniformBufferObject
                                {
                                    Name = new string((char*)binding->Name),
                                    Binding = binding->Binding,
                                    Size = binding->Block.Size
                                });
                                break;
                            case DescriptorType.Sampler:
                                _samplers.Add(new SamplerObject
                                {
                                    Name = new string((char*)binding->Name),
                                    Binding = binding->Binding
                                });
                                break;
                            case DescriptorType.SampledImage:
                                _images.Add(new ImageObject
                                {
                                    Name = new string((char*)binding->Name),
                                    Binding = binding->Binding
                                });
                                break;
                                // Add other descriptor types as needed
                        }
                    }
                }
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

            throw new NotSupportedException($"Unsupported shader variable type with traits: {traits}");
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                if (_module.Handle != default)
                {
                    unsafe
                    {
                        _api.DestroyShaderModule(_device.Device, _module, null);
                    }
                }

                _disposed = true;
            }
        }

        internal class ShaderVariable
        {
            public string Name { get; set; }
            public uint Location { get; set; }
            public ShaderVariableType Type { get; set; }
        }

        internal class UniformBufferObject
        {
            public string Name { get; set; }
            public uint Binding { get; set; }
            public uint Size { get; set; }
        }

        internal class SamplerObject
        {
            public string Name { get; set; }
            public uint Binding { get; set; }
        }

        internal class ImageObject
        {
            public string Name { get; set; }
            public uint Binding { get; set; }
        }

        internal enum ShaderVariableType
        {
            Float,
            Vec2,
            Vec3,
            Vec4,
            Mat4,
            Int,
            // Add other types as needed
        }
    }
}