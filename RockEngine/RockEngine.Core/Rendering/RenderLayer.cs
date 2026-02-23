using MessagePack;

namespace RockEngine.Core.Rendering
{

    [MessagePackObject]
    public class RenderLayer
    {
        [Key(0)]
        public uint ID { get; }
        [Key(1)]
        public string Name { get; }
        [Key(2)]
        public int Order { get; set; }
        [Key(3)]
        public bool Enabled { get; set; }

        [Key(4)]
        public RenderLayerMask Mask => (RenderLayerMask)ID;

        public RenderLayer(uint id, string name, int order, bool enabled)
        {
            ID = id;
            Name = name;
            Order = order;
            Enabled = enabled;
        }

        public RenderLayer()
        {
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
