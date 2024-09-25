
using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public record VkFence : VkObject<Fence>
    {
        private readonly RenderingContext _context;

        public VkFence(RenderingContext context, in Fence fence)
            : base(fence)
        {
            _context = context;
        }

        public unsafe static VkFence Create(RenderingContext context, in FenceCreateInfo fenceCreateInfo)
        {
            RenderingContext.Vk.CreateFence(context.Device, in fenceCreateInfo, in RenderingContext.CustomAllocator<VkFence>(), out Fence fence)
                .VkAssertResult("Failed to create fence.");

            return new VkFence(context, in fence);
        }

        public unsafe static VkFence CreateSignaled(RenderingContext context)
        {
            FenceCreateInfo fci = new FenceCreateInfo(StructureType.FenceCreateInfo, flags: FenceCreateFlags.SignaledBit);
            return Create(context, fci);
        }
        public unsafe static VkFence CreateNotSignaled(RenderingContext context)
        {
            FenceCreateInfo fci = new FenceCreateInfo(StructureType.FenceCreateInfo, flags: FenceCreateFlags.None);
            return Create(context, fci);
        }

        public void Wait()
        {
            Vk.WaitForFences(_context.Device, 1, in _vkObject, true, ulong.MaxValue)
                .VkAssertResult("Failed to wait fence");
        }

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

                    Vk.DestroyFence(_context.Device, _vkObject, in RenderingContext.CustomAllocator<VkFence>());
                }

                _disposed = true;
            }
        }
    }
}