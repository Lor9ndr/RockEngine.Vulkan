using RockEngine.Core.Builders;
using RockEngine.Core.CoreObjects;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public class ComputeShaderManager
    {
        private readonly VulkanContext _context;
        private readonly ShaderManager _shaderManager;
        private readonly PipelineManager _pipelineManager;

        public ComputeShaderManager(VulkanContext context, ShaderManager shaderManager, PipelineManager pipelineManager)
        {
            _context = context;
            _shaderManager = shaderManager;
            _pipelineManager = pipelineManager;
        }

        public async Task<RckPipeline> CreateComputePipelineAsync(string shaderName, string pipelineName)
        {
            var shader = new Shader(_context, _shaderManager.GetShader(shaderName));
            return _pipelineManager.Create(new ComputePipelineBuilder(_context, pipelineName)
                .WithShaderModule(shader));
        }

        public void Dispatch(UploadBatch cmd, uint groupX, uint groupY, uint groupZ)
        {
            cmd.Dispatch(groupX, groupY, groupZ);
        }
    }
}
