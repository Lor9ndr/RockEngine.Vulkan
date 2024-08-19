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


        private PipelineLayoutWrapper(VulkanContext context, PipelineLayout layout, PushConstantRange[] pushConstantRanges, DescriptorSetLayoutWrapper[] descriptorSetLayouts)
            : base(layout)
        {
            PushConstantRanges = pushConstantRanges;
            DescriptorSetLayouts = descriptorSetLayouts;
            _context = context;
        }


        public static unsafe PipelineLayoutWrapper Create(VulkanContext context,  params ShaderModuleWrapper[] shaders)
        {
            //_globalSetLayouts ??= CreateGlobalDescriptorLayout(context);
            var descriptorSetLayoutsWrapped = CreateDescriptorSetLayouts(context, shaders);
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

        private static unsafe DescriptorSetLayoutWrapper[] CreateDescriptorSetLayouts(VulkanContext context, ShaderModuleWrapper[] shaders)
        {
            var setLayouts = new List<DescriptorSetLayoutWrapper>();
           
            var descriptorSetLayoutsReflected = shaders.SelectMany(s => s.DescriptorSetLayouts)
                                                       .GroupBy(d => d.Set)
                                                       .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var set in descriptorSetLayoutsReflected)
            {

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