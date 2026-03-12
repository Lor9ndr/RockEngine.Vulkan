using System;

namespace RockEngine.Mathematics
{

    public static class ShaderMath
    {
        public static float Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X
        );
        public static Vector3 Normalize(Vector3 v) => v.Normalized();
        public static float Length(Vector3 v) => v.Length();
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static float Min(float a, float b) => MathF.Min(a, b);
        public static float Clamp(float value, float min, float max) => MathF.Max(min, MathF.Min(max, value));
        public static float Sin(float x) => MathF.Sin(x);
        public static float Cos(float x) => MathF.Cos(x);
    }
}