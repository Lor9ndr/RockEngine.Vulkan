using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Core.Rendering.Texturing;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.Passes
{
    public class ScreenPass : Subpass
    {
        private readonly VkPipeline _screenPipeline;
        private readonly SwapchainRenderTarget _swapchainTarget;
        private readonly Material _screenMaterial;
        protected Dictionary<Texture, TextureBinding> Bindings = new Dictionary<Texture, TextureBinding>();

        protected override uint Order => 2;

        public ScreenPass(
            VulkanContext context,
            BindingManager bindingManager,
            VkPipeline screenPipeline,
            SwapchainRenderTarget swapchainTarget)
            : base(context, bindingManager)
        {
            _screenPipeline = screenPipeline;
            _swapchainTarget = swapchainTarget;
            _screenMaterial = new Material(_screenPipeline);
        }

        public override Task Execute(VkCommandBuffer cmd, params object[] args)
        {
            var renderer = args[0] as Renderer ?? throw new ArgumentNullException(nameof(Renderer));
            cmd.SetViewport(_swapchainTarget.Viewport);
            cmd.SetScissor(_swapchainTarget.Scissor);

            cmd.BindPipeline(_screenPipeline, PipelineBindPoint.Graphics);
            if(!_screenMaterial.IsComplete)
            {
                return Task.CompletedTask;
            }
            BindingManager.BindResourcesForMaterial(renderer.FrameIndex, _screenMaterial, cmd);
            cmd.Draw(3, 1, 0, 0);
            return Task.CompletedTask;
        }

        internal void SetInputTexture(Texture outputTexture)
        {
            if(!Bindings.TryGetValue(outputTexture, out var binding))
            {
                binding = new TextureBinding(0, 0, ImageLayout.ShaderReadOnlyOptimal, outputTexture);
                Bindings.Add(outputTexture, binding);
            }
            _screenMaterial.Bind(binding);
        }
    }
}