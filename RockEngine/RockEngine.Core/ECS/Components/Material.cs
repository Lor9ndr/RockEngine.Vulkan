using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Collections;

namespace RockEngine.Core.ECS.Components
{
    public class Material
    {
        public const int TEXTURE_SET_LOCATION = 2;

        public VkPipeline Pipeline;
        public Texture[] Textures;

        public BindingCollection Bindings { get; private set; }

        public DescriptorSet TexturesDescriptorSet;
        public Material(VkPipeline pipeline, params List<Texture> textures)
        {
            Pipeline = pipeline;

            Bindings = new BindingCollection();

            var setLayout = Pipeline.Layout.GetSetLayout(TEXTURE_SET_LOCATION);
            Textures = textures.ToArray();

            if (setLayout != default)
            {
                while (setLayout.Bindings.Length > textures.Count)
                {
                    textures.Add(Texture.GetEmptyTexture(VulkanContext.GetCurrent()));
                }
                Textures = textures.ToArray();

                Bindings.Add(new TextureBinding(TEXTURE_SET_LOCATION, 0, default, Textures.Take(setLayout.Bindings.Length).ToArray()));
            }
        }
    }


    public class BindingCollection : IEnumerable<(uint Set, PerSetBindings)>
    {
        private readonly SortedDictionary<uint, PerSetBindings> _setBindings
            = new SortedDictionary<uint, PerSetBindings>();
        private readonly List<uint> _dynamicOffsets = new List<uint>();

        public uint MinSetLocation => _setBindings.Keys.FirstOrDefault();
        public uint MaxSetLocation => _setBindings.Keys.LastOrDefault();
        public int CountAllBindings => _setBindings.Sum(kvp => kvp.Value.Count);
        public int Count => _setBindings.Count;
        public IReadOnlyList<uint> DynamicOffsets => _dynamicOffsets;

        public void Add(ResourceBinding binding)
        {
            if (!_setBindings.TryGetValue(binding.SetLocation, out var setBindings))
            {
                setBindings = new PerSetBindings(binding.SetLocation);
                _setBindings[binding.SetLocation] = setBindings;
            }

            setBindings.Add(binding);

            if (binding is UniformBufferBinding ubo && ubo.Buffer.IsDynamic)
            {
                _dynamicOffsets.Add((uint)ubo.Offset);
            }
        }

        public bool Remove(ResourceBinding binding)
        {
            if (binding is null)
            {
                return false;
            }
            if (!_setBindings.TryGetValue(binding.SetLocation, out var setBindings))
                return false;

            var removed = setBindings.Remove(binding);

            if (removed && binding is UniformBufferBinding ubo && ubo.Buffer.IsDynamic)
            {
                _dynamicOffsets.Remove((uint)ubo.Offset);
            }

            if (setBindings.Count == 0)
            {
                _setBindings.Remove(binding.SetLocation);
            }

            return removed;
        }

        public IEnumerator<(uint Set, PerSetBindings)> GetEnumerator()
        {
            foreach (var kvp in _setBindings)
            {
                yield return (kvp.Key, kvp.Value);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool TryGetBindings(uint set, out PerSetBindings bindings)
        {
            return _setBindings.TryGetValue(set, out bindings);
        }

        internal void RemoveAll(Func<ResourceBinding, bool> value)
        {
            foreach (var set in _setBindings)
            {
                set.Value.RemoveAll(value);
            }
        }
    }

    public class PerSetBindings : IEnumerable<ResourceBinding>
    {
        private readonly SortedList<uint, ResourceBinding> _bindings = new SortedList<uint, ResourceBinding>();

        public uint Set { get; }
        public int Count => _bindings.Count;

        public PerSetBindings(uint set)
        {
            Set = set;
        }

        public void Add(ResourceBinding binding)
        {
            if (binding.SetLocation != Set)
                throw new ArgumentException($"Binding set {binding.SetLocation} doesn't match collection set {Set}");

            _bindings[binding.BindingLocation] = binding;
        }

        public bool Remove(ResourceBinding binding)
        {
            return _bindings.Remove(binding.BindingLocation);
        }
        public unsafe void RemoveAll(Func<ResourceBinding, bool> value)
        {
            var locations = stackalloc uint[Count];
            int i = 0;
            foreach (var item in _bindings)
            {
                if (value.Invoke(item.Value))
                {
                    locations[i] = item.Key;
                    i++;
                }
            }
            while(i > 0)
            {
                _bindings.Remove(locations[i]);
                i--;
            }
        }

        public IEnumerator<ResourceBinding> GetEnumerator() => _bindings.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
