using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public class BatchCreationParams
    {
        public string Name { get; set; }
        public CommandBufferUsageFlags UsageFlags { get;set; }
        public CommandBufferLevel Level { get; set; } = CommandBufferLevel.Primary;
        public CommandBufferInheritanceInfo? InheritanceInfo { get; set; }
        public List<VkSemaphore> WaitSemaphores { get; } = new List<VkSemaphore>();
        public List<PipelineStageFlags> WaitStages { get; } = new List<PipelineStageFlags>();
        public List<VkSemaphore> SignalSemaphores { get; } = new List<VkSemaphore>();
        public VkCommandPool CommandPool { get; set; }
    }
}