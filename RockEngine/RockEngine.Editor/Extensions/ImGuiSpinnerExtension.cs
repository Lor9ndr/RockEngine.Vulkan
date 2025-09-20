using ImGuiNET;

using System.Numerics;
public static class ImGuiSpinnerExtension
{
    public static void Spinner(Vector2 center, float radius, float thickness, uint color)
    {
        var drawList = ImGui.GetWindowDrawList();

        float time = (float)ImGui.GetTime();
        int numSegments = 30;
        float start = (float)Math.Abs(Math.Sin(time * 1.8f) * (numSegments - 5));

        float aMin = (float)Math.PI * 2.0f * start / numSegments;
        float aMax = (float)Math.PI * 2.0f * (numSegments - 3) / numSegments;

        drawList.PathClear();

        for (int i = 0; i < numSegments; i++)
        {
            float a = aMin + ((float)i / numSegments) * (aMax - aMin);
            float r = radius - thickness * 0.5f;
            drawList.PathLineTo(new Vector2(
                center.X + (float)Math.Cos(a + time * 8) * r,
                center.Y + (float)Math.Sin(a + time * 8) * r
            ));
        }

        drawList.PathStroke(color, ImDrawFlags.None, thickness);
    }
}