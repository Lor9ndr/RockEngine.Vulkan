using System.Numerics;
using MemoryPack;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Texturing.Atlasing;

namespace RockEngine.Core.ECS.Components.UI
{
    [MemoryPackable]
    public partial class RectTransform : Component
    {
        public Vector2 AnchorMin { get; set; } = Vector2.Zero;
        public Vector2 AnchorMax { get; set; } = Vector2.One;
        public Vector2 Pivot { get; set; } = new Vector2(0.5f, 0.5f);
        public Vector2 AnchoredPosition { get; set; }
        public Vector2 SizeDelta { get; set; } = new Vector2(100, 100);

        // Local rotation and scale (rarely used in UI, but can be added)
        public float Rotation { get; set; } = 0f;
        public Vector2 Scale { get; set; } = Vector2.One;

        /// <summary>
        /// Returns the final screen-space rectangle for this UI element.
        /// </summary>
        /// <param name="canvasSize">Size of the canvas (e.g., camera render target resolution)</param>
        public Rect GetRect(Vector2 canvasSize)
        {
            // Anchor points in absolute pixels
            Vector2 anchorMinAbs = AnchorMin * canvasSize;
            Vector2 anchorMaxAbs = AnchorMax * canvasSize;

            // Offset from anchor to pivot
            Vector2 offsetMin = anchorMinAbs + AnchoredPosition - Pivot * SizeDelta;
            Vector2 offsetMax = anchorMaxAbs + AnchoredPosition + (Vector2.One - Pivot) * SizeDelta;

            Vector2 size = offsetMax - offsetMin;
            return new Rect(offsetMin.X, offsetMin.Y, size.X, size.Y);
        }
    }
}