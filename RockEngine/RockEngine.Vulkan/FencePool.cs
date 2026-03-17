using System.Collections.Concurrent;

namespace RockEngine.Vulkan
{
    /// <summary>
    /// Pool for reusing VkFence objects to avoid frequent creation/destruction.
    /// Thread-safe.
    /// </summary>
    internal sealed class FencePool : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly ConcurrentBag<VkFence> _availableFences = new();
        private bool _disposed;

        public FencePool(VulkanContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Retrieves a fence from the pool or creates a new one if none are available.
        /// The fence is guaranteed to be in an unsignaled state.
        /// </summary>
        public VkFence GetFence()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FencePool));

            if (_availableFences.TryTake(out var fence))
            {
                // Reset the fence before reuse (must be signaled when returned)
                fence.Reset();
                return fence;
            }

            // No free fence – create a new one
            return VkFence.CreateNotSignaled(_context);
        }

        /// <summary>
        /// Returns a fence to the pool for later reuse.
        /// The fence must be in a signaled state before being returned.
        /// </summary>
        public void ReturnFence(VkFence fence)
        {
            if (fence == null)
                throw new ArgumentNullException(nameof(fence));

            if (_disposed)
            {
                // Pool is disposed – clean up the fence immediately
                fence.Dispose();
                return;
            }

            _availableFences.Add(fence);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (var fence in _availableFences)
            {
                fence.Dispose();
            }
            _availableFences.Clear();
        }
    }
}