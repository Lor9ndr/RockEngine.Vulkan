using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;

using Silk.NET.SPIRV.Reflect;
using Silk.NET.Vulkan;

using System.Reflection;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanShaderModuleBuilder : DisposableBuilder
    {
        private readonly Vk _vk;
        private readonly LogicalDeviceWrapper _device;

        public VulkanShaderModuleBuilder(Vk vk, LogicalDeviceWrapper device)
        {
            _vk = vk;
            _device = device;
        }

        public async ValueTask<ShaderModuleWrapper> Build(string filePath, ShaderStageFlags flag, CancellationToken cancellationToken = default)
        {
            var shaderCode = await File.ReadAllBytesAsync(filePath, cancellationToken)
                .ConfigureAwait(false);

            var pShaderCode = CreateMemoryHandle(shaderCode);
            cancellationToken.ThrowIfCancellationRequested();
            unsafe
            {
                var shaderModuleCreateInfo = new ShaderModuleCreateInfo
                {
                    SType = StructureType.ShaderModuleCreateInfo,
                    CodeSize = (nuint)shaderCode.Length,
                    PCode = (uint*)pShaderCode.Pointer
                };
             
                _vk.CreateShaderModule(_device.Device, in shaderModuleCreateInfo, null, out var shaderModule)
                    .ThrowCode($"Failed to create shader module: {filePath}");
                var reflectorApi = Reflect.GetApi();
                var reflected = new ReflectShaderModule();
                reflectorApi.CreateShaderModule((nuint)shaderCode.Length, pShaderCode.Pointer, ref reflected);
                return new ShaderModuleWrapper(_vk, shaderModule, _device, flag, ref reflected);
            }
        }
    }
}
