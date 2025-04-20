using Silk.NET.Vulkan;

using System.Buffers;

namespace RockEngine.Vulkan.Builders
{
    public class VulkanColorBlendStateBuilder : DisposableBuilder
    {
        private LogicOp _op;
        private readonly List<PipelineColorBlendAttachmentState> _attachments = new List<PipelineColorBlendAttachmentState>();

        public VulkanColorBlendStateBuilder Configure(LogicOp op)
        {
            _op = op;
            return this;
        }

        public VulkanColorBlendStateBuilder AddAttachment(PipelineColorBlendAttachmentState attachment)
        {
            _attachments.Add(attachment);
            return this;
        }
        public VulkanColorBlendStateBuilder AddAttachment(params PipelineColorBlendAttachmentState[] attachment)
        {
            _attachments.AddRange(attachment);
            return this;
        }

        public unsafe MemoryHandle Build()
        {
            var p = CreateMemoryHandle(_attachments.ToArray());
            return CreateMemoryHandle(
                [new PipelineColorBlendStateCreateInfo()
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOp = _op,
                PAttachments = (PipelineColorBlendAttachmentState*)p.Pointer,
                AttachmentCount = (uint)_attachments.Count,
            }]);
        }

        public VulkanColorBlendStateBuilder AddDefaultAttachment()
        {
            AddAttachment((new PipelineColorBlendAttachmentState
            {
                BlendEnable = false,  // Default to no blending
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.Zero,
                AlphaBlendOp = BlendOp.Add,
                ColorWriteMask = ColorComponentFlags.RBit |
                                ColorComponentFlags.GBit |
                                ColorComponentFlags.BBit |
                                ColorComponentFlags.ABit
            }));
            return this;
        }
    }
}
