using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace RockEngine.Core
{
    public struct Ray
    {
        public Vector3 Position;
        public Vector3 Direction;

        public Ray(Vector3 position, Vector3 direction)
        {
            Position = position;
            Direction = direction;
        }

        public readonly bool Intersects(Plane plane, out float distance)
        {
            float denom = Vector3.Dot(plane.Normal, Direction);

            if (MathF.Abs(denom) > 1e-6f)
            {
                distance = (-plane.D - Vector3.Dot(plane.Normal, Position)) / denom;
                return distance >= 0;
            }

            distance = 0;
            return false;
        }
    }
    public struct Plane
    {
        public Vector3 Normal;
        public float D;

        public Plane(Vector3 normal, float d)
        {
            Normal = normal;
            D = d;
        }

        public Plane(Vector3 normal, Vector3 point)
        {
            Normal = Vector3.Normalize(normal);
            D = -Vector3.Dot(Normal, point);
        }
    }
}
