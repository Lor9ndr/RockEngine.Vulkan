using RockEngine.Vulkan.VkObjects;
using RockEngine.Vulkan.VulkanInitilizers;

namespace RockEngine.Vulkan.ECS
{
    public class Entity
    {
        public string Name;
        public Transform Transform;
        private List<Component> _components = new List<Component>();
        private bool _isInitialized = false;

        public Entity()
        {
            Name = "Entity";
            Transform = AddComponent(null, new Transform(this)).GetAwaiter().GetResult();
        }

        public async Task InitializeAsync(VulkanContext context)
        {
            foreach (var item in _components)
            {
                await item.OnInitializedAsync(context);
            }
            _isInitialized = true;
        }

        public async Task<T> AddComponent<T>(VulkanContext context, T component) where T : Component
        {
            _components.Add(component);

            if (_isInitialized)
            {
                await component.OnInitializedAsync(context).ConfigureAwait(false);
            }
            _components = _components.OrderBy(s=>s.Order).ToList();
            return component;
        }

        public T GetComponent<T>() where T : Component
        {
            return _components.OfType<T>().First();
        }


        public async Task RenderAsync(VulkanContext context, CommandBufferWrapper commandBuffer)
        {
            if (!_isInitialized)
            {
                return;
            }

            foreach (var item in _components.OfType<IRenderable>())
            {
                await item.RenderAsync(context, commandBuffer);

            }
        }

        public void Dispose()
        {
            foreach (var item in _components.OfType<IDisposable>())
            {
                item.Dispose();
            }
            _isInitialized = false;
        }

        internal void Update(double time)
        {
            foreach (var item in _components)
            {
                item.Update(time);
            }
        }
    }
}
