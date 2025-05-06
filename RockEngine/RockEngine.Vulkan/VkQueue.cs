
using Silk.NET.Vulkan;

using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RockEngine.Vulkan
{
    public class VkQueue : VkObject<Queue>
    {
        internal readonly Lock _queueLock = new Lock();
        private readonly VulkanContext _context;

        public VkQueue(VulkanContext context, in Queue vkObject)
            : base(vkObject)
        {
            _context = context;
        }

        public void Submit(in SubmitInfo submitInfo, VkFence? fence = null)
        {
            lock (_queueLock)
            {
                SubmitUnsafe(submitInfo, fence);
            }

        }
        public unsafe void Submit(VkCommandBuffer commandBuffer, Semaphore[] singaleSemaphores, Semaphore[] waitSemaphores, PipelineStageFlags[] stageFlags, VkFence? fence = null)
        {
            var nativeCmd = commandBuffer.VkObjectNative;
            fixed (Semaphore* vkSemaphores = singaleSemaphores)
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
                        SubmitUnsafe(in si, fence);
                    }
                }
            }
        }
        public unsafe void Submit(VkCommandBuffer commandBuffer, VkFence? fence = null)
        {
            lock (_queueLock)
            {
                var nativeCmd = commandBuffer.VkObjectNative;
                var submitInfo = new SubmitInfo
                {
                    SType = StructureType.SubmitInfo,
                    CommandBufferCount = 1,
                    PCommandBuffers = &nativeCmd
                };
                SubmitUnsafe(in submitInfo, fence);
            }
        }
        public unsafe void Submit(VkFence? fence, params CommandBuffer[] commandBuffers)
        {
            lock (_queueLock)
            {
                fixed (CommandBuffer* nativeCmd = commandBuffers.ToArray())
                {
                    var submitInfo = new SubmitInfo
                    {
                        SType = StructureType.SubmitInfo,
                        CommandBufferCount = 1,
                        PCommandBuffers = nativeCmd
                    };
                    SubmitUnsafe(in submitInfo, fence);
                }
               
            }
        }

        internal void SubmitUnsafe(in SubmitInfo submitInfo, VkFence? fence)
        {
            fence ??= VkFence.CreateNotSignaled(_context);

            VulkanContext.Vk.QueueSubmit(
                this,
                1,
                in submitInfo,
                fence.VkObjectNative
            ).VkAssertResult("Failed to submit to queue");
            fence.Wait();
        }

        public void WaitIdle()
        {
            VulkanContext.Vk.QueueWaitIdle(this);
        }
        public override void LabelObject(string name) => _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.Queue, name);

        protected override void Dispose(bool disposing)
        {
        }
    }
}
