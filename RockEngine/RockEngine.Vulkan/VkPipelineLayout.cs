
using Silk.NET.Vulkan;

using System.Collections.ObjectModel;

namespace RockEngine.Vulkan
{
    public class VkPipelineLayout : VkObject<PipelineLayout>
    {
        public readonly PushConstantRange[] PushConstantRanges;
        public readonly ReadOnlyDictionary<uint, VkDescriptorSetLayout> DescriptorSetLayouts;

        private readonly VulkanContext _context;

        private VkPipelineLayout(VulkanContext context, PipelineLayout layout, PushConstantRange[] pushConstantRanges, Dictionary<uint, VkDescriptorSetLayout> descriptorSetLayouts)
            : base(layout)
        {
            PushConstantRanges = pushConstantRanges;
            DescriptorSetLayouts = descriptorSetLayouts.AsReadOnly();
            _context = context;

        }


        public static unsafe VkPipelineLayout Create(VulkanContext context, params VkShaderModule[] shaders)
        {
            // Merge descriptor set layouts across all shaders
            var mergedSetLayouts = CreateDescriptorSetLayouts(context, shaders);

            // Collect push constants from all shaders
            var pushConstantRanges = shaders
                .SelectMany(s => s.ConstantRanges)
                .ToArray();

            // Get native layouts in order
            var descriptorSetLayouts = mergedSetLayouts
                .OrderBy(kv => kv.Key)
                .Select(kv => kv.Value.DescriptorSetLayout)
                .ToArray();

            fixed (DescriptorSetLayout* setLayoutsPtr = descriptorSetLayouts)
            fixed (PushConstantRange* pushConstantsPtr = pushConstantRanges)
            {
                var layoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = (uint)descriptorSetLayouts.Length,
                    PSetLayouts = setLayoutsPtr,
                    PushConstantRangeCount = (uint)pushConstantRanges.Length,
                    PPushConstantRanges = pushConstantsPtr
                };

                VulkanContext.Vk.CreatePipelineLayout(context.Device, &layoutInfo,
                    in VulkanContext.CustomAllocator<VkPipelineLayout>(), out var pipelineLayout)
                    .VkAssertResult("Failed to create pipeline layout");

                return new VkPipelineLayout(context, pipelineLayout, pushConstantRanges, mergedSetLayouts);
            }
        }


        private static Dictionary<uint, VkDescriptorSetLayout> CreateDescriptorSetLayouts(
            VulkanContext context, VkShaderModule[] shaders)
        {
            var mergedSets = new Dictionary<uint, List<DescriptorSetLayoutBindingReflected>>();

            // Merge bindings across all shaders by set number
            foreach (var shader in shaders)
            {
                foreach (var setLayout in shader.DescriptorSetLayouts)
                {
                    if (!mergedSets.TryGetValue(setLayout.Set, out var bindings))
                    {
                        bindings = new List<DescriptorSetLayoutBindingReflected>();
                        mergedSets.Add(setLayout.Set, bindings);
                    }

                    foreach (var binding in setLayout.Bindings)
                    {
                        var existing = bindings.FirstOrDefault(b => b.Binding == binding.Binding);
                        if (existing != null)
                        {
                            // Merge stage flags if binding exists in multiple shaders
                            existing.StageFlags |= binding.StageFlags;
                        }
                        else
                        {
                            bindings.Add(binding);
                        }
                    }
                }
            }
            uint maxSet = mergedSets.Keys.Count != 0 ? mergedSets.Keys.Max() : 0;

            // Create empty layouts for missing sets
            for (uint setNumber = 0; setNumber <= maxSet; setNumber++)
            {
                if (!mergedSets.ContainsKey(setNumber))
                {
                    mergedSets[setNumber] = new List<DescriptorSetLayoutBindingReflected>();
                }
            }
            // Create descriptor set layouts for merged sets
            var result = new Dictionary<uint, VkDescriptorSetLayout>();
            foreach (var (setNumber, bindings) in mergedSets)
            {
                var layout = CreateDescriptorSetLayout(context, setNumber, bindings.ToArray());
                result.Add(setNumber, layout);
            }
#if DEBUG
            Console.WriteLine("Merged descriptor sets for pipeline layout:");
            foreach (var (setNumber, bindings) in mergedSets)
            {
                Console.WriteLine($"  Set {setNumber} has {bindings.Count} bindings");
                foreach (var binding in bindings)
                {
                    Console.WriteLine($"    Binding {binding.Binding}: {binding.DescriptorType} ({binding.StageFlags})");
                }
            }
#endif

            return result;
        }

        private static unsafe VkDescriptorSetLayout CreateDescriptorSetLayout(
            VulkanContext context, uint setNumber, DescriptorSetLayoutBindingReflected[] bindings)
        {
            var vkBindings = bindings
                .Select(b => new DescriptorSetLayoutBinding(
                    binding: b.Binding,
                    descriptorType: b.DescriptorType,
                    descriptorCount: b.DescriptorCount,
                    stageFlags: b.StageFlags,
                    pImmutableSamplers: null))
                .ToArray();

            fixed (DescriptorSetLayoutBinding* bindingsPtr = vkBindings)
            {
                var layoutInfo = new DescriptorSetLayoutCreateInfo
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = (uint)vkBindings.Length,
                    PBindings = bindingsPtr
                };

                VulkanContext.Vk.CreateDescriptorSetLayout(context.Device, in layoutInfo,
                    in VulkanContext.CustomAllocator<VkPipelineLayout>(),
                    out var descriptorSetLayout)
                    .VkAssertResult("Failed to create descriptor set layout");

                return new VkDescriptorSetLayout(descriptorSetLayout, setNumber, bindings);
            }
        }
        public VkDescriptorSetLayout GetSetLayout(uint location)
        {
            if(DescriptorSetLayouts.TryGetValue(location, out VkDescriptorSetLayout value))
            {
                return value;
            }
            else
            {
                return default;
            }

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
                            VulkanContext.Vk.DestroyDescriptorSetLayout(_context.Device, item.Value.DescriptorSetLayout, in VulkanContext.CustomAllocator<DescriptorSetLayout>());
                        }

                        VulkanContext.Vk.DestroyPipelineLayout(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkPipelineLayout>());
                    }
                    _vkObject = default;
                }

                _disposed = true;
            }
        }
    }
}