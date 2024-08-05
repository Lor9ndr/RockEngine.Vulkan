using RockEngine.Vulkan.DI;
using RockEngine.Vulkan.VkObjects;

namespace RockEngine.Vulkan.ECS
{
    public class Entity
    {
        public string Name;
        public TransformComponent Transform;
        private ComponentCollection _components = new ComponentCollection();
        private bool _isInitialized = false;

        public Entity()
        {
            Name = "Entity";
            Transform = AddComponent<TransformComponent>();
        }

        public async Task InitializeAsync()
        {
            foreach (var item in _components)
            {
                await item.OnInitializedAsync();
            }
            _isInitialized = true;
        }

        public T AddComponent<T>() where T:Component
        {
            var component = IoC.Container.GetInstance<T>();
            _components.Add(component);
            component.SetEntity(this);
            return component;
        }

        public T? GetComponent<T>() where T : Component
        {
            return _components.GetFirst<T>();
        }


        public async Task RenderAsync(CommandBufferWrapper commandBuffer)
        {
            if (!_isInitialized)
            {
                return;
            }

            foreach (var item in _components.GetRenderables())
            {
                await item.RenderAsync(commandBuffer);
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
