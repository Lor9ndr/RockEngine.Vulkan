using Silk.NET.Core.Native;
using Silk.NET.SPIRV.Reflect;
using Silk.NET.Vulkan;

using System.Runtime.InteropServices;

using DescriptorType = Silk.NET.SPIRV.Reflect.DescriptorType;

namespace RockEngine.Vulkan
{
    public sealed class ShaderReflectionData
    {
        private const string UNDEFINED_NAME = "UNDEFINED";
        private const string DYNAMIC_UBO_END = "_Dynamic";

        private readonly ShaderStageFlags _stage;

        public List<DescriptorSetInfo> DescriptorSets { get; } = new();
        public List<PushConstantInfo> PushConstants { get; } = new();
        public List<InputVariable> InputVariables { get; } = new();
        public List<UniformBufferInfo> UniformBuffers { get; } = new();
        public List<SamplerInfo> Samplers { get; } = new();
        public List<ImageInfo> Images { get; } = new();

        public ShaderReflectionData(in ReflectShaderModule reflectShaderModule, ShaderStageFlags stage)
        {
            _stage = stage;
            ReflectShader(in reflectShaderModule);
        }
        private unsafe void ReflectShader(in ReflectShaderModule reflectShaderModule)
        {
            var reflectorApi = Reflect.GetApi();

            // Extract input variables
            ReflectInputVariables(reflectorApi, reflectShaderModule);

            // Extract descriptor sets
            ReflectDescriptorSets(reflectorApi, reflectShaderModule);

            // Extract push constants
            ReflectPushConstants(reflectorApi, reflectShaderModule);
        }


        private unsafe void ReflectInputVariables(Reflect reflectorApi, ReflectShaderModule reflectShaderModule)
        {
            uint variableCount = 0;
            reflectorApi.EnumerateInputVariables(in reflectShaderModule, &variableCount, null);
            var variables = new InterfaceVariable*[variableCount];

            fixed (InterfaceVariable** iVariable = variables)
            {
                reflectorApi.EnumerateInputVariables(in reflectShaderModule, &variableCount, iVariable);

                for (int i = 0; i < variableCount; i++)
                {
                    var variable = variables[i];
                    InputVariables.Add(new InputVariable
                    {
                        Name = Marshal.PtrToStringAnsi((nint)variable->Name),
                        Location = variable->Location,
                        Type = MapShaderVariableType(variable->TypeDescription)
                    });
                }
            }
        }

        private unsafe void ReflectDescriptorSets(Reflect reflectorApi, ReflectShaderModule reflectShaderModule)
        {
            uint descriptorSetCount = 0;
            ReflectDescriptorSet** descriptorSets = null;
            reflectorApi.EnumerateDescriptorSets(in reflectShaderModule, ref descriptorSetCount, descriptorSets);
            descriptorSets = (ReflectDescriptorSet**)SilkMarshal.Allocate((int)descriptorSetCount * sizeof(ReflectDescriptorSet*));
            reflectorApi.EnumerateDescriptorSets(in reflectShaderModule, ref descriptorSetCount, descriptorSets);

            for (int i = 0; i < descriptorSetCount; i++)
            {
                var descriptorSet = descriptorSets[i];
                var bindings = new List<DescriptorSetLayoutBindingReflected>();

                for (int j = 0; j < descriptorSet->BindingCount; j++)
                {
                    var binding = descriptorSet->Bindings[j];
                    ProcessDescriptorBinding(reflectorApi, binding, bindings);
                }

                DescriptorSets.Add(new DescriptorSetInfo
                {
                    Set = descriptorSet->Set,
                    Bindings = bindings.ToArray()
                });
            }
            SilkMarshal.Free((nint)descriptorSets);
        }

        private unsafe void ProcessDescriptorBinding(Reflect reflectorApi, DescriptorBinding* binding, List<DescriptorSetLayoutBindingReflected> bindings)
        {
            string name = GetBindingName(reflectorApi, binding);
            var descriptorType = GetDescriptorType(binding, name);

            bindings.Add(new DescriptorSetLayoutBindingReflected(
                name: name,
                binding: binding->Binding,
                descriptorType: descriptorType,
                descriptorCount: binding->Count,
                stageFlags: _stage,
                pImmutableSamplers: default
            ));

            StoreResourceInfo(binding, name, descriptorType);
        }

        private unsafe string GetBindingName(Reflect reflectorApi, DescriptorBinding* binding)
        {
            return binding->DescriptorType switch
            {
                DescriptorType.UniformBuffer => SilkMarshal.PtrToString((nint)reflectorApi.BlockVariableTypeName(ref binding->Block)) ?? UNDEFINED_NAME,
                _ => SilkMarshal.PtrToString((nint)binding->Name) ?? UNDEFINED_NAME
            };
        }

        private unsafe Silk.NET.Vulkan.DescriptorType GetDescriptorType(DescriptorBinding* binding, string name)
        {
            if (binding->DescriptorType == DescriptorType.UniformBuffer && name.EndsWith(DYNAMIC_UBO_END))
            {
                return Silk.NET.Vulkan.DescriptorType.UniformBufferDynamic;
            }
            return (Silk.NET.Vulkan.DescriptorType)binding->DescriptorType;
        }

        private unsafe void StoreResourceInfo(DescriptorBinding* binding, string name, Silk.NET.Vulkan.DescriptorType descriptorType)
        {
            switch (descriptorType)
            {
                case Silk.NET.Vulkan.DescriptorType.UniformBuffer:
                case Silk.NET.Vulkan.DescriptorType.UniformBufferDynamic:
                    UniformBuffers.Add(new UniformBufferInfo
                    {
                        Name = name,
                        Binding = binding->Binding,
                        Set = binding->Set,
                        Size = binding->Block.Size,
                        IsDynamic = descriptorType == Silk.NET.Vulkan.DescriptorType.UniformBufferDynamic ||  name.EndsWith(DYNAMIC_UBO_END)
                    });
                    break;

                case Silk.NET.Vulkan.DescriptorType.Sampler:
                    Samplers.Add(new SamplerInfo
                    {
                        Name = name,
                        Binding = binding->Binding,
                        Set = binding->Set
                    });
                    break;

                case Silk.NET.Vulkan.DescriptorType.CombinedImageSampler:
                    Images.Add(new ImageInfo
                    {
                        Name = name,
                        Binding = binding->Binding,
                        Set = binding->Set
                    });
                    break;
            }
        }

        private unsafe void ReflectPushConstants(Reflect reflectorApi, ReflectShaderModule reflectShaderModule)
        {
            uint pushConstantCount = 0;
            reflectorApi.EnumeratePushConstants(in reflectShaderModule, &pushConstantCount, null);
            var pushConstants = new BlockVariable*[pushConstantCount];

            fixed (BlockVariable** iPushConstant = pushConstants)
            {
                reflectorApi.EnumeratePushConstants(in reflectShaderModule, &pushConstantCount, iPushConstant);

                for (int i = 0; i < pushConstantCount; i++)
                {
                    var pushConstant = pushConstants[i];
                    PushConstants.Add(new ShaderReflectionData.PushConstantInfo
                    {
                        Name = Marshal.PtrToStringAnsi((nint)pushConstant->Name) ?? "UNNAMED",
                        StageFlags = _stage,
                        Offset = pushConstant->Offset,
                        Size = pushConstant->Size
                    });
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
            return ShaderVariableType.Custom;
            throw new NotSupportedException($"Unsupported shader variable type with traits: {traits}");
        }

        public struct DescriptorSetInfo
        {
            public uint Set;
            public DescriptorSetLayoutBindingReflected[] Bindings;
        }

        public struct PushConstantInfo
        {
            public string Name { get; set; }
            public ShaderStageFlags StageFlags { get; set; }
            public uint Offset { get; set; }
            public uint Size { get; set; }
            public byte[] Value;
            public PushConstantRange ToPushConstantRangeVulkan() => new PushConstantRange() { Offset = Offset, Size = Size, StageFlags = StageFlags };
        }

        public struct InputVariable
        {
            public string Name;
            public uint Location;
            public ShaderVariableType Type;
        }

        public struct UniformBufferInfo
        {
            public string Name;
            public uint Binding;
            public uint Set;
            public uint Size;
            public bool IsDynamic;
        }

        public struct SamplerInfo
        {
            public string Name;
            public uint Binding;
            public uint Set;
        }

        public struct ImageInfo
        {
            public string Name;
            public uint Binding;
            public uint Set;
        }

        public enum ShaderVariableType
        {
            Float,
            Vec2,
            Vec3,
            Vec4,
            Mat4,
            Int,
            Custom
        }
    }

}
