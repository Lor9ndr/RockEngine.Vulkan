using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;
using static RockEngine.Vulkan.ShaderReflectionData;

namespace RockEngine.Core.Rendering.Materials
{
    public class MaterialPassTemplate
    {
        public string SubpassName { get; }
        public string PipelineName { get; }
        public MergedShaderReflectionData ReflectionData { get; }

        private readonly List<ResourceBinding> _defaultBindings = new();
        private readonly Dictionary<string, object> _defaultPushConstants = new();
        private readonly ITypeBasedResourceProvider _resourceProvider;

        public IReadOnlyList<ResourceBinding> DefaultBindings => _defaultBindings.AsReadOnly();
        public IReadOnlyDictionary<string, object> DefaultPushConstants => _defaultPushConstants.AsReadOnly();

        // Expose expected resources using reflection data types
        public IReadOnlyDictionary<string, BindingInfo> ExpectedDescriptorBindings { get; private set; }
        public IReadOnlyDictionary<string, PushConstantInfo> ExpectedPushConstants { get; private set; }

        public MaterialPassTemplate(
            string subpassName,
            string pipelineName,
            MergedShaderReflectionData reflectionData,
            VulkanContext vulkanContext,
            ITypeBasedResourceProvider? resourceProvider = null)
        {
            SubpassName = subpassName ?? throw new ArgumentNullException(nameof(subpassName));
            PipelineName = pipelineName ?? throw new ArgumentNullException(nameof(pipelineName));
            ReflectionData = reflectionData ?? throw new ArgumentNullException(nameof(reflectionData));
            _resourceProvider = resourceProvider ?? new TypeBasedResourceProvider(vulkanContext);

            InitializeDefaultResources();
        }

        private void InitializeDefaultResources()
        {
            InitializeExpectedResources();
            InitializeDefaultBindings();
            InitializeDefaultPushConstants();
        }

        private void InitializeExpectedResources()
        {
            var descriptorBindings = new Dictionary<string, BindingInfo>();
            foreach (var setInfo in ReflectionData.DescriptorSets.Values)
            {
                foreach (var binding in setInfo.Bindings.Values)
                {
                    if (string.IsNullOrEmpty(binding.Reflection.Name))
                    {
                        throw new Exception("NAME IS NULL FIX ME");
                    }

                    descriptorBindings[binding.Reflection.Name] = binding;
                }
            }
            ExpectedDescriptorBindings = descriptorBindings.AsReadOnly();

            var pushConstants = new Dictionary<string, PushConstantInfo>();
            foreach (var pushConst in ReflectionData.PushConstants)
            {
                pushConstants[pushConst.Name] = pushConst;
            }
            ExpectedPushConstants = pushConstants.AsReadOnly();
        }

        private void InitializeDefaultBindings()
        {
            var context = GetCurrent(); // Assumes static accessor exists
            foreach (var setInfo in ReflectionData.DescriptorSets.Values)
            {
                foreach (var binding in setInfo.Bindings.Values)
                {
                    var resourceBinding = CreateDefaultBinding(setInfo.Set, binding, context);
                    if (resourceBinding != null)
                    {
                        _defaultBindings.Add(resourceBinding);
                    }
                }
            }
        }

        private ResourceBinding? CreateDefaultBinding(uint set, BindingInfo binding, VulkanContext context)
        {
            return binding.Reflection.DescriptorType switch
            {
                DescriptorType.CombinedImageSampler => CreateTextureBinding(set, binding, context),
                DescriptorType.SampledImage => CreateTextureBinding(set, binding, context),
                DescriptorType.StorageImage => CreateTextureBinding(set, binding, context),
                DescriptorType.UniformBuffer => CreateBufferBinding(set, binding, context),
                DescriptorType.StorageBuffer => CreateBufferBinding(set, binding, context),
                DescriptorType.UniformBufferDynamic => CreateBufferBinding(set, binding, context),
                DescriptorType.StorageBufferDynamic => CreateBufferBinding(set, binding, context),
                _ => null
            };
        }

        private ResourceBinding CreateTextureBinding(uint set, BindingInfo binding, VulkanContext context)
        {
            var texture = _resourceProvider.GetDefaultResource(binding);
            return new TextureBinding(set, binding.Reflection.Binding, 0, Vk.RemainingMipLevels, ImageLayout.ShaderReadOnlyOptimal, textures: (Texturing.Texture)texture);
        }

        private ResourceBinding? CreateBufferBinding(uint set, BindingInfo binding, VulkanContext context)
        {
            // TODO: Implement default buffer creation if needed
            return null;
        }

        private void InitializeDefaultPushConstants()
        {
            foreach (var pushConst in ReflectionData.PushConstants)
            {
                var defaultValue = _resourceProvider.GetDefaultPushConstant(pushConst);
                if (defaultValue != null)
                {
                    _defaultPushConstants[pushConst.Name] = defaultValue;
                }
            }
        }

        public MaterialPass CreateMaterialPass(RckPipeline pipeline)
        {
            ArgumentNullException.ThrowIfNull(pipeline);
            var pass = new MaterialPass(pipeline, ExpectedDescriptorBindings);

            foreach (var binding in _defaultBindings)
            {
                pass.BindResource((ResourceBinding)binding.Clone());
            }

            foreach (var (name, value) in _defaultPushConstants)
            {
                pass.PushConstant(name, value);
            }

            return pass;
        }
    }
}