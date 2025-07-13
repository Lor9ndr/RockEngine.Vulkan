using RockEngine.Vulkan.Builders;

using Silk.NET.Vulkan;

using System.Buffers;

namespace RockEngine.Core.Builders
{
    public class VulkanPipelineVertexInputStateBuilder : DisposableBuilder
    {
        private readonly List<VertexInputBindingDescription> _vertexBindingDescriptions = new List<VertexInputBindingDescription>();
        private readonly List<VertexInputAttributeDescription> _attributeDescription = new List<VertexInputAttributeDescription>();

        public VulkanPipelineVertexInputStateBuilder Add(
            VertexInputBindingDescription vertexBindingDescription,
            VertexInputAttributeDescription[] attributeDescription)
        {
            _vertexBindingDescriptions.Add(vertexBindingDescription);
            _attributeDescription.AddRange(attributeDescription);
            return this;
        }

        public unsafe MemoryHandle Build()
        {
            var pBindings = CreateMemoryHandle(_vertexBindingDescriptions.ToArray());
            var pAttributes = CreateMemoryHandle(_attributeDescription.ToArray());
            PipelineVertexInputStateCreateInfo inputState = new PipelineVertexInputStateCreateInfo()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                PVertexAttributeDescriptions = (VertexInputAttributeDescription*)pAttributes.Pointer,
                VertexAttributeDescriptionCount = (uint)_attributeDescription.Count,
                PVertexBindingDescriptions = (VertexInputBindingDescription*)pBindings.Pointer,
                VertexBindingDescriptionCount = (uint)_vertexBindingDescriptions.Count
            };
            return CreateMemoryHandle([inputState]);
        }

    }
}
