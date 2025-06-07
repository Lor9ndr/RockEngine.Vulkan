using NLog;

using RockEngine.Core.Internal;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;

namespace RockEngine.Core.ECS.Components
{
    public class Material
    {
        public VkPipeline Pipeline;
        public Texture[] Textures;
        private readonly int _textureSetLocation = -1;

        internal BindingCollection Bindings { get; private set; } = new BindingCollection();
        public Dictionary<string, ShaderReflectionData.PushConstantInfo> PushConstants { get;private set;}

        public bool IsComplete => Pipeline != null &&
         Pipeline.Layout.DescriptorSetLayouts.All(setLayout =>
             setLayout.Value.Bindings.All(bindingInfo =>
                 Bindings.Any(b => b.Set == setLayout.Key)));


        public Material(VkPipeline pipeline, params List<Texture> textures)
        {
            Pipeline = pipeline;
            PushConstants = Pipeline.Layout.PushConstantRanges.ToDictionary(s => s.Name);

           /* Console.WriteLine( pipeline.Name);
            foreach (var set in pipeline.Layout.DescriptorSetLayouts)
            {
                Console.WriteLine($"Set {set.Key} bindings: {string.Join(",", set.Value.Bindings.Select(b => $"{b.Binding}:{b.DescriptorType}"))}");
            }*/

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
            {
                throw new ArgumentException($"Push constant '{name}' not found.");
            }

            // Validate size
            uint expectedSize = constant.Size;
            uint actualSize = (uint)Unsafe.SizeOf<T>();
           /* if (actualSize != expectedSize)
            {
                throw new ArgumentException(
                    $"Size mismatch for push constant '{name}'. Expected: {expectedSize}, Actual: {actualSize}");
            }*/

            // Serialize the struct into a byte array
            constant.Value = new byte[expectedSize];
            unsafe
            {
                fixed (byte* dataPtr = constant.Value)
                {
                    *(T*)dataPtr = value; // Directly write the struct into the byte array
                }
            }

            PushConstants[name] = constant; // Update the stored data
        }

        internal unsafe void CmdPushConstants(VkCommandBuffer cmd)
        {
            foreach (var constant in PushConstants.Values)
            {
                if (constant.Value == null || constant.Value.Length != constant.Size)
                {
                    continue; // Skip unset or invalid constants (or throw)
                }

                fixed (byte* dataPtr = constant.Value)
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
    }
}
