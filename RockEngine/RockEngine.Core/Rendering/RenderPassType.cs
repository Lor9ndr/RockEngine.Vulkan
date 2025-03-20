namespace RockEngine.Core.Rendering
{
    public enum RenderPassType : short
    {
        None = 0,
        ImGui = 1,
        Depth = 2,
        ColorDepth = 3,
        Deferred = 4,
    }
}
