using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public struct ShaderVariableReflected
    {
        public string? Name { get; set; }
        public uint Location { get; set; }
        public ShaderVariableType Type { get; set; }
        public ShaderStageFlags ShaderStage { get; set; }
    }

    public struct UniformBufferObjectReflected
    {
        public required string Name { get; set; }
        public uint Binding { get; set; }
        public uint Size { get; set; }
        public List<UniformBufferMemberReflected>? Members { get; set; }
        public uint Set { get; internal set; }
        public ShaderStageFlags ShaderStage { get; set; }
    }

    public struct UniformBufferMemberReflected
    {
        public required string Name { get; set; }
        public uint Offset { get; set; }
        public uint Size { get; set; }
        public ShaderVariableType Type { get; set; }
        public ShaderStageFlags ShaderStage { get; set; }
    }

    public struct SamplerObjectReflected
    {
        public required string Name { get; set; }
        public uint Binding { get; set; }
        public ShaderStageFlags ShaderStage { get; set; }
        public uint Set { get; internal set; }
    }

    public struct ImageObjectReflected
    {
        public required string Name { get; set; }
        public uint Binding { get; set; }
        public ShaderStageFlags ShaderStage { get; set; }
        public uint Set { get; internal set; }
    }
    public struct PushConstantReflected
    {
        public string Name { get; set; }
        public ShaderStageFlags StageFlags { get; set; }
        public uint Offset { get; set; }
        public uint Size { get; set; }
        // Stores the serialized struct bytes
        public byte[] Value;
        public PushConstantRange ToPushConstantRangeVulkan() => new PushConstantRange() { Offset = Offset, Size = Size, StageFlags = StageFlags };
    }
    public enum ShaderVariableType
    {
        Float,
        Vec2,
        Vec3,
        Vec4,
        Mat4,
        Int,
        Custom
    }
}
