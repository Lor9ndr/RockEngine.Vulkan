using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    [Serializable]
    internal class VulkanException : Exception
    {
        private Result _status;
        private string _v;

        public VulkanException()
        {
        }

        public VulkanException(string? message) : base(message)
        {
        }

        public VulkanException(Result status, string v)
        {
            _status = status;
            _v = v;
        }

        public VulkanException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}