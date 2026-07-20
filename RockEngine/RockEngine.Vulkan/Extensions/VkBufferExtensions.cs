using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.Extensions
{
    public static class VkBufferExtensions
    {
        extension(VkBuffer buffer)
        {
            public ulong GetDeviceAddress()
            {
                var info = new BufferDeviceAddressInfo
                {
                    SType = StructureType.BufferDeviceAddressInfo,
                    Buffer = buffer
                };
                return VK.GetBufferDeviceAddress(GetCurrent().Device, ref info);
            }
        }
    }

}
