
using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public class VkFence : VkObject<Fence>
    {
        private readonly VulkanContext _context;

        public VkFence(VulkanContext context, in Fence fence)
            : base(fence)
        {
            _context = context;
        }

        public static unsafe VkFence Create(VulkanContext context, in FenceCreateInfo fenceCreateInfo)
        {
            VulkanContext.Vk.CreateFence(context.Device, in fenceCreateInfo, in VulkanContext.CustomAllocator<VkFence>(), out Fence fence)
                .VkAssertResult("Failed to create fence.");
            return new VkFence(context, in fence);
        }

        public static unsafe VkFence CreateSignaled(VulkanContext context)
        {
            FenceCreateInfo fci = new FenceCreateInfo(StructureType.FenceCreateInfo, flags: FenceCreateFlags.SignaledBit);
            return Create(context, fci);
        }

        public static unsafe VkFence CreateNotSignaled(VulkanContext context)
        {
            FenceCreateInfo fci = new FenceCreateInfo(StructureType.FenceCreateInfo, flags: FenceCreateFlags.None);
            return Create(context, fci);
        }

        public void Reset()
        {
            Vk.ResetFences(_context.Device, 1, in _vkObject);
        }

        public void Wait(ulong timeoutMs = 10_000_000_000)
        {
/*            var status = GetFenceStatus();
            if (status == Result.Success) return; // Уже сигнален*/

            Vk.WaitForFences(_context.Device, 1, in _vkObject, true, timeoutMs) // 10 сек
                .VkAssertResult("Failed to wait fence");
        }
        public async Task WaitAsync(CancellationToken cancellationToken = default)
        {
            Wait();
            return;
           /* while (true)
            {
                var result = GetFenceStatus();
                Console.WriteLine(result);
                switch (result)
                {
                    case Result.Success:
                        return;
                    case Result.NotReady:
                        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                        continue;
                    case Result.Timeout:
                        throw new VulkanException(result, "Failed to wait fence, timeout");

                }
            }*/
        }

        public Result GetFenceStatus()
        {
            return Vk.GetFenceStatus(_context.Device, this);
        }
        public override void LabelObject(string name) => _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.Fence, name);

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                unsafe
                {
                    Vk.DestroyFence(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkFence>());
                }

                _disposed = true;
            }
        }
    }
}