namespace RockEngine.Vulkan
{
    public readonly struct DebugLabelScope : IDisposable
    {
        private readonly DebugUtilsFunctions _debugUtils;
        private readonly VkCommandBuffer _commandBuffer;

        public DebugLabelScope(DebugUtilsFunctions debugUtils, VkCommandBuffer commandBuffer, string labelName, float[] color)
        {
            _debugUtils = debugUtils;
            _commandBuffer = commandBuffer;

            if (_debugUtils._cmdBeginDebugUtilsLabel != null)
            {
                if (color.Length < 4)
                {
                    throw new ArgumentException("Color array must contain at least 4 elements", nameof(color));
                }
                _debugUtils.CmdBeginDebugUtilsLabel(_commandBuffer, labelName, color);

            }
        }

        public void Dispose()
        {
            _debugUtils._cmdEndDebugUtilsLabel?.Invoke(_commandBuffer);
        }
    }
}
