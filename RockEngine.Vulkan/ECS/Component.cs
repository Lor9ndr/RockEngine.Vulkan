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

        public virtual ValueTask UpdateAsync(double time)
        {
            return ValueTask.CompletedTask;
        }
        internal void SetEntity(Entity entity)
        {
            Entity = entity;
        }
    }
}
