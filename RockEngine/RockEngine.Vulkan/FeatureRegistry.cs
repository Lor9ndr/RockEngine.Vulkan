using RockEngine.Vulkan.DeviceFeatures;

namespace RockEngine.Vulkan
{
    public class FeatureRegistry
    {
        private readonly List<DeviceFeature> _features = new();
        private readonly HashSet<string> _enabledFeatureNames = new();

        public IReadOnlyList<DeviceFeature> Features => _features;
        public IReadOnlySet<string> EnabledFeatures => _enabledFeatureNames;

        public void RequestFeature(DeviceFeature feature)
        {
            _features.Add(feature);
        }

        /// <summary>
        /// Queries the physical device and marks features as enabled if supported.
        /// Returns true if all REQUIRED features are supported.
        /// </summary>
        public bool CheckSupport(VkPhysicalDevice physicalDevice, out List<DeviceFeature> unsupportedRequired)
        {
            unsupportedRequired = new List<DeviceFeature>();
            foreach (var feature in _features)
            {
                if (feature.IsSupported(physicalDevice))
                {
                    _enabledFeatureNames.Add(feature.Name);
                }
                else if (feature.IsRequired)
                {
                    unsupportedRequired.Add(feature);
                }
            }
            return unsupportedRequired.Count == 0;
        }

        public T? TryGetFeature<T>()
        {
            return Features.OfType<T>().FirstOrDefault();
        }

        /// <summary>
        /// Returns all required extensions from enabled features.
        /// </summary>
        public IEnumerable<string> GetAllRequiredExtensions()
        {
            return _features
                .Where(f => _enabledFeatureNames.Contains(f.Name))
                .SelectMany(f => f.GetRequiredExtensions())
                .Distinct();
        }

        /// <summary>
        /// Returns all preprocessor defines from enabled features.
        /// </summary>
        public IEnumerable<string> GetAllPreprocessorDefines()
        {
            return _features
                .Where(f => _enabledFeatureNames.Contains(f.Name))
                .SelectMany(f => f.GetPreprocessorDefines())
                .Distinct();
        }

       
    }
}