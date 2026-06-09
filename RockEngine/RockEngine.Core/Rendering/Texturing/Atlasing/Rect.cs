namespace RockEngine.Core.Rendering.Texturing.Atlasing
{
    public struct Rect
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public readonly int Area => Width * Height;
        public readonly int Right => X + Width;
        public readonly int Bottom => Y + Height;
        public Rect(int x, int y, int w, int h)
        {
            X = x;
            Y = y;
            Width = w;
            Height = h;
        }
        public bool Contains(Rect other) =>
            X <= other.X && Y <= other.Y && Right >= other.Right && Bottom >= other.Bottom;
    }
}
