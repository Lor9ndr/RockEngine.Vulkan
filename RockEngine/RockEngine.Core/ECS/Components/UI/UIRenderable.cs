using System.Drawing;
using System.Numerics;
using MemoryPack;
using RockEngine.Core.Attributes;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Texturing.Atlasing;

namespace RockEngine.Core.ECS.Components.UI
{
    public abstract class UIRenderable : Component
    {
        public int SortingOrder { get; set; }

        [Color]
        public Vector4 Color { get; set; } = new Vector4(255);
        [MemoryPackIgnore]
        public abstract AtlasRegion? TextureRegion { get; }

        [SerializeIgnore]
        public bool IsDirty { get; set; } = true;

        /// <summary>
        /// Writes up to 4 vertices into the provided span.
        /// </summary>
        public abstract void WriteVertexData(Span<UIVertex> vertices, Vector2 canvasSize, RectTransform rectTransform);
    }
}