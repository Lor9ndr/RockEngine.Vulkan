
using Silk.NET.Vulkan;

using System.Collections.ObjectModel;

namespace RockEngine.Vulkan
{
    public record VkPipelineLayout : VkObject<PipelineLayout>
    {
        public readonly PushConstantRange[] PushConstantRanges;
        public readonly ReadOnlyDictionary<uint,VkDescriptorSetLayout> DescriptorSetLayouts;

        private readonly RenderingContext _context;

        private VkPipelineLayout(RenderingContext context, PipelineLayout layout, PushConstantRange[] pushConstantRanges, Dictionary<uint, VkDescriptorSetLayout> descriptorSetLayouts)
            : base(layout)
        {
            PushConstantRanges = pushConstantRanges;
            DescriptorSetLayouts = descriptorSetLayouts.AsReadOnly();
            _context = context;
           
        }


        public static unsafe VkPipelineLayout Create(RenderingContext context, params VkShaderModule[] shaders)
        {
            var descriptorSetLayoutsWrapped = CreateDescriptorSetLayouts(context, shaders);
            var pushConstantRanges = shaders.SelectMany(s => s.ConstantRanges).ToArray();
            var descriptorSetLayouts = descriptorSetLayoutsWrapped.Select(s => s.Value.DescriptorSetLayout).ToArray();

            fixed (DescriptorSetLayout* setLayout = descriptorSetLayouts)
            fixed (PushConstantRange* constantRange = pushConstantRanges)
            {
                var layoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = (uint)descriptorSetLayoutsWrapped.Count,
                    PSetLayouts = setLayout,
                    PushConstantRangeCount = (uint)pushConstantRanges.Length,
                    PPushConstantRanges = constantRange
                };
                RenderingContext.Vk.CreatePipelineLayout(context.Device, &layoutInfo, in RenderingContext.CustomAllocator<VkPipelineLayout>(), out var pipelineLayout)
                    .VkAssertResult("Failed to create pipeline layout");
                return new VkPipelineLayout(context, pipelineLayout, pushConstantRanges,
                    descriptorSetLayoutsWrapped);
            }
        }

        private static unsafe Dictionary<uint, VkDescriptorSetLayout> CreateDescriptorSetLayouts(RenderingContext context, VkShaderModule[] shaders)
        {
            var setLayouts = new List<VkDescriptorSetLayout>();

            foreach (var shader in shaders)
            {
                foreach (var layout in shader.DescriptorSetLayouts)
                {
                    fixed (DescriptorSetLayoutBinding* pBindings = layout.Bindings.Select(s => (DescriptorSetLayoutBinding)s).ToArray())
                    {
                        var layoutInfo = new DescriptorSetLayoutCreateInfo
                        {
                            SType = StructureType.DescriptorSetLayoutCreateInfo,
                            BindingCount = (uint)layout.Bindings.Length,
                            PBindings = pBindings
                        };

                        RenderingContext.Vk.CreateDescriptorSetLayout(context.Device, in layoutInfo,
                                                                      in RenderingContext.CustomAllocator<VkPipelineLayout>(),
                                                                      out var descriptorSet)
                            .VkAssertResult("Failed to create descriptor set layout");

                        var setLayout = new VkDescriptorSetLayout(descriptorSet, layout.Set, layout.Bindings);
                        setLayouts.Add(setLayout);
                    }

                }
            }

            return setLayouts.ToDictionary(s=>s.SetLocation);
        }

        public VkDescriptorSetLayout GetSetLayout(uint location)
        {
            return DescriptorSetLayouts[location];
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
                            RenderingContext.Vk.DestroyDescriptorSetLayout(_context.Device, item.Value.DescriptorSetLayout, in RenderingContext.CustomAllocator<DescriptorSetLayout>());
                        }

                        RenderingContext.Vk.DestroyPipelineLayout(_context.Device, _vkObject, in RenderingContext.CustomAllocator<VkPipelineLayout>());
                    }
                    _vkObject = default;
                }

                _disposed = true;
            }
        }
    }
}