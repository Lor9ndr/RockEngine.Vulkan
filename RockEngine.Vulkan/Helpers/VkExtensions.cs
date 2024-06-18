using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.Helpers
{
    internal static class VkExtensions
    {
        public static Result ThrowCode(this Result result, string message)
        {
            return result switch
            {
                Result.Success => result,
                _ => throw new Exception(message + Environment.NewLine + result),
            };
        }
        public static Result ThrowCode(this Result result, Result additionalCheck, string message)
        {
            if (result == Result.Success || result == additionalCheck)
            {
                return result;
            }
            throw new Exception(message + Environment.NewLine + result);
        }
        public static Result ThrowCode(this Result result, string message, params Result[] additionalChecks)
        {
            if (result == Result.Success || additionalChecks.Contains(result))
            {
                return result;
            }
            throw new Exception(message + Environment.NewLine + result);
        }
    }
}
