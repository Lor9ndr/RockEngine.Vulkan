using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using Silk.NET.Vulkan;

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
                throw new InvalidOperationException($"Subpass {subpassName} already exists");
            }

            _passes[subpassName] = pass;
        }

        public MaterialPass GetPass(string subpassName)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _passes.GetValueOrDefault(subpassName);
        }

        public bool HasPass(string subpassName) => _passes.ContainsKey(subpassName);

        #region High‑level named resource API

        public void SetTexture(string name, Texture texture)
        {
            SetResource(name, texture);
        }

        public void SetUniformBuffer(string name, UniformBuffer buffer)
        {
            SetResource(name, buffer);
        }

        public void SetStorageBuffer<T>(string name, StorageBuffer<T> buffer) where T : unmanaged
        {
            SetResource(name, buffer);
        }

        public void SetPushConstant<T>(string name, T value)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Push constant name cannot be null or empty", nameof(name));
            }


            // Apply immediately to all passes that have this push constant
            foreach (var pass in _passes.Values)
            {
                if (pass.ContainsPushConstant(name))
                {
                    pass.PushConstant(name, value);
                }
            }

        }

        private void SetResource(string name, object resource)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Resource name cannot be null or empty", nameof(name));
            }

            foreach (var pass in Passes)
            {
                if (pass.Value.ExpectedResources.TryGetValue(name, out var bindingInfo))
                {
                    switch (bindingInfo.Reflection.DescriptorType)
                    {
                        case DescriptorType.CombinedImageSampler:
                            pass.Value.BindResource(new TextureBinding(bindingInfo.Set, bindingInfo.Reflection.Binding,
                                                                       0, bindingInfo.Reflection.DescriptorCount,
                                                                       ImageLayout.ShaderReadOnlyOptimal,
                                                                       (Texture)resource));
                            break;
                        case DescriptorType.UniformBuffer or DescriptorType.UniformBufferDynamic:
                            pass.Value.BindResource(new UniformBufferBinding(
                                (UniformBuffer)resource,
                                bindingInfo.Reflection.Binding,
                                bindingInfo.Set));
                            break;
                        case DescriptorType.StorageBuffer:
                            pass.Value.BindResource(new StorageBufferBinding<byte>(
                                (StorageBuffer<byte>)resource,
                                bindingInfo.Reflection.Binding,
                                bindingInfo.Set));
                            break;
                        default:
                            break;
                    }
                }

            }
        }



        #endregion

        #region Low‑level binding API (for dynamic materials or direct control)

        public void BindResource(ResourceBinding binding)
        {
            foreach (var pass in _passes.Values)
            {
                pass.BindResource(binding);
            }
        }

        public void BindResource(string subpassName, ResourceBinding binding)
        {
            if (!_passes.TryGetValue(subpassName, out var pass))
            {
                throw new ArgumentException($"Subpass '{subpassName}' not found");
            }

            pass.BindResource(binding);
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