namespace RockEngine.Vulkan
{
    public struct StorageBufferInfo
    {
        public string Name { get; internal set; }
        public uint Binding { get; internal set; }
        public uint Set { get; internal set; }
        public uint Size { get; internal set; }
    }
}