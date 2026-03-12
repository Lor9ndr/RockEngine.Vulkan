namespace RockEngine.Core.Rendering
{
    public class RenderLayerSystem
    {
        private readonly Dictionary<string, RenderLayer> _layers = new();
        private readonly Dictionary<uint, string> _layerIdToName = new();
        private uint _nextLayerId = 0;

        public RenderLayer DefaultLayer { get; }
        public RenderLayer UI { get; }
        public RenderLayer Debug { get; }

        public RenderLayerSystem()
        {
            // Create default layers
            DefaultLayer = CreateLayer("Default", 0);
            UI = CreateLayer("UI", 1000);
            Debug = CreateLayer("Debug", 2000);
        }

        public RenderLayer CreateLayer(string name, int order = 0, bool enabled = true)
        {
            if (_layers.ContainsKey(name))
            {
                throw new ArgumentException($"Layer '{name}' already exists");
            }

            var layer = new RenderLayer(_nextLayerId++, name, order, enabled);
            _layers[name] = layer;
            _layerIdToName[layer.ID] = name;
            return layer;
        }

        public RenderLayer GetLayer(string name) => _layers.TryGetValue(name, out var layer) ? layer : null;
        public RenderLayer GetLayer(uint id) => _layerIdToName.TryGetValue(id, out var name) ? _layers[name] : null;

        public IEnumerable<RenderLayer> GetLayers() => _layers.Values.OrderBy(l => l.Order);
        public IEnumerable<RenderLayer> GetEnabledLayers() => _layers.Values.Where(l => l.Enabled).OrderBy(l => l.Order);
    }
}
