using RockEngine.Core.Rendering.Objects;

namespace RockEngine.Core.Registries
{
    public class RenderPassRegistry : IRegistry<RckRenderPass, Type>
    {
        private readonly Dictionary<Type, RckRenderPass> _renderPasses = new();

        public RckRenderPass Get(Type key)
        {
            return _renderPasses[key];
        }

        public void Register(Type key, RckRenderPass value)
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

        public IEnumerable<RckRenderPass> GetAll()
        {
            return _renderPasses.Values;
        }
    }
}
