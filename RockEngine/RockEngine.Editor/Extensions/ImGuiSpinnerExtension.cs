using ImGuiNET;

using System.Numerics;

using ImGuiNET;
using System.Numerics;

using ImGuiNET;
using System.Numerics;

namespace RockEngine.Editor.EditorUI.ImGuiRendering
{
    public static class ImguiExtensions
    {
        // Loading spinner frames using Font Awesome
        private static readonly string[] _spinnerFrames =
        {
            "\uf110", // fa-spinner (rotating)
        };

        private static double _lastSpinnerUpdateTime = 0;
        private static int _currentSpinnerFrame = 0;

        /// <summary>
        /// Creates a button with a Font Awesome icon and optional tooltip
        /// </summary>
        public static bool IconButton(string icon, string tooltip = null, Vector2? size = null)
        {
            bool result = ImGui.Button($"{icon}##{icon}_{tooltip}", size ?? Vector2.Zero);

            if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(tooltip);
            }

            return result;
        }

        /// <summary>
        /// Creates a small icon button (for toolbars)
        /// </summary>
        public static bool SmallIconButton(string icon, string tooltip = null)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 4));
            bool result = IconButton(icon, tooltip, new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight()));
            ImGui.PopStyleVar();
            return result;
        }

        /// <summary>
        /// Draws centered text within available space
        /// </summary>
        public static void CenteredText(string text)
        {
            float windowWidth = ImGui.GetWindowSize().X;
            float textWidth = ImGui.CalcTextSize(text).X;

            ImGui.SetCursorPosX((windowWidth - textWidth) * 0.5f);
            ImGui.Text(text);
        }

        /// <summary>
        /// Draws centered text with wrapping
        /// </summary>
        public static void CenteredTextWrapped(string text, float maxWidth)
        {
            float textWidth = ImGui.CalcTextSize(text).X;

            if (textWidth > maxWidth)
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + maxWidth);
                CenteredText(text);
                ImGui.PopTextWrapPos();
            }
            else
            {
                float offset = (maxWidth - textWidth) * 0.5f;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
                ImGui.Text(text);
            }
        }

        /// <summary>
        /// Draws a progress bar centered in available space
        /// </summary>
        public static void CenteredProgressBar(float fraction, Vector2 size)
        {
            float windowWidth = ImGui.GetWindowSize().X;
            ImGui.SetCursorPosX((windowWidth - size.X) * 0.5f);
            ImGui.ProgressBar(fraction, size);
        }

        /// <summary>
        /// Draws a loading spinner
        /// </summary>
        public static void LoadingSpinner(string label, Vector2 size, float thickness, uint color)
        {
            var drawList = ImGui.GetWindowDrawList();
            var center = ImGui.GetCursorScreenPos() + size * 0.5f;

            Spinner(center, Math.Min(size.X, size.Y) * 0.5f, thickness, color);
            ImGui.Dummy(size);
        }

        /// <summary>
        /// Gets the current loading spinner icon (animated)
        /// </summary>
        public static string GetLoadingSpinnerIcon()
        {
            double currentTime = ImGui.GetTime();
            if (currentTime - _lastSpinnerUpdateTime > 0.1)
            {
                _currentSpinnerFrame = (_currentSpinnerFrame + 1) % _spinnerFrames.Length;
                _lastSpinnerUpdateTime = currentTime;
            }
            return _spinnerFrames[_currentSpinnerFrame];
        }

        /// <summary>
        /// Draws a spinner animation
        /// </summary>
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

        /// <summary>
        /// Creates a help marker with tooltip
        /// </summary>
        public static void HelpMarker(string description)
        {
            ImGui.TextDisabled("\uf059"); // fa-question-circle

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
                ImGui.TextUnformatted(description);
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
        }

        /// <summary>
        /// Draws a text with a background color
        /// </summary>
        public static void TextWithBackground(string text, Vector4 bgColor, Vector4 textColor)
        {
            var cursorPos = ImGui.GetCursorScreenPos();
            var textSize = ImGui.CalcTextSize(text);
            var drawList = ImGui.GetWindowDrawList();

            drawList.AddRectFilled(cursorPos, cursorPos + textSize, ImGui.ColorConvertFloat4ToU32(bgColor));
            ImGui.TextColored(textColor, text);
        }

        /// <summary>
        /// Creates a toggle button that works like a checkbox but looks like a button
        /// </summary>
        public static bool ToggleButton(string label, bool isActive, Vector2? size = null)
        {
            if (isActive)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
            }

            bool result = ImGui.Button(label, size ?? Vector2.Zero);

            if (isActive)
            {
                ImGui.PopStyleColor();
            }

            return result;
        }
    }
}
