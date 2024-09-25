namespace RockEngine.Vulkan.Builders
{
    public struct QueueFamilyIndices
    {
        public uint? PresentFamily { get; set; } = null;
        public uint? GraphicsFamily { get; set; } = null;
        public uint? ComputeFamily { get; set; } = null;
        public uint? TransferFamily { get; set; } = null;
        public QueueFamilyIndices()
        {
        }

        public readonly bool IsComplete()
        {
            return GraphicsFamily.HasValue && ComputeFamily.HasValue && TransferFamily.HasValue && PresentFamily.HasValue;
        }
    }
}