﻿using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Passes;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

namespace RockEngine.Core.Rendering.PipelineRenderers
{
    internal class DeferredRenderPipeline : IRenderPipeline
    {
        private readonly VulkanContext _context;
        private readonly GeometryPass _geometryPass;
        private readonly LightingPass _lightingPass;
        private readonly PostLightPass _postLightPass;
        private readonly ScreenPass _screenPass;
        private readonly ImGuiPass _imGuiPass;

        public LightingPass LightingPass => _lightingPass;

        internal DeferredRenderPipeline(
            VulkanContext context,
            GeometryPass geometryPass,
            LightingPass lightingPass,
            PostLightPass postLightPass,
            ScreenPass screenPass,
            ImGuiPass imGuiPass)
        {
            _context = context;
            _geometryPass = geometryPass;
            _lightingPass = lightingPass;
            _postLightPass = postLightPass;
            _screenPass = screenPass;
            _imGuiPass = imGuiPass;
        }

        public async Task Execute(VkCommandBuffer cmd, CameraManager cameraManager, Renderer renderer)
        {
            foreach (var camera in cameraManager.ActiveCameras)
            {
                await ExecuteCameraPass(cmd, camera, renderer);
            }
          /*  if (cameraManager.ActiveCameras.Count > 0)
            {*/
                await ExecuteFinalPass(cmd, renderer);
            //}

        }

        private async Task ExecuteCameraPass(VkCommandBuffer cmd, Camera camera, Renderer renderer)
        {
            //using (cmd.NameAction("Camera Render Pass", [0.2f, 0.8f, 0.2f, 1.0f]))
            //{

                camera.RenderTarget.PrepareForRender(cmd);
                unsafe
                {
                    fixed (ClearValue* pClearValue = camera.RenderTarget.ClearValues)
                    {
                        var renderPassBeginInfo = new RenderPassBeginInfo
                        {
                            SType = StructureType.RenderPassBeginInfo,
                            RenderPass = camera.RenderTarget.RenderPass,
                            Framebuffer = camera.RenderTarget.Framebuffers[renderer.FrameIndex],
                            RenderArea = camera.RenderTarget.Scissor,
                            ClearValueCount = (uint)camera.RenderTarget.ClearValues.Length,
                            PClearValues = pClearValue
                        };

                        cmd.BeginRenderPass(in renderPassBeginInfo, SubpassContents.SecondaryCommandBuffers);
                    }
                }

                var buffers = cmd.CommandPool.AllocateCommandBuffers(3, CommandBufferLevel.Secondary);
                var subpassIndices = new[] { 0u, 1u, 2u };
                unsafe
                {
                    for (int i = 0; i < buffers.Length; i++)
                    {
                        var inheritanceInfo = new CommandBufferInheritanceInfo
                        {
                            SType = StructureType.CommandBufferInheritanceInfo,
                            RenderPass = camera.RenderTarget.RenderPass,
                            Subpass = subpassIndices[i],
                            Framebuffer = camera.RenderTarget.Framebuffers[renderer.FrameIndex]
                        };

                        buffers[i].Begin(new CommandBufferBeginInfo
                        {
                            SType = StructureType.CommandBufferBeginInfo,
                            Flags = CommandBufferUsageFlags.RenderPassContinueBit,
                            PInheritanceInfo = &inheritanceInfo
                        });
                    buffers[i].LabelObject($"ExecuteCameraPass [{i}] cmd");
                    }
                }

                using (cmd.NameAction("Geometry Pass", [0.8f, 0.2f, 0.2f, 1.0f]))
                {
                    await _geometryPass.Execute(buffers[0], renderer.FrameIndex, camera);
                    buffers[0].End();

                    cmd.ExecuteSecondary(buffers[0]);
                }
                using (cmd.NameAction("Lighting Pass", [0.2f, 0.2f, 0.8f, 1.0f]))
                {
                    cmd.NextSubpass(SubpassContents.SecondaryCommandBuffers);
                    await LightingPass.Execute(buffers[1], camera, renderer.FrameIndex);
                    buffers[1].End();
                    cmd.ExecuteSecondary(buffers[1]);
                }
                using (cmd.NameAction("Post light Pass", [0.4f, 0.4f, 0.4f, 1.0f]))
                {
                    cmd.NextSubpass(SubpassContents.SecondaryCommandBuffers);
                    await _postLightPass.Execute(buffers[2], renderer.FrameIndex, camera);
                    buffers[2].End();
                    cmd.ExecuteSecondary(buffers[2]);
                }

                cmd.EndRenderPass();
                camera.RenderTarget.TransitionToRead(cmd);
                _screenPass.SetInputTexture(camera.RenderTarget.OutputTexture);
            foreach (var item in buffers)
            {
                _context.SubmitContext.AddDependency(item);
            }
            //}
        }

        private async Task ExecuteFinalPass(VkCommandBuffer cmd, Renderer renderer)
        {
            renderer.SwapchainTarget.PrepareForRender(cmd);
            using (cmd.NameAction("Screen composition", [0.7f, 0.7f, 0.7f, 1.0f]))
            {
                unsafe
                {
                    fixed (ClearValue* pClearValue = renderer.SwapchainTarget.ClearValues)
                    {
                        var swapchainBeginInfo = new RenderPassBeginInfo
                        {
                            SType = StructureType.RenderPassBeginInfo,
                            RenderPass = renderer.SwapchainTarget.RenderPass,
                            Framebuffer = renderer.SwapchainTarget.Framebuffers[renderer.FrameIndex],
                            RenderArea = new Rect2D { Extent = renderer.SwapchainTarget.Size },
                            ClearValueCount = (uint)renderer.SwapchainTarget.ClearValues.Length,
                            PClearValues = pClearValue
                        };

                        cmd.BeginRenderPass(in swapchainBeginInfo, SubpassContents.SecondaryCommandBuffers);
                    }
                }

                // Subpass 0: Screen composition
                var screenCmd = cmd.CommandPool.AllocateCommandBuffer(CommandBufferLevel.Secondary);
                unsafe
                {
                    var inheritanceInfo = stackalloc CommandBufferInheritanceInfo[]
                    {
                            new CommandBufferInheritanceInfo()
                            {
                                SType = StructureType.CommandBufferInheritanceInfo,
                                RenderPass = renderer.SwapchainTarget.RenderPass,
                                Subpass = 0,
                                Framebuffer = renderer.SwapchainTarget.Framebuffers[renderer.FrameIndex]
                            }
                        };

                    screenCmd.Begin(new CommandBufferBeginInfo
                    {
                        SType = StructureType.CommandBufferBeginInfo,
                        Flags = CommandBufferUsageFlags.RenderPassContinueBit,
                        PInheritanceInfo = inheritanceInfo
                    });
                }
                using (screenCmd.NameAction("Screen part", [0.7f, 0.7f, 0.7f, 1.0f]))
                {
                    await _screenPass.Execute(screenCmd, renderer);
                }
                using (screenCmd.NameAction("imgui part", [0.7f, 0.7f, 0.7f, 1.0f]))
                {
                    await _imGuiPass.Execute(screenCmd, renderer);
                }
                screenCmd.End();
                cmd.ExecuteSecondary(screenCmd);

                cmd.EndRenderPass();
                _context.SubmitContext.AddDependency(screenCmd);
            }
        }

        public void Dispose()
        {
            _geometryPass.Dispose();
            LightingPass.Dispose();
            _screenPass.Dispose();
            _imGuiPass.Dispose();
        }

        public Task Update()
        {
            /*foreach (var item in _commandBuffers)
            {
                item.Dispose();
            }
            _commandBuffers.Clear();*/
            return Task.CompletedTask;
        }
    }
}
