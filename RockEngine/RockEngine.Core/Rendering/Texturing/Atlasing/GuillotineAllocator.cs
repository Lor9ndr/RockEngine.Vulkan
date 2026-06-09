namespace RockEngine.Core.Rendering.Texturing.Atlasing
{
    public class GuillotineAllocator : IAtlasAllocator
    {
        private readonly List<Rect> _freeRects = new();
        private long _allocatedArea;

        public long AllocatedArea => _allocatedArea;

        public void Reset(int pageWidth, int pageHeight)
        {
            _freeRects.Clear();
            _freeRects.Add(new Rect(0, 0, pageWidth, pageHeight));
            _allocatedArea = 0;
        }

        public bool Allocate(int width, int height, out Rect rect)
        {
            // Find best free rect using a simple "Short Side Fit" heuristic
            int bestIndex = -1;
            int bestArea = int.MaxValue;
            for (int i = 0; i < _freeRects.Count; i++)
            {
                var f = _freeRects[i];
                if (f.Width >= width && f.Height >= height)
                {
                    int leftoverHoriz = f.Width - width;
                    int leftoverVert = f.Height - height;
                    int score = Math.Min(leftoverHoriz, leftoverVert); // "Short Side Fit"
                    if (score < bestArea)
                    {
                        bestArea = score;
                        bestIndex = i;
                    }
                }
            }
            if (bestIndex == -1)
            {
                rect = default;
                return false;
            }

            var chosen = _freeRects[bestIndex];
            _freeRects.RemoveAt(bestIndex);

            // Place at top-left corner of chosen rect
            rect = new Rect(chosen.X, chosen.Y, width, height);
            _allocatedArea += rect.Area;

            // Split remaining L-shaped area into up to two rectangles
            int rightRemain = chosen.Width - width;
            int bottomRemain = chosen.Height - height;

            if (rightRemain > 0)
            {
                _freeRects.Add(new Rect(chosen.X + width, chosen.Y, rightRemain, chosen.Height));
            }

            if (bottomRemain > 0)
            {
                _freeRects.Add(new Rect(chosen.X, chosen.Y + height, width, bottomRemain));
            }

            return true;
        }

        public void Free(Rect rect)
        {
            // Add freed rectangle back and merge with existing free rectangles
            _freeRects.Add(rect);
            MergeFreeRects();
            _allocatedArea -= rect.Area;
        }

        private void MergeFreeRects()
        {
            // Simple greedy merge: combine any two that share a side and line up perfectly
            bool merged;
            do
            {
                merged = false;
                for (int i = 0; i < _freeRects.Count; i++)
                {
                    for (int j = i + 1; j < _freeRects.Count; j++)
                    {
                        var a = _freeRects[i];
                        var b = _freeRects[j];
                        if (a.Y == b.Y && a.Height == b.Height && a.Right == b.X)
                        {
                            _freeRects[i] = new Rect(a.X, a.Y, a.Width + b.Width, a.Height);
                            _freeRects.RemoveAt(j);
                            merged = true;
                            break;
                        }
                        if (a.Y == b.Y && a.Height == b.Height && b.Right == a.X)
                        {
                            _freeRects[i] = new Rect(b.X, b.Y, a.Width + b.Width, a.Height);
                            _freeRects.RemoveAt(j);
                            merged = true;
                            break;
                        }
                        if (a.X == b.X && a.Width == b.Width && a.Bottom == b.Y)
                        {
                            _freeRects[i] = new Rect(a.X, a.Y, a.Width, a.Height + b.Height);
                            _freeRects.RemoveAt(j);
                            merged = true;
                            break;
                        }
                        if (a.X == b.X && a.Width == b.Width && b.Bottom == a.Y)
                        {
                            _freeRects[i] = new Rect(a.X, b.Y, a.Width, a.Height + b.Height);
                            _freeRects.RemoveAt(j);
                            merged = true;
                            break;
                        }
                    }
                    if (merged)
                    {
                        break;
                    }
                }
            } 
            while (merged);
        }
    }
}
