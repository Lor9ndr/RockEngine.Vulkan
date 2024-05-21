using RockEngine.Vulkan.Helpers;
using RockEngine.Vulkan.VkObjects;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan.VkBuilders
{
    internal class VulkanShaderModuleBuilder : DisposableBuilder
    {
        private readonly Vk _vk;
        private readonly VulkanLogicalDevice _device;

        public VulkanShaderModuleBuilder(Vk vk, VulkanLogicalDevice device)
        {
            _vk = vk;
            _device = device;
        }

        public async Task<VulkanShaderModule> Build(string filePath,CancellationToken cancellationToken = default)
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
                return new VulkanShaderModule(_vk, shaderModule, _device);
            }
        }
/*
        public VulkanShaderModule Build(string filePath, CancellationToken cancellationToken = default)
        {
            var shaderCode =  File.ReadAllBytes(filePath, cancellationToken);

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
                return new VulkanShaderModule(_vk, shaderModule, _device);
            }
        }*/
    }

}
