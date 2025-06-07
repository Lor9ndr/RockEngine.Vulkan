using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

using System.Runtime.InteropServices;

namespace RockEngine.Vulkan
{
    public unsafe class DebugUtilsFunctions
    {
        private readonly Vk _vk;
        private readonly Device _device;

        // Объявляем делегаты для функций расширения
        internal delegate void vkCmdBeginDebugUtilsLabelEXTDelegate(CommandBuffer commandBuffer, DebugUtilsLabelEXT* pLabelInfo);
        internal delegate void vkCmdEndDebugUtilsLabelEXTDelegate(CommandBuffer commandBuffer);
        internal delegate Result vkSetDebugUtilsObjectNameEXTDelegate(Device device, DebugUtilsObjectNameInfoEXT* pNameInfo);

        internal vkCmdBeginDebugUtilsLabelEXTDelegate _cmdBeginDebugUtilsLabel;
        internal vkCmdEndDebugUtilsLabelEXTDelegate _cmdEndDebugUtilsLabel;
        internal vkSetDebugUtilsObjectNameEXTDelegate _setDebugUtilsObjectName;

        public DebugUtilsFunctions(Vk vk, Device device)
        {
            _vk = vk;
            _device = device;
            LoadFunctions();
        }

        private void LoadFunctions()
        {
            _cmdBeginDebugUtilsLabel = GetProcDelegate<vkCmdBeginDebugUtilsLabelEXTDelegate>("vkCmdBeginDebugUtilsLabelEXT");
            _cmdEndDebugUtilsLabel = GetProcDelegate<vkCmdEndDebugUtilsLabelEXTDelegate>("vkCmdEndDebugUtilsLabelEXT");
            _setDebugUtilsObjectName = GetProcDelegate<vkSetDebugUtilsObjectNameEXTDelegate>("vkSetDebugUtilsObjectNameEXT");
        }

        private T GetProcDelegate<T>(string name) where T : Delegate
        {
            var ptr = _vk.GetDeviceProcAddr(_device, name);
            return ptr != nint.Zero ? Marshal.GetDelegateForFunctionPointer<T>(ptr) : null;
        }

        public unsafe void CmdBeginDebugUtilsLabel(CommandBuffer commandBuffer, string labelName, float[] color)
        {
            if (_cmdBeginDebugUtilsLabel != null)
            {
                var labelPtr = SilkMarshal.StringToPtr(labelName);
                fixed (float* colorPtr = color)
                {
                    var labelInfo = new DebugUtilsLabelEXT
                    {
                        SType = StructureType.DebugUtilsLabelExt,
                        PLabelName = (byte*)labelPtr,
                    };
                    // rgba
                    for (int i = 0; i < 4; i++)
                    {
                        labelInfo.Color[i] = color[i];
                    }

                    _cmdBeginDebugUtilsLabel(commandBuffer, &labelInfo);
                    SilkMarshal.FreeString(labelPtr);
                }
            }
        }

        public unsafe void CmdEndDebugUtilsLabel(CommandBuffer commandBuffer)
        {
            _cmdEndDebugUtilsLabel?.Invoke(commandBuffer);
        }

        public unsafe void SetDebugUtilsObjectName<T>(T handle, ObjectType objectType, string name) where T : unmanaged
        {
            if (_setDebugUtilsObjectName != null)
            {
                var namePtr = SilkMarshal.StringToPtr(name);
                {
                    var nameInfo = new DebugUtilsObjectNameInfoEXT
                    {
                        SType = StructureType.DebugUtilsObjectNameInfoExt,
                        ObjectType = objectType,
                        ObjectHandle = (ulong)(*(nint*)&handle),
                        PObjectName = (byte*)namePtr
                    };
                    _setDebugUtilsObjectName(_device, &nameInfo);
                    SilkMarshal.FreeString(namePtr);
                }
            }
        }

        public DebugLabelScope CmdDebugLabelScope(VkCommandBuffer commandBuffer, string labelName, float[] color)
        {
            return new DebugLabelScope(this, commandBuffer, labelName, color);
        }
    }
}
