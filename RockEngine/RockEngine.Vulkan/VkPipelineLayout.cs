
using Silk.NET.Vulkan;

using System.Collections.Concurrent;

namespace RockEngine.Vulkan
{
    public record VkPipelineLayout : VkObject<PipelineLayout>
    {
        public readonly PushConstantRange[] PushConstantRanges;
        public readonly VkDescriptorSetLayout[] DescriptorSetLayouts;
        private readonly RenderingContext _context;


        private static readonly ConcurrentBag<VkDescriptorSetLayout> _allDescriptorSetLayouts;
        static VkPipelineLayout()
        {
            _allDescriptorSetLayouts = new ConcurrentBag<VkDescriptorSetLayout>();
        }

        private VkPipelineLayout(RenderingContext context, PipelineLayout layout, PushConstantRange[] pushConstantRanges, VkDescriptorSetLayout[] descriptorSetLayouts)
            : base(layout)
        {
            PushConstantRanges = pushConstantRanges;
            DescriptorSetLayouts = descriptorSetLayouts;
            _context = context;
        }


        public static unsafe VkPipelineLayout Create(RenderingContext context, params VkShaderModule[] shaders)
        {
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
                RenderingContext.Vk.CreatePipelineLayout(context.Device, &layoutInfo, in RenderingContext.CustomAllocator, out var pipelineLayout)
                    .VkAssertResult("Failed to create pipeline layout");
                return new VkPipelineLayout(context, pipelineLayout, pushConstantRanges,
                    descriptorSetLayoutsWrapped);
            }
        }

        private static unsafe VkDescriptorSetLayout[] CreateDescriptorSetLayouts(RenderingContext context, VkShaderModule[] shaders)
        {
           /* var setLayouts = new List<VkDescriptorSetLayout>();

            var descriptorSetLayoutsReflected = shaders.SelectMany(s => s.DescriptorSetLayouts)
                                                       .GroupBy(d => d.Set)
                                                       .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var set in descriptorSetLayoutsReflected)
            {
                foreach (var layout in set.Value)
                {
                    var bindings = layout.Bindings.Select(b => new DescriptorSetLayoutBindingReflected(b.Name, b.Binding, b.DescriptorType, b.DescriptorCount, b.StageFlags, b.PImmutableSamplers)).ToArray();
                    var newLayout = new VkDescriptorSetLayout(default, set.Key, bindings);

                    // Check if the layout already exists in _allDescriptorSetLayouts
                    var existingLayout = _allDescriptorSetLayouts.FirstOrDefault(l => l.Equals(newLayout));
                    if (existingLayout.Equals(default))
                    {
                        // If it doesn't exist, create a new DescriptorSetLayout and add it to _allDescriptorSetLayouts
                        fixed (DescriptorSetLayoutBinding* pBindings = layout.Bindings.Select(b => new DescriptorSetLayoutBinding
                        {
                            Binding = b.Binding,
                            DescriptorType = b.DescriptorType,
                            DescriptorCount = b.DescriptorCount,
                            StageFlags = b.StageFlags,
                            PImmutableSamplers = b.PImmutableSamplers
                        }).ToArray())
                        {
                            var layoutInfo = new DescriptorSetLayoutCreateInfo
                            {
                                SType = StructureType.DescriptorSetLayoutCreateInfo,
                                BindingCount = (uint)bindings.Length,
                                PBindings = pBindings
                            };
                            var descriptorSetLayout = context.DescriptorSetManager.CreateDescriptorSetLayout(in layoutInfo);
                            newLayout.DescriptorSetLayout = descriptorSetLayout;
                            _allDescriptorSetLayouts.Add(newLayout);
                        }
                        setLayouts.Add(newLayout);
                    }
                    else
                    {
                        // If it exists, use the existing layout
                        setLayouts.Add(existingLayout);
                    }
                }
            }*/

            return [];
        }


        public VkDescriptorSetLayout? GetSetLayout(uint location)
        {
            var setLayout = DescriptorSetLayouts.FirstOrDefault(s => s.SetLocation == location);
            return setLayout == default ? null : setLayout;
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
                            RenderingContext.Vk.DestroyDescriptorSetLayout(_context.Device, item.DescriptorSetLayout, in RenderingContext.CustomAllocator);
                        }

                        RenderingContext.Vk.DestroyPipelineLayout(_context.Device, _vkObject, in RenderingContext.CustomAllocator);
                    }
                    _vkObject = default;
                }

                _disposed = true;
            }
        }
    }
}