using SkiaSharp;

namespace RockEngine.Core.Rendering.Texturing.Atlasing
{
    public class GridAllocator : IAtlasAllocator
    {
        private int _cellWidth, _cellHeight;
        private int _columns, _rows;
        private bool[] _cells;
        private long _allocatedArea;
        private int _totalCells;


        public long AllocatedArea => _allocatedArea;

        // Use a separate config method if cell size is not the whole page.
        public void Configure(int cellWidth, int cellHeight)
        {
            _cellWidth = cellWidth;
            _cellHeight = cellHeight;
        }

        public void Reset(int pageWidth, int pageHeight)
        {
            _columns = pageWidth / _cellWidth;
            _rows = pageHeight / _cellHeight;
            _totalCells = _columns * _rows;
            _cells = new bool[_totalCells];
            _allocatedArea = 0;
        }

        public bool Allocate(int width, int height, out Rect rect)
        {
            if (width != _cellWidth || height != _cellHeight)
            {
                rect = default;
                return false;
            }

            for (int i = 0; i < _totalCells; i++)
            {
                if (!_cells[i])
                {
                    _cells[i] = true;
                    int row = i / _columns;
                    int col = i % _columns;
                    rect = new Rect(col * _cellWidth, row * _cellHeight, _cellWidth, _cellHeight);
                    _allocatedArea += rect.Area;
                    return true;
                }
            }
            rect = default;
            return false;
        }

        public void Free(Rect rect)
        {
          
            int col = rect.X / _cellWidth;
            int row = rect.Y / _cellHeight;
            if (col >= 0 && col < _columns && row >= 0 && row < _rows)
            {
                int index = row * _columns + col;
                if (_cells[index])
                {
                    _cells[index] = false;
                    _allocatedArea -= rect.Area;
                }
            }
        }
    }
}
