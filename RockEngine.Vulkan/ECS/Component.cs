using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.ECS
{
    public abstract class Component
    {
        protected bool IsInitialized;
        public abstract int Order { get; }
        public Entity Entity { get; protected set; }

        public Component(Entity entity)
        {
            SetEntity(entity);
        }

        public abstract Task OnInitializedAsync(VulkanContext context);

        public virtual void Update(double time) { }
        public void SetEntity(Entity entity)
        {
            Entity = entity;
        }
    }
}
