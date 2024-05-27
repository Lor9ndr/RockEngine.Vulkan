using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.ECS
{
    public class Entity
    {
        public string Name;
        public Transform Transform;
        private readonly List<Component> _components = new List<Component>();
        private bool _isInitialized = false;

        public Entity()
        {
            Name = "Entity";
            Transform = AddComponent(null, new Transform()).GetAwaiter().GetResult();
        }

        public async Task<T> AddComponent<T>(VulkanContext context,T component) where T : Component
        {
            _components.Add(component);

            if (_isInitialized)
            {
                await component.OnInitializedAsync(context).ConfigureAwait(false);
            }
            return component;
        }

        public T GetComponent<T>() where T : Component
        {
            return (T)_components.OfType<T>().First();
        }

        public async Task InitalizeAsync(VulkanContext context)
        {
            if (_isInitialized)
            {
                return;
            }
            var tsks = new Task[_components.Count];
            for (int i = 0; i < _components.Count; i++)
            {
                Component item = _components[i];
                tsks[i] = item.OnInitializedAsync(context);
            }
            await Task.WhenAll(tsks).
                ConfigureAwait(false);
            _isInitialized = true;
        }

        public async Task Update(double time, VulkanContext context, CommandBufferWrapper commandBuffer)
        {
            for (int i = 0; i < _components.Count; i++)
            {
                Component? item = _components[i];
                await item.UpdateAsync(time, context, commandBuffer);
            }
        }

        public void Render(VulkanContext context, CommandBufferWrapper commandBuffer)
        {
            foreach (var item in _components.OfType<IRenderableComponent>())
            {
                item.Render(context, commandBuffer);
            }
        }

        public void Dispose()
        {
            foreach (var item in _components.OfType<IDisposable>())
            {
                item.Dispose();
            }
        }
    }
}
