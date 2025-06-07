
using Silk.NET.Vulkan;

using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RockEngine.Vulkan
{
    public class VkQueue : VkObject<Queue>
    {
        internal readonly Mutex _queueLock = new Mutex();
        private readonly VulkanContext _context;
        public uint FamilyIndex { get; private set; }

        public VkQueue(VulkanContext context, in Queue vkObject, uint familyIndex)
            : base(vkObject)
        {
            _context = context;
            FamilyIndex = familyIndex;
        }

        public void Submit(in SubmitInfo submitInfo, VkFence? fence = null)
        {
            SubmitUnsafe(submitInfo, fence);
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

            public unsafe void Submit(Span<CommandBuffer> commandBuffers, Span<Semaphore> singaleSemaphores, Span<Semaphore> waitSemaphores, PipelineStageFlags[] stageFlags, VkFence? fence = null)
        {
            fixed (CommandBuffer* pCommandbuffers = commandBuffers)
            fixed (Semaphore* vkSemaphores = singaleSemaphores)
            fixed (PipelineStageFlags* pstageflags = stageFlags)
            fixed (Semaphore* pWaitSemaphores = waitSemaphores)
            {
                SubmitInfo si = new SubmitInfo()
                {
                    SType = StructureType.SubmitInfo,
                    CommandBufferCount = (uint)commandBuffers.Length,
                    PCommandBuffers = pCommandbuffers,
                    PSignalSemaphores = vkSemaphores,
                    SignalSemaphoreCount = (uint)singaleSemaphores.Length,
                    PWaitDstStageMask = pstageflags,
                    PWaitSemaphores = pWaitSemaphores,
                    WaitSemaphoreCount = (uint)waitSemaphores.Length
                };
                SubmitUnsafe(in si, fence);
            }
        }
        public unsafe void Submit(VkCommandBuffer commandBuffer, VkFence? fence = null)
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
        public unsafe void Submit(VkFence? fence, params CommandBuffer[] commandBuffers)
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

        internal void SubmitUnsafe(in SubmitInfo submitInfo, VkFence? fence)
        {
            _queueLock.WaitOne();
            try
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
            finally
            {
                _queueLock.ReleaseMutex();
            }
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
