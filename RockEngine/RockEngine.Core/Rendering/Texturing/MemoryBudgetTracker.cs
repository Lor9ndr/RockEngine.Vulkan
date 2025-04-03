using RockEngine.Core.Helpers;

namespace RockEngine.Core.Rendering.Texturing
{
    public class MemoryBudgetTracker : IDisposable
    {
        private readonly ulong _budget;
        private ulong _used;
        private readonly Dictionary<StreamableTexture, ulong> _allocations = new();
        private readonly LinkedList<StreamableTexture> _lruList = new();

        public MemoryBudgetTracker(ulong budgetBytes) => _budget = budgetBytes;

        public bool CanAllocate(ulong required = 0) =>
            _used + required <= _budget;

        public void Track(StreamableTexture texture)
        {
            if (!_allocations.ContainsKey(texture))
            {
                _allocations[texture] = CalculateTextureSize(texture);
                _used += _allocations[texture];
                _lruList.AddLast(texture);
            }
        }
        public void Untrack(StreamableTexture texture)
        {
            if (_allocations.TryGetValue(texture, out var size))
            {
                _used -= size;
                _allocations.Remove(texture);
                _lruList.Remove(texture);
            }
        }
        public void UpdateUsage(ulong size) => _used += size;

        private ulong CalculateTextureSize(StreamableTexture tex)
        {
            ulong size = 0;
            for (uint i = 0; i < tex.TotalMipLevels; i++)
            {
                size += GetMipSize(tex.Image.Width, tex.Image.Height, i,
                    tex.Image.Format.GetBytesPerPixel());
            }
            return size;
        }

        private static ulong GetMipSize(uint width, uint height, uint mip, uint bpp)
        {
            var mipWidth = Math.Max(width >> (int)mip, 1);
            var mipHeight = Math.Max(height >> (int)mip, 1);
            return (ulong)(mipWidth * mipHeight * bpp);
        }

        public void Dispose()
        {
            foreach (var tex in _allocations.Keys)
                tex.Dispose();

            _allocations.Clear();
        }
    }
}