namespace RockEngine.Core.Rendering.Texturing.Atlasing
{
    public class MaxRectsAllocator : IAtlasAllocator
    {
        // Heuristic choice (can be made configurable)
        public enum FitHeuristic
        {
            BestShortSideFit,
            BestLongSideFit,
            BestAreaFit,
            BottomLeft
        }

        private readonly FitHeuristic _heuristic;
        private List<Rect> _freeRects = new();
        private long _allocatedArea;

        public MaxRectsAllocator(FitHeuristic heuristic = FitHeuristic.BestShortSideFit)
        {
            _heuristic = heuristic;
        }

        public long AllocatedArea => _allocatedArea;

        public void Reset(int pageWidth, int pageHeight)
        {
            _freeRects.Clear();
            _freeRects.Add(new Rect(0, 0, pageWidth, pageHeight));
            _allocatedArea = 0;
        }

        public bool Allocate(int width, int height, out Rect rect)
        {
            // Evaluate each free rectangle
            int bestIndex = -1;
            Rect bestRect = default;
            int bestScore1 = int.MaxValue;
            int bestScore2 = int.MaxValue;

            for (int i = 0; i < _freeRects.Count; i++)
            {
                var f = _freeRects[i];
                if (f.Width >= width && f.Height >= height)
                {
                    int leftoverHoriz = f.Width - width;
                    int leftoverVert = f.Height - height;
                    int score1, score2;

                    switch (_heuristic)
                    {
                        case FitHeuristic.BestShortSideFit:
                            score1 = Math.Min(leftoverHoriz, leftoverVert);
                            score2 = Math.Max(leftoverHoriz, leftoverVert);
                            break;
                        case FitHeuristic.BestLongSideFit:
                            score1 = Math.Max(leftoverHoriz, leftoverVert);
                            score2 = Math.Min(leftoverHoriz, leftoverVert);
                            break;
                        case FitHeuristic.BestAreaFit:
                            score1 = leftoverHoriz * leftoverVert;
                            score2 = f.Area;
                            break;
                        case FitHeuristic.BottomLeft:
                            score1 = f.Y + height;
                            score2 = f.X + width;
                            break;
                        default:
                            score1 = Math.Min(leftoverHoriz, leftoverVert);
                            score2 = Math.Max(leftoverHoriz, leftoverVert);
                            break;
                    }

                    bool better = false;
                    if (score1 < bestScore1 || (score1 == bestScore1 && score2 < bestScore2))
                    {
                        better = true;
                    }

                    if (better)
                    {
                        bestIndex = i;
                        bestRect = new Rect(f.X, f.Y, width, height);
                        bestScore1 = score1;
                        bestScore2 = score2;
                    }
                }
            }

            if (bestIndex == -1)
            {
                rect = default;
                return false;
            }

            // Place the rectangle
            rect = bestRect;
            _allocatedArea += rect.Area;
            var used = _freeRects[bestIndex];
            _freeRects.RemoveAt(bestIndex);

            // Split the remaining area into up to 4 new free rectangles
            if (used.Width > width)
            {
                _freeRects.Add(new Rect(used.X + width, used.Y, used.Width - width, used.Height));
            }

            if (used.Height > height)
            {
                _freeRects.Add(new Rect(used.X, used.Y + height, width, used.Height - height));
            }

            if (used.Width > width && used.Height > height)
            {
                _freeRects.Add(new Rect(used.X + width, used.Y + height, used.Width - width, used.Height - height));
            }

            // Merge/cull rectangles that are fully contained within another
            PruneFreeList();
            return true;
        }

        public void Free(Rect rect)
        {
            _freeRects.Add(rect);
            MergeFreeRects();
            _allocatedArea -= rect.Area;
        }

        private void PruneFreeList()
        {
            // Remove any rectangle that is completely inside another
            for (int i = _freeRects.Count - 1; i >= 0; i--)
            {
                for (int j = 0; j < _freeRects.Count; j++)
                {
                    if (i != j && _freeRects[j].Contains(_freeRects[i]))
                    {
                        _freeRects.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private void MergeFreeRects()
        {
            // Similar greedy merge as in Guillotine, but also handle overlapping containment
            bool merged;
            do
            {
                merged = false;
                // First remove any rect contained within another
                for (int i = _freeRects.Count - 1; i >= 0; i--)
                {
                    for (int j = 0; j < _freeRects.Count; j++)
                    {
                        if (i != j && _freeRects[j].Contains(_freeRects[i]))
                        {
                            _freeRects.RemoveAt(i);
                            merged = true;
                            break;
                        }
                    }
                    if (merged)
                    {
                        break;
                    }
                }
                if (!merged)
                {
                    // Adjacent merging (same as Guillotine)
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
            } while (merged);
        }
    }
}
