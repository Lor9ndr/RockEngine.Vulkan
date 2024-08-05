using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.ECS
{
    public abstract class Component
    {
        protected bool IsInitialized;
        public Entity Entity { get; protected set; }

        public Component()
        {
        }

        public abstract Task OnInitializedAsync();

        public virtual void Update(double time) { }
        internal void SetEntity(Entity entity)
        {
            Entity = entity;
        }
    }
}
