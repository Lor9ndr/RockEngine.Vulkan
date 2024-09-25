using Silk.NET.Vulkan;

using System.Buffers;

namespace RockEngine.Vulkan.Builders
{
    public class VulkanColorBlendStateBuilder : DisposableBuilder
    {
        private LogicOp _op;
        private List<PipelineColorBlendAttachmentState> _attachments = new List<PipelineColorBlendAttachmentState>();

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
    }
}
