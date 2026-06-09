namespace RockEngine.Core.Rendering.Texturing.Atlasing
{
    public interface IAtlasAllocator
    {
        /// <summary>
        /// Try to allocate a rectangle; returns true and fills rect.
        /// </summary>
        bool Allocate(int width, int height, out Rect rect);

        /// <summary>
        /// Free a previously allocated rectangle (no‑op if not tracked).
        /// </summary>
        void Free(Rect rect);

        /// <summary>
        /// Reset allocator for a new page of given dimensions.
        /// </summary>
        void Reset(int pageWidth, int pageHeight);

        /// <summary>
        /// Total allocated area in pixels (for diagnostics).
        /// </summary>
        long AllocatedArea { get; }
    }
}
