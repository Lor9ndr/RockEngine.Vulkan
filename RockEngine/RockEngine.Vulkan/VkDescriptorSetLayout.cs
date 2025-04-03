using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public struct VkDescriptorSetLayout : IEquatable<VkDescriptorSetLayout>
    {
        public DescriptorSetLayout DescriptorSetLayout;
        public uint SetLocation;
        public readonly DescriptorSetLayoutBindingReflected[] Bindings;

        public VkDescriptorSetLayout(DescriptorSetLayout descriptorSetLayout, uint setLocation, DescriptorSetLayoutBindingReflected[] bindingsArr)
        {
            DescriptorSetLayout = descriptorSetLayout;
            SetLocation = setLocation;
            Bindings = bindingsArr;
        }


        public bool Equals(VkDescriptorSetLayout other)
        {
            if (SetLocation == other.SetLocation)
            {
                if (Bindings is null && other.Bindings is null)
                {

                    return true;
                }
                if (Bindings is null && other.Bindings is not null)
                {
                    return false;
                }
                if (Bindings is not null && other.Bindings is null)
                {
                    return false;
                }
            }


            if (SetLocation != other.SetLocation || Bindings.Length != other.Bindings.Length)
                return false;

            for (int i = 0; i < Bindings.Length; i++)
            {
                if (!BindingsEqual(Bindings[i], other.Bindings[i]))
                    return false;
            }

            return true;
        }

        private static unsafe bool BindingsEqual(DescriptorSetLayoutBindingReflected a, DescriptorSetLayoutBindingReflected b)
        {
            return a.Binding == b.Binding &&
                   a.DescriptorType == b.DescriptorType &&
                   a.DescriptorCount == b.DescriptorCount &&
                   a.StageFlags == b.StageFlags &&
                   a.PImmutableSamplers == b.PImmutableSamplers;
        }

        public override bool Equals(object obj)
        {
            return obj is VkDescriptorSetLayout layout && Equals(layout);
        }

        public override unsafe int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + SetLocation.GetHashCode();
                foreach (var binding in Bindings)
                {
                    hash = hash * 23 + binding.Binding.GetHashCode();
                    hash = hash * 23 + binding.DescriptorType.GetHashCode();
                    hash = hash * 23 + binding.DescriptorCount.GetHashCode();
                    hash = hash * 23 + binding.StageFlags.GetHashCode();
                    hash = hash * 23 + (binding.PImmutableSamplers != null ? binding.PImmutableSamplers->GetHashCode() : 0);
                }
                return hash;
            }
        }

        public static bool operator ==(VkDescriptorSetLayout left, VkDescriptorSetLayout right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VkDescriptorSetLayout left, VkDescriptorSetLayout right)
        {
            return !(left == right);
        }
    }
}
