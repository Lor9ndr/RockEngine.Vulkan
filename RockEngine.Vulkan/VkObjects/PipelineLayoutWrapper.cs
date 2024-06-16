using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects.Reflected;
using RockEngine.Vulkan.VulkanInitilizers;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkObjects
{
    public class PipelineLayoutWrapper : VkObject<PipelineLayout>
    {
        public readonly PushConstantRange[] PushConstantRanges;
        public readonly DescriptorSetLayoutWrapper[] DescriptorSetLayouts;
        private readonly VulkanContext _context;


        private PipelineLayoutWrapper(VulkanContext context, PipelineLayout layout, PushConstantRange[] pushConstantRanges, DescriptorSetLayoutWrapper[] descriptorSetLayouts)
            :base(layout)
        {
            PushConstantRanges = pushConstantRanges;
            DescriptorSetLayouts = descriptorSetLayouts;
            _context = context;
        }

        public static unsafe PipelineLayoutWrapper Create(VulkanContext context, UniformBufferObjectReflected[] uniformsReflected, PushConstantRange[] pushConstantRanges)
        {
            var descriptorSetLayouts = CreateDescriptorSetLayouts(context, uniformsReflected);
            var setLayouts = descriptorSetLayouts.Select(s => s.DescriptorSetLayout).ToArray();

            fixed(DescriptorSetLayout* setLayout =  setLayouts)
            fixed(PushConstantRange* constantRange = pushConstantRanges)
            {
                var layoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = (uint)descriptorSetLayouts.Length,
                    PSetLayouts = setLayout,
                    PushConstantRangeCount = (uint)pushConstantRanges.Length,
                    PPushConstantRanges = constantRange
                };
                context.Api.CreatePipelineLayout(context.Device, &layoutInfo, null, out var pipelineLayout)
                    .ThrowCode("Failed to create pipeline layout");
                return new PipelineLayoutWrapper(context, pipelineLayout, pushConstantRanges, descriptorSetLayouts);
            }
        }
        public static unsafe PipelineLayoutWrapper Create(VulkanContext context, DescriptorSetLayout[] descriptorSetLayouts, PushConstantRange[] pushConstantRanges)
        {

            fixed (DescriptorSetLayout* setLayout = descriptorSetLayouts)
            fixed (PushConstantRange* constantRange = pushConstantRanges)
            {
                var layoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = (uint)descriptorSetLayouts.Length,
                    PSetLayouts = setLayout,
                    PushConstantRangeCount = (uint)pushConstantRanges.Length,
                    PPushConstantRanges = constantRange
                };
                context.Api.CreatePipelineLayout(context.Device, &layoutInfo, null, out var pipelineLayout)
                    .ThrowCode("Failed to create pipeline layout");
                return new PipelineLayoutWrapper(context, pipelineLayout, pushConstantRanges, 
                    descriptorSetLayouts.Select(s => new DescriptorSetLayoutWrapper(s)).ToArray());
            }
        }

        private static unsafe DescriptorSetLayoutWrapper[] CreateDescriptorSetLayouts(VulkanContext context, UniformBufferObjectReflected[] reflectedUbos)
        {
            var groupedUboSets = reflectedUbos.GroupBy(ubo => ubo.Set);

            List<DescriptorSetLayoutWrapper> setLayouts = new List<DescriptorSetLayoutWrapper>();
            foreach (var ubo in reflectedUbos)
            {
                var bindings = new List<DescriptorSetLayoutBinding>
                {
                    new DescriptorSetLayoutBinding
                    {
                        Binding = ubo.Binding,
                        DescriptorType = DescriptorType.UniformBuffer,
                        DescriptorCount = 1,
                        StageFlags = ubo.ShaderStage,
                        PImmutableSamplers = null
                    }
                };

                fixed (DescriptorSetLayoutBinding* pBindings = bindings.ToArray())
                {
                    var layoutInfo = new DescriptorSetLayoutCreateInfo
                    {
                        SType = StructureType.DescriptorSetLayoutCreateInfo,
                        BindingCount = (uint)bindings.Count,
                        PBindings = pBindings
                    };

                    context.Api.CreateDescriptorSetLayout(context.Device, in layoutInfo, null, out var descriptorSetLayout)
                        .ThrowCode("Failed to create descriptor set layout");
                    setLayouts.Add(new DescriptorSetLayoutWrapper(descriptorSetLayout));
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
