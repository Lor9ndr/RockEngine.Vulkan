namespace RockEngine.Mathematics
{
    public struct Vector3
    {
        public float X, Y, Z;

        public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public Vector3(float value) { X = value; Y = value; Z = value; }

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.X * b, a.Y * b, a.Z * b);
        public static Vector3 operator /(Vector3 a, float b) => new Vector3(a.X / b, a.Y / b, a.Z / b);

        public Vector2 XY => new Vector2(X, Y);
        public Vector3 Normalized() => this / Length();
        public float Length() => MathF.Sqrt(X * X + Y * Y + Z * Z);
    }
}