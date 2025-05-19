using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.Builders
{
    public class ComputePipelineBuilder : DisposableBuilder
    {
        private readonly VulkanContext _context;
        private VkPipelineLayout _layout;
        private VkShaderModule _shaderModule;
        private readonly string _name;
        private readonly nint _pName;

        public ComputePipelineBuilder(VulkanContext context, string name)
        {
            _context = context;
            _name = name;
            _pName = SilkMarshal.StringToPtr("main");
        }

        public ComputePipelineBuilder WithShaderModule(VkShaderModule shader)
        {
            _shaderModule = shader;
            return this;
        }

        public ComputePipelineBuilder WithLayout(VkPipelineLayout layout)
        {
            _layout = layout;
            return this;
        }

        public unsafe VkPipeline Build()
        {
            _layout ??= VkPipelineLayout.Create(_context, _shaderModule);

            var stageInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.ComputeBit,
                Module = _shaderModule,
                PName = (byte*)_pName
            };

            var createInfo = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = stageInfo,
                Layout = _layout
            };

            VulkanContext.Vk.CreateComputePipelines(
                _context.Device,
                default,
                1,
                in createInfo,
                in VulkanContext.CustomAllocator<VkPipeline>(),
                out var pipeline
            ).VkAssertResult();

            _context.DebugUtils.SetDebugUtilsObjectName(pipeline, ObjectType.Pipeline, _name);

            return new VkPipeline(_context, _name, pipeline, _layout);
        }

        protected override void Dispose(bool disposing)
        {
            SilkMarshal.Free(_pName);
            _shaderModule?.Dispose();
        }
    }
}
