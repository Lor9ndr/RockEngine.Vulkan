
using Silk.NET.Vulkan;

using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RockEngine.Vulkan
{
    public record VkQueue : VkObject<Queue>
    {
        public VkQueue(in Queue vkObject) 
            : base(vkObject)
        {
        }

        public void Submit(in SubmitInfo submitInfo, VkFence? fence = null)
        {
            RenderingContext.Vk.QueueSubmit(this, 1, in submitInfo, fence is null ? default: fence.VkObjectNative)
                .VkAssertResult("Failed to submit to queue");
        }
        public unsafe void Submit(VkCommandBuffer commandBuffer, Semaphore[] singaleSemaphores, Semaphore[] waitSemaphores, PipelineStageFlags[] stageFlags, VkFence? fence = null)
        {
            var nativeCmd = commandBuffer.VkObjectNative;
            fixed(Semaphore* vkSemaphores = singaleSemaphores)
            {
                fixed (PipelineStageFlags* pstageflags = stageFlags)
                {
                    fixed (Semaphore* pWaitSemaphores = waitSemaphores)
                    {
                        SubmitInfo si = new SubmitInfo()
                        {
                            SType = StructureType.SubmitInfo,
                            CommandBufferCount = 1,
                            PCommandBuffers = &nativeCmd,
                            PSignalSemaphores = vkSemaphores,
                            SignalSemaphoreCount = (uint)singaleSemaphores.Length,
                            PWaitDstStageMask = pstageflags,
                            PWaitSemaphores = pWaitSemaphores,
                            WaitSemaphoreCount = (uint)waitSemaphores.Length
                        };
                        Submit(in si, fence);
                    }
                }

            }
           
         
        }
        public void WaitIdle()
        {
            RenderingContext.Vk.QueueWaitIdle(this);
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}
