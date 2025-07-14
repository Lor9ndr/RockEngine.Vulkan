using NLog;

using RockEngine.Core.Internal;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;

using static RockEngine.Vulkan.ShaderReflectionData;

namespace RockEngine.Core.ECS.Components
{
    public class Material : IDisposable
    {
        public VkPipeline Pipeline;
        public Texture[] Textures;
        private readonly int _textureSetLocation = -1;

        internal BindingCollection Bindings { get; private set; } = new BindingCollection();
        public Dictionary<string, ShaderReflectionData.PushConstantInfo> PushConstants { get;private set;}
        private readonly Dictionary<string, byte[]> _pushConstantValues = new();

        public bool IsComplete => Pipeline != null &&
         Pipeline.Layout.DescriptorSetLayouts.All(setLayout =>
             setLayout.Value.Bindings.All(bindingInfo =>
                 Bindings.Any(b => b.Set == setLayout.Key)));

        public Dictionary<string, byte[]> PushConstantValues => _pushConstantValues;

        public Material(VkPipeline pipeline, params List<Texture> textures)
        {
            Pipeline = pipeline;
            var dict = new Dictionary<string, PushConstantInfo>();
            foreach (var pushConstant in Pipeline.Layout.PushConstantRanges)
            {
                if (dict.TryGetValue(pushConstant.Name, out var value))
                {
                    value.StageFlags |= pushConstant.StageFlags;
                }
                else
                {
                    dict[pushConstant.Name] = pushConstant;
                }
            }
               
            PushConstants = dict;

          

            foreach (var set in Pipeline.Layout.DescriptorSetLayouts)
            {
                if (set.Value.Bindings.Any(b => b.DescriptorType == DescriptorType.CombinedImageSampler))
                {
                    _textureSetLocation = (int)set.Key;
                    break;
                }
            }

            if (_textureSetLocation == -1) return;

            var setLayout = Pipeline.Layout.GetSetLayout((uint)_textureSetLocation);
            Textures = textures.ToArray();

            if (setLayout != default)
            {
                while (setLayout.Bindings.Length > textures.Count)
                {
                    textures.Add(Texture.GetEmptyTexture(VulkanContext.GetCurrent()));
                }
                Textures = textures.ToArray();

                Bind(new TextureBinding((uint)_textureSetLocation, 0, default, Textures.Take(setLayout.Bindings.Length).ToArray()));
            }
        }


        public void Bind(ResourceBinding binding)
        {
            Bindings.Add(binding);
        }
        public void Bind(Texture binding, int bindingLocation)
        {
            Bindings.Add(new TextureBinding((uint)_textureSetLocation, (uint)bindingLocation, ImageLayout.ShaderReadOnlyOptimal, binding));
        }

        public bool Unbind(ResourceBinding binding)
        {
            return Bindings.Remove(binding);
        }
        public void PushConstant<T>(string name, T value) where T : unmanaged
        {
            if (!PushConstants.TryGetValue(name, out var constant))
                throw new ArgumentException($"Push constant '{name}' not found.");

            uint size = (uint)Unsafe.SizeOf<T>();
            /*if (size != constant.Size)
                throw new ArgumentException($"Size mismatch for '{name}'. Expected: {constant.Size}, Actual: {size}");*/

            // Get or create byte array
            if (!_pushConstantValues.TryGetValue(name, out byte[]? buffer) || buffer.Length != size)
            {
                buffer = new byte[size];
                _pushConstantValues[name] = buffer;
            }

            // Direct memory copy
            Unsafe.As<byte, T>(ref buffer[0]) = value;
        }

        internal unsafe void CmdPushConstants(VkCommandBuffer cmd)
        {
            foreach (var (name, constant) in PushConstants)
            {
                if (!_pushConstantValues.TryGetValue(name, out var buffer))
                    continue;

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
            _pushConstantValues.Clear();
        }
    }
}
