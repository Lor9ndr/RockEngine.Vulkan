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

    public class RenderLayer
    {
        public uint ID { get; }
        public string Name { get; }
        public int Order { get; set; }
        public bool Enabled { get; set; }
        public RenderLayerMask Mask => (RenderLayerMask)ID;

        public RenderLayer(uint id, string name, int order, bool enabled)
        {
            ID = id;
            Name = name;
            Order = order;
            Enabled = enabled;
        }

        public override string ToString() => $"{Name} (ID: {ID}, Order: {Order})";
    }

    [Flags]
    public enum RenderLayerMask : ulong
    {
        None = 0,
        Default = 1 << 0,
        UI = 1 << 1,
        Debug = 1 << 2,
        User0 = 1UL << 3,
        User1 = 1UL << 4,
        User2 = 1UL << 5,
        User3 = 1UL << 6,
        User4 = 1UL << 7,
        User5 = 1UL << 8,
        User6 = 1UL << 9,
        User7 = 1UL << 10,
        User8 = 1UL << 11,
        User9 = 1UL << 12,
        User10 = 1UL << 13,
        User11 = 1UL << 14,
        User12 = 1UL << 15,
        User13 = 1UL << 16,
        User14 = 1UL << 17,
        User15 = 1UL << 18,
        User16 = 1UL << 19,
        User17 = 1UL << 20,
        User18 = 1UL << 21,
        User19 = 1UL << 22,
        User20 = 1UL << 23,
        User21 = 1UL << 24,
        User22 = 1UL << 25,
        User23 = 1UL << 26,
        User24 = 1UL << 27,
        User25 = 1UL << 28,
        User26 = 1UL << 29,
        User27 = 1UL << 30,
        User28 = 1UL << 31,
        User29 = 1UL << 32,
        User30 = 1UL << 33,
        User31 = 1UL << 34,

        // Define All as combination of all known layers, not ulong.MaxValue
        All = Default | UI | Debug | User0 | User1 | User2 | User3 | User4 | User5 | User6 | User7 | User8 | User9 |
               User10 | User11 | User12 | User13 | User14 | User15 | User16 | User17 | User18 | User19 | User20 |
               User21 | User22 | User23 | User24 | User25 | User26 | User27 | User28 | User29 | User30 | User31
    }
    public static class RenderLayerMaskExtensions
    {
        public static bool Contains(this RenderLayerMask mask, RenderLayer layer)
        {
            if (layer == null)
            {
                return false;
            }

            var layerBit = (RenderLayerMask)(1UL << (int)(layer.ID)); ;
            return (mask & layerBit) != 0;
        }
        public static RenderLayerMask Add(this RenderLayerMask mask, RenderLayer layer)
        {
            if (layer == null)
            {
                return mask;
            }

            return mask | (RenderLayerMask)(1UL << (int)layer.ID);
        }

        public static RenderLayerMask Remove(this RenderLayerMask mask, RenderLayer layer)
            => mask & ~(RenderLayerMask)(1UL << (int)layer.ID);

        public static RenderLayerMask FromLayers(params RenderLayer[] layers)
        {
            var mask = RenderLayerMask.None;
            foreach (var layer in layers)
            {
                mask = mask.Add(layer);
            }

            return mask;
        }
    }
}
