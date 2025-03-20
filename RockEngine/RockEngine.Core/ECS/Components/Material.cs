using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Collections;
using System.Linq;

namespace RockEngine.Core.ECS.Components
{
    public class Material
    {
        public const int TEXTURE_SET_LOCATION = 2;

        public VkPipeline Pipeline;
        public Texture[] Textures;
       
        public Pipeline GeometryPipeline { get; set; }
        public BindingCollection Bindings { get; private set; }

        public DescriptorSet TexturesDescriptorSet;
        public Material(VkPipeline pipeline, params List<Texture> textures)
        {
            Pipeline = pipeline;

            Bindings = new BindingCollection();

            var setLayout = Pipeline.Layout.GetSetLayout(TEXTURE_SET_LOCATION);
            while (setLayout.Bindings.Length >= textures.Count)
            {
                textures.Add(Texture.GetEmptyTexture(RenderingContext.GetCurrent()));
            }
            Textures = textures.ToArray();
            // Add texture bindings to the bindings list
            Bindings.Add(new TextureBinding(TEXTURE_SET_LOCATION, 0, Textures.Take(setLayout.Bindings.Length).ToArray()));
        }
    }


    public class BindingCollection : IEnumerable<ResourceBinding>
    {
        private readonly SortedList<(uint SetLocation, uint BindingLocation), ResourceBinding> _bindings;

        public uint MinSetLocation { get; private set; } = uint.MaxValue;
        public uint MaxSetLocation { get; private set; } = uint.MinValue;
        public uint Count { get; private set; }

        public BindingCollection()
        {
            _bindings = new SortedList<(uint SetLocation, uint BindingLocation), ResourceBinding>();
        }

        public void Add(ResourceBinding binding)
        {
            _bindings.Add((binding.SetLocation, binding.BindingLocation), binding);
            MinSetLocation = Math.Min(MinSetLocation, binding.SetLocation);
            MaxSetLocation = Math.Max(MaxSetLocation, binding.SetLocation);
            Count++;
        }

        public void Remove(ResourceBinding binding)
        {
            _bindings.Remove((binding.SetLocation, binding.BindingLocation));
            // Recalculate min/max if needed
            if (_bindings.Count > 0)
            {
                MinSetLocation = _bindings.Keys.Min(b => b.SetLocation);
                MaxSetLocation = _bindings.Keys.Max(b => b.SetLocation);
            }
            else
            {
                MinSetLocation = uint.MaxValue;
                MaxSetLocation = uint.MinValue;
            }
            Count--;
        }

        public IEnumerator<ResourceBinding> GetEnumerator()
        {
            return _bindings.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

}
