using Silk.NET.SPIRV.Reflect;
using Silk.NET.Vulkan;


namespace RockEngine.Vulkan
{
    public class VkShaderModule : VkObject<ShaderModule>
    {
        private readonly VulkanContext _context;
        private readonly ShaderStageFlags _stage;
        private readonly ShaderReflectionData _reflectedData;

        /// <summary>
        /// Shader stage (e.g. fragment, vertex)
        /// </summary>
        public ShaderStageFlags Stage => _stage;

        public ShaderReflectionData ReflectedData => _reflectedData;

        public VkShaderModule(VulkanContext context, ShaderModule module, ShaderStageFlags stage, ref ReflectShaderModule reflectShaderModule)
            : base(module)
        {
            _context = context;
            _stage = stage;

            _reflectedData = new ShaderReflectionData(in reflectShaderModule, _stage);
        }

        public static async Task<VkShaderModule> CreateAsync(VulkanContext context, string path, ShaderStageFlags stage, CancellationToken cancellationToken = default)
        {
            var shaderCode = await File.ReadAllBytesAsync(path, cancellationToken)
               .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            return Create(context, shaderCode, stage);
        }

        public static VkShaderModule Create(VulkanContext context, string path, ShaderStageFlags stage)
        {
            var shaderCode = File.ReadAllBytes(path);
            return Create(context, shaderCode, stage);
        }

        public static unsafe VkShaderModule Create(VulkanContext context, byte[] bytes, ShaderStageFlags stage)
        {
            fixed (byte* pshaderCode = bytes)
            {
                var shaderModuleCreateInfo = new ShaderModuleCreateInfo
                {
                    SType = StructureType.ShaderModuleCreateInfo,
                    CodeSize = (nuint)bytes.Length,
                    PCode = (uint*)pshaderCode
                };
                VulkanContext.Vk.CreateShaderModule(
                    context.Device,
                    in shaderModuleCreateInfo,
                    in VulkanContext.CustomAllocator<VkShaderModule>(),
                    out var shaderModule).VkAssertResult($"Failed to create shader module: {stage}");

                var reflectorApi = Reflect.GetApi();
                var reflected = new ReflectShaderModule(Generator.KhronosSpirvToolsAssembler);
                reflectorApi.CreateShaderModule((nuint)bytes.Length, pshaderCode, ref reflected);

                return new VkShaderModule(context, shaderModule, stage, ref reflected);
            }
        }
        public static unsafe VkShaderModule Create(VulkanContext context, uint[] data, ShaderStageFlags stage)
        {
            fixed (uint* pshaderCode = data)
            {
                var shaderModuleCreateInfo = new ShaderModuleCreateInfo
                {
                    SType = StructureType.ShaderModuleCreateInfo,
                    CodeSize = (nuint)(data.Length * sizeof(uint)),
                    PCode = pshaderCode
                };
                VulkanContext.Vk.CreateShaderModule(context.Device, in shaderModuleCreateInfo, in VulkanContext.CustomAllocator<VkShaderModule>(), out var shaderModule)
                    .VkAssertResult($"Failed to create shader module: {stage}");
                var reflectorApi = Reflect.GetApi();
                var reflected = new ReflectShaderModule(Generator.KhronosSpirvToolsAssembler);
                reflectorApi.CreateShaderModule((nuint)data.Length, pshaderCode, ref reflected);

                return new VkShaderModule(context, shaderModule, stage, ref reflected);
            }
        }

        public override void LabelObject(string name) => _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.ShaderModule, name);

        protected override unsafe void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                if (_vkObject.Handle != default)
                {
                    VulkanContext.Vk.DestroyShaderModule(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkShaderModule>());
                }

                _disposed = true;
            }
        }
    }
}