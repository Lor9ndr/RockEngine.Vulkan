using RockEngine.Vulkan;
using Silk.NET.Vulkan;
using RockEngine.Core.Builders;

namespace RockEngine.Core.Rendering.Managers
{
    public class ComputeShaderManager
    {
        private readonly VulkanContext _context;
        private readonly BindingManager _bindingManager;
        private readonly PipelineManager _pipelineManager;

        public ComputeShaderManager(VulkanContext context, BindingManager bindingManager, PipelineManager pipelineManager)
        {
            _context = context;
            _bindingManager = bindingManager;
            _pipelineManager = pipelineManager;
        }

        public async Task<VkPipeline> CreateComputePipelineAsync(string shaderPath,string pipelineName)
        {
            var shader = await VkShaderModule.CreateAsync(
                _context,
                shaderPath,
                ShaderStageFlags.ComputeBit
            );
            
            return _pipelineManager.Create(new ComputePipelineBuilder(_context, pipelineName)
                .WithShaderModule(shader));
        }

        public void Dispatch(VkCommandBuffer cmd, uint groupX, uint groupY, uint groupZ)
        {
            VulkanContext.Vk.CmdDispatch(cmd, groupX, groupY, groupZ);
        }
    }
}
