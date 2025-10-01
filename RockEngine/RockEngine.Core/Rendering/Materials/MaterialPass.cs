using NLog;

using RockEngine.Core.Internal;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

using static RockEngine.Vulkan.ShaderReflectionData;

namespace RockEngine.Core.Rendering.Materials
{
    public class MaterialPass : IDisposable
    {
        public RckPipeline Pipeline { get; }
        public BindingCollection Bindings { get; } 
        public IReadOnlyDictionary<string, PushConstantInfo> PushConstants { get; }

        private readonly Dictionary<string, byte[]> _pushConstantValues = new();
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private bool _disposed = false;

        public MaterialPass(RckPipeline pipeline)
        {
            Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            PushConstants = BuildPushConstantDictionary(pipeline.Layout).AsReadOnly();
            InitializeDefaultPushConstantValues();
            Bindings = new BindingCollection();
        }

        private void InitializeDefaultPushConstantValues()
        {
            // Initialize all push constants with their type-appropriate default values
            foreach (var (name, constant) in PushConstants)
            {
                var defaultValue = CreateDefaultValueForSize(constant.Size);
                if (defaultValue != null)
                {
                    SetPushConstantValue(name, defaultValue, constant.Size);
                }
            }
        }

        private void SetPushConstantValue(string name, object value, uint size)
        {
            var method = typeof(MaterialPass).GetMethod("PushConstant", BindingFlags.Public | BindingFlags.Instance);
            if (method == null) return;

            try
            {
                var genericMethod = method.MakeGenericMethod(value.GetType());
                genericMethod.Invoke(this, new[] { name, value });
            }
            catch
            {
                // If type conversion fails, initialize with zeros
                _pushConstantValues[name] = new byte[size];
            }
        }

        private static object? CreateDefaultValueForSize(uint size)
        {
            return size switch
            {
                4 => 0f,        // float
                8 => Vector2.Zero,
                12 => Vector3.Zero,
                16 => Vector4.Zero,
                64 => Matrix4x4.Identity,
                _ => null // Let the push constant system handle initialization
            };
        }

        private static Dictionary<string, PushConstantInfo> BuildPushConstantDictionary(VkPipelineLayout layout)
        {
            var dict = new Dictionary<string, PushConstantInfo>(StringComparer.Ordinal);
            foreach (var pushConstant in layout.PushConstantRanges)
            {
                if (string.IsNullOrEmpty(pushConstant.Name))
                {
                    _logger.Warn("Encountered push constant with null or empty name");
                    continue;
                }

                if (dict.TryGetValue(pushConstant.Name, out var existing))
                {
                    existing.StageFlags |= pushConstant.StageFlags;
                }
                else
                {
                    dict[pushConstant.Name] = pushConstant;
                }
            }
            return dict;
        }

        public bool BindResource(ResourceBinding binding)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (binding == null)
            {
                _logger.Warn("Attempted to bind null resource");
                return false;
            }

            // Validate binding against pipeline layout using reflection data
            if (IsBindingCompatible(binding))
            {
                Bindings.Add(binding);
                return true;
            }

            _logger.Warn($"Binding doesn't fit into pipeline layout. Set: {binding.SetLocation}, Binding: {binding.BindingLocation}, Type: {binding.DescriptorType}");
            return false;
        }

        private bool IsBindingCompatible(ResourceBinding binding)
        {
            return Pipeline.Layout.DescriptorSetLayouts.TryGetValue(binding.SetLocation, out var setLayout) &&
                   setLayout.Bindings.Any(s =>
                   {
                       return binding.BindingLocation.Contains(s.Binding) &&
                                                                     s.DescriptorType == binding.DescriptorType;
                   });
        }

        public void PushConstant<T>(string name, T value) where T : unmanaged
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Push constant name cannot be null or empty", nameof(name));

            if (!PushConstants.TryGetValue(name, out var constant))
            {
                throw new ArgumentException($"Push constant '{name}' not found in material pass '{Pipeline.Name}'.");
            }

            uint size = (uint)Unsafe.SizeOf<T>();
            if (size != constant.Size)
            {
                //throw new ArgumentException($"Size mismatch for push constant '{name}'. Expected: {constant.Size}, Actual: {size}");
            }

            if (!_pushConstantValues.TryGetValue(name, out byte[]? buffer) || buffer.Length != size)
            {
                buffer = new byte[size];
                _pushConstantValues[name] = buffer;
            }

            Unsafe.As<byte, T>(ref buffer[0]) = value;
        }

        public bool TryGetPushConstantType(string name, out uint size)
        {
            size = 0;
            if (PushConstants.TryGetValue(name, out var constant))
            {
                size = constant.Size;
                return true;
            }
            return false;
        }

        public unsafe void CmdPushConstants(VkCommandBuffer cmd)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (cmd == null)
            {
                throw new ArgumentNullException(nameof(cmd));
            }

            foreach (var (name, constant) in PushConstants)
            {
                if (!_pushConstantValues.TryGetValue(name, out var buffer))
                {
                    // Initialize with zeros if not set
                    buffer = new byte[constant.Size];
                    _pushConstantValues[name] = buffer;
                }

                fixed (byte* dataPtr = buffer)
                {
                    cmd.PushConstants(
                        Pipeline.Layout,
                        constant.StageFlags,
                        constant.Offset,
                        constant.Size,
                        dataPtr
                    );
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _pushConstantValues.Clear();
            Bindings.Clear();
            _disposed = true;
        }
    }
}