using RockEngine.Core.Rendering.ResourceBindings;


namespace RockEngine.Core.Rendering.Materials
{
    public class Material : IDisposable
    {
        private readonly Dictionary<string, MaterialPass> _passes = new();
        private bool _disposed;

        public string Name { get; }
        public IReadOnlyDictionary<string, MaterialPass> Passes => _passes;

        public Material(string name)
        {
            Name = name;
        }

        public void AddPass(string subpassName, MaterialPass pass)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_passes.ContainsKey(subpassName))
            {
                throw new InvalidOperationException($"Subpass {subpassName} already exisits");
            }
            _passes[subpassName] = pass;
        }

        public MaterialPass GetPass(string subpassName)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            return _passes.GetValueOrDefault(subpassName);
        }

        public bool HasPass(string subpassName) => _passes.ContainsKey(subpassName);

        #region Convenient Binding Methods

        // Bind to all passes
        public void BindResource(ResourceBinding binding)
        {
            foreach (var pass in _passes.Values)
            {
                pass.BindResource(binding);
            }
        }

        // Bind to specific subpass
        public void BindResource(string subpassName, ResourceBinding binding)
        {
            if (!_passes.TryGetValue(subpassName, out var pass))
            {
                throw new ArgumentException($"Subpass '{subpassName}' not found in material '{Name}'");
            }
            pass.BindResource(binding);
        }

        // Bind to multiple subpasses
        public void BindResource(IEnumerable<string> subpassNames, ResourceBinding binding)
        {
            foreach (var subpassName in subpassNames)
            {
                if (_passes.TryGetValue(subpassName, out var pass))
                {
                    pass.BindResource(binding);
                }
            }
        }
        /*public void Bind(Texture texture, string name)
        {
            foreach(var pass in _passes.Values)
            {
                foreach (var item in pass.Pipeline.Layout.DescriptorSetLayouts)
                {
                    if(item.Value.)
                }
            }
        }*/


        // Push constant methods
        public void PushConstant<T>(string name, T value)
        {
            foreach (var pass in _passes.Values)
            {
                if (pass.PushConstants.ContainsKey(name))
                {
                    pass.PushConstant(name, value);
                }
            }
        }

        public void PushConstant<T>(string subpassName, string name, T value)
        {
            if (!_passes.TryGetValue(subpassName, out var pass))
            {
                throw new ArgumentException($"Subpass '{subpassName}' not found in material '{Name}'");
            }
            pass.PushConstant(name, value);
        }

        public bool UnbindResource(ResourceBinding binding)
        {
            bool removed = false;
            foreach (var pass in _passes.Values)
            {
                removed |= pass.Bindings.Remove(binding);
            }
            return removed;
        }

        public bool UnbindResource(string subpassName, ResourceBinding binding)
        {
            if (_passes.TryGetValue(subpassName, out var pass))
            {
                return pass.Bindings.Remove(binding);
            }
            return false;
        }

        #endregion

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            foreach (var pass in _passes.Values)
            {
                pass.Dispose();
            }
            _passes.Clear();
            _disposed = true;
        }
    }
}