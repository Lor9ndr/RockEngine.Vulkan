using System.Numerics;

namespace RockEngine.Core.Physics
{
    public struct Ray
    {
        public Vector3 Origin;
        public Vector3 Direction;

        public Ray(Vector3 origin, Vector3 direction)
        {
            Origin = origin;
            Direction = Vector3.Normalize(direction);
        }

        public Vector3 GetPoint(float t) => Origin + Direction * t;

        public static bool RayPlaneIntersection(Ray ray, Vector3 planePoint, Vector3 planeNormal, out float t)
        {
            float denom = Vector3.Dot(planeNormal, ray.Direction);
            if (Math.Abs(denom) < 1e-6f)
            {
                t = 0;
                return false; // Ray parallel to plane
            }
            t = Vector3.Dot(planeNormal, planePoint - ray.Origin) / denom;
            return t >= 0;
        }
    }

}
