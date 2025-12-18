namespace RockEngine.Core.ECS.Components
{


namespace RockEngine.Core.ECS.Components
    {
        public enum ConstraintBreakMode
        {
            None,          // Never break
            Force,         // Break when force exceeds threshold
            Torque,        // Break when torque exceeds threshold
            ForceOrTorque  // Break when either force or torque exceeds threshold
        }
    }
}