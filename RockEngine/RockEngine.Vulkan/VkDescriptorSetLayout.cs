using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public struct VkDescriptorSetLayout : IEquatable<VkDescriptorSetLayout>
    {
        public DescriptorSetLayout DescriptorSetLayout;
        public uint SetLocation;
        public readonly DescriptorSetLayoutBindingReflected[] Bindings;
        public readonly int StageFlagsSum;

        public VkDescriptorSetLayout(DescriptorSetLayout descriptorSetLayout, uint setLocation, DescriptorSetLayoutBindingReflected[] bindingsArr)
        {
            DescriptorSetLayout = descriptorSetLayout;
            SetLocation = setLocation;
            Bindings = bindingsArr;
            StageFlagsSum = bindingsArr.Sum(s=>(int)s.StageFlags);
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
            {
                return false;
            }

            for (int i = 0; i < Bindings.Length; i++)
            {
                if (!BindingsEqual(Bindings[i], other.Bindings[i]))
                {
                    return false;
                }
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
