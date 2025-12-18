using RockEngine.Core.ECS;

namespace RockEngine.Core.Rendering.Managers
{
    public class TriggerEvent
    {
        public Entity Entity { get; set; }
        public Entity OtherEntity { get; set; }
        /// <summary>
        /// true = enter, false = exit
        /// </summary>
        public bool Enter { get; set; } 
    }
}