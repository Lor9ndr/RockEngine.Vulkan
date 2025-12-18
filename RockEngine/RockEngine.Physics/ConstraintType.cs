namespace RockEngine.Core.ECS.Components
{


namespace RockEngine.Core.ECS.Components
    {
        public enum ConstraintType
        {
            Distance,      // Keep two points at fixed distance
            Hinge,         // Allow rotation around one axis
            BallSocket,    // Keep two points together but allow free rotation
            Spring,        // Spring force between two points
            Fixed,         // Completely fixed (no relative motion)
            Prismatic,     // Allow translation along one axis
            ConeTwist,     // Cone and twist constraint (like human joints)
            Gear,          // Gear ratio constraint
            RackAndPinion, // Rack and pinion constraint
            Generic6DOF    // Generic 6 degrees of freedom constraint
        }
    }
}