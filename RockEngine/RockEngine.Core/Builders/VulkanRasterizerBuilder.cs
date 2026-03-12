using RockEngine.Vulkan.Builders;

using Silk.NET.Core;
using Silk.NET.Vulkan;

using System.Buffers;

namespace RockEngine.Core.Builders
{
    public class VulkanRasterizerBuilder : DisposableBuilder
    {
        private Bool32 _depthClamp = true;
        private Bool32 _rasterDiscard = false;
        private float _width = 1.0f;
        private PolygonMode _mode = Silk.NET.Vulkan.PolygonMode.Fill;
        private CullModeFlags _cull = CullModeFlags.None;
        private FrontFace _frontFace = Silk.NET.Vulkan.FrontFace.Clockwise;
        private Bool32 _depthBias = true;
        private float _depthBiasConstantFactor;
        private float _depthBiasClamp;
        private float _depthBiasSlopeFactor;

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
        public VulkanRasterizerBuilder DepthBiasConstantFactor(float value)
        {
            _depthBiasConstantFactor = value;
            return this;
        }
        public VulkanRasterizerBuilder DepthBiasClamp(float value)
        {
            _depthBiasClamp = value;
            return this;
        }
        public VulkanRasterizerBuilder DepthBiasSlopeFactor(float value)
        {
            _depthBiasSlopeFactor = value;
            return this;
        }

        public MemoryHandle Build()
        {
            return CreateMemoryHandle([new PipelineRasterizationStateCreateInfo()
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                CullMode = _cull,
                DepthBiasEnable = _depthBias,
                DepthClampEnable = _depthClamp,
                FrontFace = _frontFace,
                PolygonMode = _mode,
                LineWidth = _width,
                RasterizerDiscardEnable = _rasterDiscard,
                DepthBiasConstantFactor = _depthBiasConstantFactor,
                DepthBiasClamp = _depthBiasClamp,
                DepthBiasSlopeFactor = _depthBiasSlopeFactor,
            }]);
        }

        
    }
}
