using System.Numerics;

namespace RockEngine.Core.Extensions
{
    public static class NumericsExtensions
    {
        public static Vector3 QuaternionToEuler(this Quaternion q)
        {
            q = Quaternion.Normalize(q);

            // Extract sin(pitch)
            float sinPitch = 2.0f * (q.W * q.Y - q.Z * q.X);

            // Handle gimbal lock
            if (MathF.Abs(sinPitch) >= 1.0f)
            {
                return new Vector3(
                    MathF.CopySign(MathF.PI / 2.0f, sinPitch) * (180.0f / MathF.PI),
                    0.0f,
                    MathF.Atan2(-2.0f * (q.X * q.Z - q.W * q.Y), 1.0f - 2.0f * (q.Y * q.Y + q.Z * q.Z)) * (180.0f / MathF.PI)
                );
            }
            else
            {
                return new Vector3(
                    MathF.Asin(sinPitch) * (180.0f / MathF.PI),
                    MathF.Atan2(2.0f * (q.X * q.W + q.Y * q.Z), 1.0f - 2.0f * (q.X * q.X + q.Y * q.Y)) * (180.0f / MathF.PI),
                    MathF.Atan2(2.0f * (q.X * q.Y + q.Z * q.W), 1.0f - 2.0f * (q.Y * q.Y + q.Z * q.Z)) * (180.0f / MathF.PI)
                );
            }
        }

        public static Quaternion EulerToQuaternion(this Vector3 euler)
        {
            Vector3 radians = euler * (MathF.PI / 180.0f);

            float cy = MathF.Cos(radians.Z * 0.5f);
            float sy = MathF.Sin(radians.Z * 0.5f);
            float cp = MathF.Cos(radians.X * 0.5f);
            float sp = MathF.Sin(radians.X * 0.5f);
            float cr = MathF.Cos(radians.Y * 0.5f);
            float sr = MathF.Sin(radians.Y * 0.5f);

            return new Quaternion(
                sr * cp * cy - cr * sp * sy,
                cr * sp * cy + sr * cp * sy,
                cr * cp * sy - sr * sp * cy,
                cr * cp * cy + sr * sp * sy
            );
        }
    }
}
