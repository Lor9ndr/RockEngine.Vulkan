using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

using Silk.NET.SDL;
using Silk.NET.Vulkan;

using System;

namespace RockEngine.Core.Rendering.Passes
{
    public class LightingPass : RenderPass
    {
        private readonly LightManager _lightManager;
        private readonly VkPipeline _lightingPipeline;

        public LightingPass(
            VulkanContext context,
            BindingManager bindingManager,
            LightManager lightManager,
            VkPipeline lightingPipeline)
            : base(context, bindingManager)
        {
            _lightManager = lightManager;
            _lightingPipeline = lightingPipeline;
        }

        public override async Task Execute(VkCommandBuffer cmd, params object[] args)
        {
            var camera = args[0] as Camera ?? throw new ArgumentNullException(nameof(Camera));
            cmd.SetViewport(camera.RenderTarget.Viewport);
            cmd.SetScissor(camera.RenderTarget.Scissor);
            cmd.BindPipeline(_lightingPipeline, PipelineBindPoint.Graphics);
           
            BindingManager.BindResourcesForMaterial(camera.RenderTarget.GBuffer.Material, cmd);

            cmd.Draw(3, 1, 0, 0);
        }
    }
}
