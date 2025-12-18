namespace RockEngine.Core.Rendering
{

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
    public static class RenderLayerMaskExtensions
    {
        extension(RenderLayerMask mask)
        {
            public bool Contains(RenderLayer layer)
            {
                if (layer == null)
                {
                    return false;
                }

                var layerBit = (RenderLayerMask)(1UL << (int)(layer.ID)); ;
                return (mask & layerBit) != 0;
            }
            public RenderLayerMask Add(RenderLayer layer)
            {
                if (layer == null)
                {
                    return mask;
                }

                return mask | (RenderLayerMask)(1UL << (int)layer.ID);
            }

            public RenderLayerMask Remove(RenderLayer layer)
                => mask & ~(RenderLayerMask)(1UL << (int)layer.ID);
        }

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
