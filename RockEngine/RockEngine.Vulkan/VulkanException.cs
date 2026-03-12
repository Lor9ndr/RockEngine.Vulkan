using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    [Serializable]
    public class VulkanException : Exception
    {
        public Result Result { get; private set; }

        public VulkanException(Result result, string message) :
            base(message + Environment.NewLine + $"Result: {result}")
        {
            Data["Result"] = result;
            Result = result;
        }

        public VulkanException(DebugUtilsMessageSeverityFlagsEXT messageSeverity, string? message):
            base(messageSeverity.ToString() +  Environment.NewLine + message) 
        {
            Data["Result"] = messageSeverity;
        }
    }
}