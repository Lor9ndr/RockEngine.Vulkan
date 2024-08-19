using RockEngine.Vulkan.Rendering.MaterialRendering;
using RockEngine.Vulkan.VkObjects;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.Rendering
{
    public class FrameInfo
    {
        public int FrameIndex { get;set;}
        public float FrameTime { get; set; }

        public CommandBufferWrapper? CommandBuffer { get;set;}

        public EffectTemplate CurrentEffect { get; internal set; }

        public MeshpassType PassType { get; internal set; }
        public Queue<(uint SetIndex, DescriptorSet DescriptorSet)> DescriptorSetQueue { get; } = new Queue<(uint, DescriptorSet)>();
        public List<UniformBufferObject> PendingUBOs { get;} = new List<UniformBufferObject>();



    }
}
