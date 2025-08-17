using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public class BatchCreationParams
    {
        public string Name { get; set; }
        public CommandBufferType Type { get; set; } = CommandBufferType.Graphics;
        public List<VkSemaphore> WaitSemaphores { get; } = new List<VkSemaphore>();
        public List<PipelineStageFlags> WaitStages { get; } = new List<PipelineStageFlags>();
        public List<VkSemaphore> SignalSemaphores { get; } = new List<VkSemaphore>();
        public uint QueueFamilyIndex { get; set; } = uint.MaxValue;
    }
}