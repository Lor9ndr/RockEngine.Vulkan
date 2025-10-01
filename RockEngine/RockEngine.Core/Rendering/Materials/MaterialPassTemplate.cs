using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Reflection;

namespace RockEngine.Core.Rendering.Materials
{
    public class MaterialPassTemplate
    {
        public string SubpassName { get; }
        public string PipelineName { get; }
        public ShaderReflectionData ReflectionData { get; }

        private readonly List<ResourceBinding> _defaultBindings = new();
        private readonly Dictionary<string, object> _defaultPushConstants = new();
        private readonly ITypeBasedResourceProvider _resourceProvider;

        public IReadOnlyList<ResourceBinding> DefaultBindings => _defaultBindings.AsReadOnly();
        public IReadOnlyDictionary<string, object> DefaultPushConstants => _defaultPushConstants.AsReadOnly();

        public MaterialPassTemplate(
            string subpassName,
            string pipelineName,
            ShaderReflectionData reflectionData,
            ITypeBasedResourceProvider resourceProvider = null)
        {
            SubpassName = subpassName ?? throw new ArgumentNullException(nameof(subpassName));
            PipelineName = pipelineName ?? throw new ArgumentNullException(nameof(pipelineName));
            ReflectionData = reflectionData ?? throw new ArgumentNullException(nameof(reflectionData));
            _resourceProvider = resourceProvider ?? new TypeBasedResourceProvider();

            InitializeDefaultResources();
        }

        private void InitializeDefaultResources()
        {
            InitializeDefaultBindings();
            InitializeDefaultPushConstants();
        }

        private void InitializeDefaultBindings()
        {
            var context = VulkanContext.GetCurrent();
            if (context == null)
                throw new InvalidOperationException("VulkanContext is not available");

            foreach (var setInfo in ReflectionData.DescriptorSets)
            {
                foreach (var binding in setInfo.Bindings)
                {
                    var resourceBinding = CreateDefaultBinding(setInfo.Set, binding, context);
                    if (resourceBinding != null)
                    {
                        _defaultBindings.Add(resourceBinding);
                    }
                }
            }
        }

        private ResourceBinding CreateDefaultBinding(uint set, DescriptorSetLayoutBindingReflected binding, VulkanContext context)
        {
            return binding.DescriptorType switch
            {
                DescriptorType.CombinedImageSampler => CreateTextureBinding(set, binding, context),
                DescriptorType.SampledImage => CreateTextureBinding(set, binding, context),
                DescriptorType.StorageImage => CreateTextureBinding(set, binding, context),
                DescriptorType.UniformBuffer => CreateBufferBinding(set, binding, context),
                DescriptorType.StorageBuffer => CreateBufferBinding(set, binding, context),
                DescriptorType.UniformBufferDynamic => CreateBufferBinding(set, binding, context),
                DescriptorType.StorageBufferDynamic => CreateBufferBinding(set, binding, context),
                _ => throw new NotImplementedException()
            };
        }

        private ResourceBinding CreateTextureBinding(uint set, DescriptorSetLayoutBindingReflected binding, VulkanContext context)
        {
            var texture = _resourceProvider.GetDefaultTexture(binding, context);
            return new TextureBinding(set, binding.Binding, 0, binding.DescriptorCount, texture);
        }

        private ResourceBinding CreateBufferBinding(uint set, DescriptorSetLayoutBindingReflected binding, VulkanContext context)
        {
            return null; // Implement buffer creation as needed
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
            if (pipeline == null)
                throw new ArgumentNullException(nameof(pipeline));

            var pass = new MaterialPass(pipeline);

            foreach (var binding in _defaultBindings)
            {
                pass.BindResource((ResourceBinding)binding.Clone()); 
            }

            // Apply default push constants
            foreach (var (name, value) in _defaultPushConstants)
            {
                SetPushConstant(pass, name, value);
            }

            return pass;
        }

        private static void SetPushConstant(MaterialPass pass, string name, object value)
        {
            if (value == null) return;

            var method = typeof(MaterialPass).GetMethod("PushConstant", BindingFlags.Public | BindingFlags.Instance);
            if (method == null) return;

            try
            {
                var genericMethod = method.MakeGenericMethod(value.GetType());
                genericMethod.Invoke(pass, new[] { name, value });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set push constant '{name}': {ex.Message}");
            }
        }
    }
}