using RockEngine.Core.Rendering;

namespace RockEngine.Core.Registries
{
    public class RenderPassRegistry : IRegistry<EngineRenderPass, Type>
    {
        private readonly Dictionary<Type, EngineRenderPass> _renderPasses = new();

        public EngineRenderPass Get(Type key)
        {
            return _renderPasses[key];
        }

        public void Register(Type key, EngineRenderPass value)
        {
            _renderPasses[key] = value;
        }

        public void Unregister(Type key)
        {
            _renderPasses.Remove(key);
        }
        public void Dispose()
        {
            foreach (var item in _renderPasses)
            {
                item.Value.Dispose();
            }
        }
    }
}
