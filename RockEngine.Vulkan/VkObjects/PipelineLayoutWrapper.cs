using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects.Reflected;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

using System.Diagnostics;

namespace RockEngine.Vulkan.VkObjects
{
    public class PipelineLayoutWrapper : VkObject<PipelineLayout>
    {
        public readonly PushConstantRange[] PushConstantRanges;
        public readonly DescriptorSetLayoutWrapper[] DescriptorSetLayouts;
        private readonly VulkanContext _context;

        /// <summary>
        /// Global set layouts such as model and camera data
        /// </summary>
        private static DescriptorSetLayoutWrapper[]? _globalSetLayouts;

        private PipelineLayoutWrapper(VulkanContext context, PipelineLayout layout, PushConstantRange[] pushConstantRanges, DescriptorSetLayoutWrapper[] descriptorSetLayouts)
            : base(layout)
        {
            PushConstantRanges = pushConstantRanges;
            DescriptorSetLayouts = descriptorSetLayouts;
            _context = context;
            
        }

        public static unsafe DescriptorSetLayoutWrapper[] CreateGlobalDescriptorLayout(VulkanContext context)
        {
            var setLayouts = new DescriptorSetLayoutWrapper[2];
            var cameraBindings = stackalloc DescriptorSetLayoutBinding[]
            {
                new DescriptorSetLayoutBinding
                {
                    Binding = 0,
                    DescriptorType = DescriptorType.UniformBuffer,
                    DescriptorCount = 1,
                    StageFlags = ShaderStageFlags.VertexBit,
                    PImmutableSamplers = null
                },
            };

            var layoutInfo = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = 1,
                PBindings = cameraBindings
            };
            context.Api.CreateDescriptorSetLayout(context.Device, in layoutInfo, null, out var cameraDescriptorSetLayout)
                .ThrowCode("Failed to create global descriptor set layout");
            setLayouts[0] = new DescriptorSetLayoutWrapper(cameraDescriptorSetLayout, 0, [new DescriptorSetLayoutBindingReflected("CameraData", cameraBindings[0])]);

            var modelBindings = stackalloc DescriptorSetLayoutBinding[]
           {
                new DescriptorSetLayoutBinding
                {
                    Binding = 0,
                    DescriptorType = DescriptorType.UniformBuffer,
                    DescriptorCount = 1,
                    StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                    PImmutableSamplers = null
                },
            };

            layoutInfo = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = 1,
                PBindings = modelBindings
            };

            context.Api.CreateDescriptorSetLayout(context.Device, in layoutInfo, null, out var modelDescriptorSetLayout)
                .ThrowCode("Failed to create global descriptor set layout");

            setLayouts[1] = new DescriptorSetLayoutWrapper(
                modelDescriptorSetLayout,
                Constants.MODEL_SET, 
                [new DescriptorSetLayoutBindingReflected("Model", modelBindings[0])]
                );
            return setLayouts;
        }

        public static unsafe PipelineLayoutWrapper Create(VulkanContext context, bool useGlobalSetLayout = true, params ShaderModuleWrapper[] shaders)
        {
            _globalSetLayouts ??= CreateGlobalDescriptorLayout(context);
            var descriptorSetLayoutsWrapped = CreateDescriptorSetLayouts(context, shaders, useGlobalSetLayout);
            var pushConstantRanges = shaders.SelectMany(s => s.ConstantRanges).ToArray();
            var descriptorSetLayouts = descriptorSetLayoutsWrapped.Select(s => s.DescriptorSetLayout).ToArray();

            fixed (DescriptorSetLayout* setLayout = descriptorSetLayouts)
            fixed (PushConstantRange* constantRange = pushConstantRanges)
            {
                var layoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = (uint)descriptorSetLayoutsWrapped.Length,
                    PSetLayouts = setLayout,
                    PushConstantRangeCount = (uint)pushConstantRanges.Length,
                    PPushConstantRanges = constantRange
                };
                context.Api.CreatePipelineLayout(context.Device, &layoutInfo, null, out var pipelineLayout)
                    .ThrowCode("Failed to create pipeline layout");
                return new PipelineLayoutWrapper(context, pipelineLayout, pushConstantRanges,
                    descriptorSetLayoutsWrapped);
            }
        }

        private static unsafe DescriptorSetLayoutWrapper[] CreateDescriptorSetLayouts(VulkanContext context, ShaderModuleWrapper[] shaders, bool useGlobalSetLayout)
        {
            var setLayouts = new List<DescriptorSetLayoutWrapper>();

            if (useGlobalSetLayout)
            {
                setLayouts.AddRange(_globalSetLayouts);
            }

            var descriptorSetLayoutsReflected = shaders.SelectMany(s => s.DescriptorSetLayouts)
                                                       .GroupBy(d => d.Set)
                                                       .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var set in descriptorSetLayoutsReflected)
            {
                // Skip set if global layout is provided on same set
                if (useGlobalSetLayout && _globalSetLayouts.Any(s => s.SetLocation == set.Key))
                {
                    Debugger.Log(1, "Set layout", $"Skipping set layout at set {set.Key}");

                    continue;
                }

                var bindings = new List<DescriptorSetLayoutBinding>();

                foreach (var layout in set.Value)
                {
                    bindings.AddRange(layout.Bindings.Select(b => new DescriptorSetLayoutBinding
                    {
                        Binding = b.Binding,
                        DescriptorType = b.DescriptorType,
                        DescriptorCount = b.DescriptorCount,
                        StageFlags = b.StageFlags,
                        PImmutableSamplers = b.PImmutableSamplers
                    }));
                }
                var bindingsArr = bindings.ToArray();
                fixed (DescriptorSetLayoutBinding* pBindings = bindingsArr)
                {
                    var layoutInfo = new DescriptorSetLayoutCreateInfo
                    {
                        SType = StructureType.DescriptorSetLayoutCreateInfo,
                        BindingCount = (uint)bindings.Count,
                        PBindings = pBindings
                    };
                    context.Api.CreateDescriptorSetLayout(context.Device, in layoutInfo, null, out var descriptorSetLayout)
                        .ThrowCode("Failed to create descriptor set layout");
                    setLayouts.Add(new DescriptorSetLayoutWrapper(descriptorSetLayout, set.Key, set.Value.SelectMany(s=>s.Bindings).ToArray()));
                }
            }

            return setLayouts.ToArray();
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects) if any.
                }

                // Free unmanaged resources (unmanaged objects) and override a finalizer below.
                // Set large fields to null.
                if (_vkObject.Handle != 0)
                {
                    unsafe
                    {
                        foreach (var item in DescriptorSetLayouts)
                        {
                            _context.Api.DestroyDescriptorSetLayout(_context.Device, item.DescriptorSetLayout, null);
                        }

                        _context.Api.DestroyPipelineLayout(_context.Device, _vkObject, null);
                    }
                    _vkObject = default;
                }

                _disposed = true;
            }
        }
    }
}