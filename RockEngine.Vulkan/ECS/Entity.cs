using RockEngine.Vulkan.DI;
using RockEngine.Vulkan.Rendering;

using System.Text.Json.Serialization;

namespace RockEngine.Vulkan.ECS
{
    // TODO: make it more perfomanced
    // Think about awaiting tasks as it can be not so cool in terms of game may be reference from one object to another and so on
    public class Entity
    {
        public string Name;
        public TransformComponent Transform;
        private ComponentCollection _components = new ComponentCollection();
        private bool _isInitialized = false;

        [JsonConstructor]
        public Entity()
        {
            Name = "Entity";
            Transform = AddComponent<TransformComponent>();
        }

        public async Task InitializeAsync()
        {
            var tasks = new List<Task>();
            foreach (var item in _components)
            {
                tasks.Add(item.OnInitializedAsync());
            }
            await Task.WhenAll(tasks);
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

        public async Task RenderAsync(FrameInfo frameInfo)
        {
            if (!_isInitialized)
            {
                return;
            }

            foreach (var item in _components.GetRenderables())
            {
                await item.RenderAsync(frameInfo);
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

        internal bool TryGet<T>(out T component) where T: Component
        {
            component = _components.GetFirst<T>();
            return component is not null;
        }
    }
}
