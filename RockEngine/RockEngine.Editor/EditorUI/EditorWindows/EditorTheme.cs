using ImGuiNET;

using System.Numerics;

namespace RockEngine.Editor.EditorUI.EditorWindows
{
    public static class EditorTheme
    {
        public static void ApplyModernDarkTheme()
        {
            var style = ImGui.GetStyle();
            var colors = style.Colors;
            var io = ImGui.GetIO();

            // Increased spacing and rounded corners
            style.WindowPadding = new Vector2(12, 12);
            style.WindowRounding = 8.0f;
            style.FramePadding = new Vector2(8, 4);
            style.FrameRounding = 6.0f;
            style.PopupRounding = 4.0f;
            style.ChildRounding = 8.0f;
            style.ScrollbarRounding = 6.0f;
            style.GrabRounding = 4.0f;
            style.TabRounding = 6.0f;
            style.ItemSpacing = new Vector2(10, 8);
            style.ItemInnerSpacing = new Vector2(6, 4);
            style.IndentSpacing = 20.0f;
            style.ScrollbarSize = 12.0f;

            // Modern dark color palette
            var bgColor = new Vector4(0.08f, 0.08f, 0.08f, 1.00f);
            var darkColor = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
            var accentColor = new Vector4(0.16f, 0.44f, 0.75f, 1.00f);
            var accentHoverColor = new Vector4(0.20f, 0.54f, 0.85f, 1.00f);
            var textColor = new Vector4(0.92f, 0.92f, 0.92f, 1.00f);

            colors[(int)ImGuiCol.Text] = textColor;
            colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.50f, 0.50f, 0.50f, 1.00f);
            colors[(int)ImGuiCol.WindowBg] = bgColor;
            colors[(int)ImGuiCol.ChildBg] = darkColor;
            colors[(int)ImGuiCol.PopupBg] = darkColor;
            colors[(int)ImGuiCol.Border] = new Vector4(0.18f, 0.18f, 0.18f, 0.50f);
            colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);

            // Interactive elements
            colors[(int)ImGuiCol.FrameBg] = darkColor;
            colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.20f, 0.20f, 0.20f, 1.00f);
            colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.22f, 0.22f, 0.22f, 1.00f);

            // Buttons
            colors[(int)ImGuiCol.Button] = new Vector4(0.20f, 0.20f, 0.20f, 1.00f);
            colors[(int)ImGuiCol.ButtonHovered] = accentColor;
            colors[(int)ImGuiCol.ButtonActive] = accentHoverColor;

            // Headers
            colors[(int)ImGuiCol.Header] = accentColor;
            colors[(int)ImGuiCol.HeaderHovered] = accentHoverColor;
            colors[(int)ImGuiCol.HeaderActive] = accentHoverColor;

            // Titles
            colors[(int)ImGuiCol.TitleBg] = darkColor;
            colors[(int)ImGuiCol.TitleBgActive] = darkColor;
            colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.00f, 0.00f, 0.00f, 0.51f);

            // Scrollbars
            colors[(int)ImGuiCol.ScrollbarBg] = darkColor;
            colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.35f, 0.35f, 0.35f, 1.00f);
            colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.40f, 0.40f, 0.40f, 1.00f);
            colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.45f, 0.45f, 0.45f, 1.00f);

            // Sliders
            colors[(int)ImGuiCol.SliderGrab] = accentColor;
            colors[(int)ImGuiCol.SliderGrabActive] = accentHoverColor;

            // Check marks
            colors[(int)ImGuiCol.CheckMark] = accentColor;

            // Tabs
            colors[(int)ImGuiCol.Tab] = darkColor;
            colors[(int)ImGuiCol.TabHovered] = accentColor;
            colors[(int)ImGuiCol.TabActive] = accentHoverColor;
            colors[(int)ImGuiCol.TabUnfocused] = darkColor;
            colors[(int)ImGuiCol.TabUnfocusedActive] = darkColor;

            // Docking
            colors[(int)ImGuiCol.DockingPreview] = accentColor * new Vector4(1.0f, 1.0f, 1.0f, 0.7f);
            colors[(int)ImGuiCol.DockingEmptyBg] = new Vector4(0.10f, 0.10f, 0.10f, 1.00f);

            // Separators
            colors[(int)ImGuiCol.Separator] = new Vector4(0.20f, 0.20f, 0.20f, 1.00f);
            colors[(int)ImGuiCol.SeparatorHovered] = accentColor;
            colors[(int)ImGuiCol.SeparatorActive] = accentHoverColor;

            // Resize grips
            colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.30f, 0.30f, 0.30f, 0.20f);
            colors[(int)ImGuiCol.ResizeGripHovered] = accentColor;
            colors[(int)ImGuiCol.ResizeGripActive] = accentHoverColor;

            // Plot lines
            colors[(int)ImGuiCol.PlotLines] = accentColor;
            colors[(int)ImGuiCol.PlotLinesHovered] = accentHoverColor;
            colors[(int)ImGuiCol.PlotHistogram] = accentColor;
            colors[(int)ImGuiCol.PlotHistogramHovered] = accentHoverColor;

            // Text selection
            colors[(int)ImGuiCol.TextSelectedBg] = accentColor * new Vector4(0.24f, 0.45f, 0.68f, 0.35f);

            // Font scaling for icons
            if (io.Fonts.Fonts.Size > 1)
            {
                float baseSize = io.Fonts.Fonts[0].FontSize;
                float iconSize = io.Fonts.Fonts[1].FontSize;
                float scaleFactor = baseSize / iconSize;

                io.Fonts.Fonts[1].Scale = scaleFactor;
                io.Fonts.Build();
            }
        }
    }
}
