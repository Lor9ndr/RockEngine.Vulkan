using RockEngine.Core.CoreObjects;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Vulkan;
using RockEngine.Vulkan.Builders;

using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace RockEngine.Core.Builders
{
    public class ComputePipelineBuilder : DisposableBuilder
    {
        private readonly VulkanContext _context;
        private CoreObjects.PipelineLayout _layout;
        private Shader _shader;
        private readonly string _name;
        private readonly nint _pName;

        public ComputePipelineBuilder(VulkanContext context, string name)
        {
            _context = context;
            _name = name;
            _pName = SilkMarshal.StringToPtr("main");
        }

        public ComputePipelineBuilder WithShaderModule(Shader shader)
        {
            _shader = shader;
            return this;
        }

        public ComputePipelineBuilder WithLayout(CoreObjects.PipelineLayout layout)
        {
            _layout = layout;
            return this;
        }

        public unsafe RckPipeline Build() // Changed return type to RckPipeline
        {
            _layout ??= new CoreObjects.PipelineLayout(_context, _shader);

            var stageInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.ComputeBit,
                Module = _shader.ShaderModule,
                PName = (byte*)_pName
            };

            var createInfo = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = stageInfo,
                Layout = _layout.VkPipelineLayout
            };

            VK.CreateComputePipelines(
                _context.Device,
                default,
                1,
                in createInfo,
                in CustomAllocator<VkPipeline>(),
                out var pipeline
            ).VkAssertResult();

            _context.DebugUtils.SetDebugUtilsObjectName(pipeline, ObjectType.Pipeline, _name);

            var vkPipeline = new VkPipeline(_context, _name, pipeline, _layout.VkPipelineLayout);

            return new RckPipeline(vkPipeline, _name, _layout);
        }

        protected override void Dispose(bool disposing)
        {
            SilkMarshal.Free(_pName);
        }
    }
}