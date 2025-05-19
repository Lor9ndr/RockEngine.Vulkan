using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Passes
{
    public class LightingPass : Subpass
    {
        private readonly LightManager _lightManager;
        private readonly TransformManager _transformManager;
        private readonly IndirectCommandManager _indirectCommands;
        private readonly VkPipeline _lightingPipeline;
        private readonly World _world;
        private readonly GlobalUbo _globalUbo;
        private readonly UniformBufferBinding _binding;
        private TextureBinding _IblBinding;

        public LightingPass(
            VulkanContext context,
            BindingManager bindingManager,
            LightManager lightManager,
            TransformManager transformManager,
            IndirectCommandManager indirectCommands,
            GlobalUbo globalUbo,
            VkPipeline lightingPipeline,
            World world)
            : base(context, bindingManager)
        {
            _lightManager = lightManager;
            _transformManager = transformManager;
            _indirectCommands = indirectCommands;
            _lightingPipeline = lightingPipeline;
            _world = world;
            _globalUbo = globalUbo;
            _binding = new UniformBufferBinding(_globalUbo, 0, 0);

           
        }

        protected override uint Order => 1;

        public override Task Execute(VkCommandBuffer cmd, params object[] args)
        {
            var camera = args[0] as Camera ?? throw new ArgumentNullException(nameof(Camera));
            uint frameIndex = (uint)args[1];

            cmd.SetViewport(camera.RenderTarget.Viewport);
            cmd.SetScissor(camera.RenderTarget.Scissor);
            camera.RenderTarget.GBuffer.Material.Bind(_lightManager.GetCurrentLightBufferBinding());

            if(_IblBinding != null)
            {
                camera.RenderTarget.GBuffer.Material.Bind(_IblBinding);
            }

            cmd.BindPipeline(_lightingPipeline, PipelineBindPoint.Graphics);
            
            camera.RenderTarget.GBuffer.Material.CmdPushConstants(cmd);

            BindingManager.BindResourcesForMaterial(camera.RenderTarget.GBuffer.Material, cmd);
            cmd.Draw(3, 1, 0, 0);
            return Task.CompletedTask;
        }

        internal void SetIBLTextures(Texture irradiance, Texture prefilter, Texture brdfLUT)
        {
            
            _IblBinding =  new TextureBinding(3, 0, default,irradiance, prefilter, brdfLUT);
        }

        private Silk.NET.Core.Bool32 GetMultiDrawIndirectFeature()
        {
            return Context.Device.PhysicalDevice.Features2.Features.MultiDrawIndirect;
        }
    }
}
