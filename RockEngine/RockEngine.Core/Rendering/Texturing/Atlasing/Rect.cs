namespace RockEngine.Core.Rendering.Texturing.Atlasing
{
    public struct Rect
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;
        public readonly float Area => Width * Height;
        public readonly float Right => X + Width;
        public readonly float Bottom => Y + Height;
        public readonly float Left => X;
        public readonly float Top => Y;
        public Rect(float x, float y, float w, float h)
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
