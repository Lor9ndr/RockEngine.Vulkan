using System.Numerics;
using System.Runtime.CompilerServices;
using NLog;
using RockEngine.Core.Internal;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;
using ZLinq;
using static RockEngine.Vulkan.ShaderReflectionData;

namespace RockEngine.Core.Rendering.Materials
{
    internal class PushConstantBlock
    {
        public required string BlockName { get; set; }
        public ShaderStageFlags StageFlags { get; set; }
        public uint Offset { get; set; }
        public uint Size { get; set; }
        public required byte[] Data { get; set; }
        public Dictionary<string, PushConstantMemberInfo> Members { get; } = new();
    }

    public class MaterialPass : IDisposable
    {
        public RckPipeline Pipeline { get; }
        public BindingCollection Bindings { get; }
        public IReadOnlyDictionary<string, BindingInfo> ExpectedResources { get; }

        private readonly Dictionary<string, PushConstantBlock> _pushConstantBlocks = new();
        private readonly Dictionary<string, string> _memberToBlock = new(); // member name -> block name
        private readonly HashSet<string> _dirtyBlocks = new();

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private bool _disposed = false;

        public MaterialPass(RckPipeline pipeline, IReadOnlyDictionary<string, BindingInfo>? expectedResources = null)
        {
            Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            ExpectedResources = expectedResources ?? new Dictionary<string, BindingInfo>();
            BuildPushConstantBlocks(pipeline.Layout);
            Bindings = new BindingCollection();
        }

        public IReadOnlyList<string> GetTextureSlotNamesFromPushConstants()
        {
            var slots = new List<(string name, uint offset)>();
            foreach (var block in _pushConstantBlocks.Values)
            {
                foreach (var member in block.Members.Values)
                {
                    if (member.Name.EndsWith("Index"))
                    {
                        string slotName = member.Name.Substring(0, member.Name.Length - 5);
                        slots.Add((slotName, member.Offset));
                    }
                }
            }
            return slots.OrderBy(s => s.offset).Select(s => s.name).ToList().AsReadOnly();
        }

        public bool UsesBindlessTextures()
        {
            // Check if any expected combined image sampler binding has descriptorCount == 0
            return ExpectedResources.Values.Any(info =>
                info.Reflection.DescriptorType == DescriptorType.CombinedImageSampler &&
                info.Reflection.DescriptorCount == 0);
        }

        private void SetPushConstantValue(string name, object value, uint size)
        {
            try
            {
                PushConstant(name, value);
            }
            catch
            {
                // If type conversion fails, initialize with zeros
                PushConstant(name, new byte[size]);
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

        private void BuildPushConstantBlocks(CoreObjects.PipelineLayout layout)
        {
            // layout.PushConstantRanges should be extended to include members
            foreach (var blockInfo in layout.VkPipelineLayout.PushConstantRanges)
            {
                var block = new PushConstantBlock
                {
                    BlockName = blockInfo.Name,
                    StageFlags = blockInfo.StageFlags,
                    Offset = blockInfo.Offset,
                    Size = blockInfo.Size,
                    Data = new byte[blockInfo.Size]
                };
                _pushConstantBlocks[blockInfo.Name] = block;

                foreach (var member in blockInfo.Members)
                {
                    block.Members[member.Name] = member;
                    _memberToBlock[member.Name] = blockInfo.Name;
                }
            }
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

            _logger.Warn($"Binding doesn't fit into pipeline layout, pipeline name:{Pipeline.Name}, Set: {binding.SetLocation}, Binding: {binding.BindingLocation}, Type: {binding.DescriptorType}");
            return false;
        }

        private bool IsBindingCompatible(ResourceBinding binding)
        {
            return Pipeline.Layout.VkPipelineLayout.DescriptorSetLayouts.TryGetValue(binding.SetLocation, out var setLayout) &&
                   setLayout.Bindings.AsValueEnumerable().Any(s =>
                   {
                       return binding.BindingLocation.Contains(s.Binding) &&
                                                                     s.DescriptorType == binding.DescriptorType;
                   });
        }

        /// <summary>
        /// Sets a push constant value by block name (entire block) or member name.
        /// O(1) lookup via dictionaries.
        /// </summary>
        public void PushConstant<T>(string name, in T value)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Push constant name cannot be null or empty", nameof(name));
            }

            uint size = (uint)Unsafe.SizeOf<T>();

            // Try as block name
            if (_pushConstantBlocks.TryGetValue(name, out var block))
            {
                if (size != block.Size)
                {
                    //_logger.Warn($"Push constant block '{name}' size mismatch: expected {block.Size}, got {size}");
                }

                var bytes = new Span<byte>(block.Data);
                Unsafe.As<byte, T>(ref bytes[0]) = value;
                _dirtyBlocks.Add(name);
                return;
            }

            // Try as member name
            if (_memberToBlock.TryGetValue(name, out string blockName) &&
                _pushConstantBlocks.TryGetValue(blockName, out block))
            {
                if (block.Members.TryGetValue(name, out var member))
                {
                    if (size != member.Size)
                    {
                        // _logger.Warn($"Push constant member '{name}' size mismatch: expected {member.Size}, got {size}");
                    }

                    var offset = (int)member.Offset;
                    var span = new Span<byte>(block.Data, offset, (int)member.Size);
                    Unsafe.As<byte, T>(ref span[0]) = value;
                    _dirtyBlocks.Add(blockName);
                    return;
                }
            }

            throw new ArgumentException($"Push constant '{name}' not found as block or member in material pass '{Pipeline.Name}'.");
        }

        public unsafe void CmdPushConstants(UploadBatch batch)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(batch);

            foreach (var block in _pushConstantBlocks.Values)
            {
                fixed (byte* dataPtr = block.Data)
                {
                    batch.PushConstants(
                        Pipeline.Layout.VkPipelineLayout,
                        block.StageFlags,
                        block.Offset,
                        block.Size,
                        dataPtr
                    );
                }
            }
            _dirtyBlocks.Clear(); // optional: clear if you still want to track changes for other reasons
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Bindings.Clear();
            _disposed = true;
        }

        public bool ContainsPushConstant(string name)
        {
            return (_pushConstantBlocks.ContainsKey(name) || _memberToBlock.ContainsKey(name));
        }
    }
}