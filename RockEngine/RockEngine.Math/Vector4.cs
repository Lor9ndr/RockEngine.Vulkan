namespace RockEngine.Mathematics
{
    public struct Vector4
    {
        public float X, Y, Z, W;

        public Vector4(float x, float y, float z, float w)
        {
            X = x; Y = y; Z = z; W = w;
        }

        public Vector4(Vector3 xyz, float w)
        {
            X = xyz.X; Y = xyz.Y; Z = xyz.Z; W = w;
        }

        public static Vector4 operator +(Vector4 a, Vector4 b)
            => new Vector4(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);

        public static Vector4 operator -(Vector4 a, Vector4 b)
            => new Vector4(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);

        public static Vector4 operator *(Vector4 a, float b)
            => new Vector4(a.X * b, a.Y * b, a.Z * b, a.W * b);

        public static Vector4 operator /(Vector4 a, float b)
            => new Vector4(a.X / b, a.Y / b, a.Z / b, a.W / b);

        public Vector3 XYZ => new Vector3(X, Y, Z);

        public override string ToString() => $"({X}, {Y}, {Z}, {W})";
    }
}