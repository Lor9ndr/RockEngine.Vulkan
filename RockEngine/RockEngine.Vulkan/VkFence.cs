
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

        public void Wait()
        {
            Vk.WaitForFences(_context.Device, 1, in _vkObject, true, ulong.MaxValue)
                .VkAssertResult("Failed to wait fence");
        }
        public ValueTask WaitAsync()
        {
            Wait();
            return default;
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
                    var fenceStatus = Vk.GetFenceStatus(_context.Device, this);
                    // Awesome feature, by requesting fence status we avoid that error :)
                    //System.Exception: "Vulkan Error: Validation Error: [ VUID-vkDestroyFence-fence-01120 ]
                    //Object 0: handle = 0x9389c50000000061, type = VK_OBJECT_TYPE_FENCE; | MessageID = 0x5d296248 |
                    //vkDestroyFence(): fence (VkFence 0x9389c50000000061[]) is in use.
                    //The Vulkan spec states: All queue submission commands that refer to fence must have completed execution 

                    Vk.DestroyFence(_context.Device, _vkObject, in VulkanContext.CustomAllocator<VkFence>());
                }

                _disposed = true;
            }
        }

       
    }
}