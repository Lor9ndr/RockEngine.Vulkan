using Silk.NET.Core;
using Silk.NET.Vulkan;

using System.Buffers;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanRasterizerBuilder : DisposableBuilder
    {
        private Bool32 _depthClamp = false;
        private Bool32 _rasterDiscard = false;
        private float _width = 1.0f;
        private PolygonMode _mode = Silk.NET.Vulkan.PolygonMode.Fill;
        private CullModeFlags _cull = CullModeFlags.BackBit;
        private FrontFace _frontFace = Silk.NET.Vulkan.FrontFace.Clockwise;
        private Bool32 _depthBias = false;

        public VulkanRasterizerBuilder DepthClamp(Bool32 depthclamp)
        {
            _depthClamp = depthclamp;
            return this;
        }

        public VulkanRasterizerBuilder RasterDiscardEnable(Bool32 rasterDiscard)
        {
            _rasterDiscard = rasterDiscard;
            return this;
        }

        public VulkanRasterizerBuilder LineWidth(float width)
        {
            _width = width;
            return this;
        }

        public VulkanRasterizerBuilder PolygonMode(PolygonMode mode)
        {
            _mode = mode;
            return this;
        }
        public VulkanRasterizerBuilder CullFace(CullModeFlags cull)
        {
            _cull = cull;
            return this;
        }

        public VulkanRasterizerBuilder FrontFace(FrontFace frontFace)
        {
            _frontFace = frontFace;
            return this;
        }

        public VulkanRasterizerBuilder DepthBiasEnabe(Bool32 enable)
        {
            _depthBias = enable;
            return this;
        }

        public MemoryHandle Build()
        {
            return CreateMemoryHandle([ new PipelineRasterizationStateCreateInfo()
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                CullMode = _cull,
                DepthBiasEnable = _depthBias,
                DepthClampEnable = _depthClamp,
                FrontFace = _frontFace,
                PolygonMode = _mode,
                LineWidth = _width,
                RasterizerDiscardEnable = _rasterDiscard,
            }]);
        }

    }
}
