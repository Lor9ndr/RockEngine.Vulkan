﻿using System.Numerics;

namespace RockEngine.Vulkan.ECS
{
    internal class Transform : Component
    {
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; } = Quaternion.Identity;
        public Vector3 Scale { get; set; } = Vector3.One;
    }
}
