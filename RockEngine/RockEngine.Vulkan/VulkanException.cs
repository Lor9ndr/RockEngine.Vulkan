using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    [Serializable]
    internal class VulkanException : Exception
    {

        public VulkanException(Result result, string message) :
            base(message + Environment.NewLine + $"Result: {result}")
        {
            Data["Result"] = result;
        }

        public VulkanException(DebugUtilsMessageSeverityFlagsEXT messageSeverity, string? message):
            base(messageSeverity.ToString() +  Environment.NewLine + message) 
        {
            Data["Result"] = messageSeverity;
        }
    }
}