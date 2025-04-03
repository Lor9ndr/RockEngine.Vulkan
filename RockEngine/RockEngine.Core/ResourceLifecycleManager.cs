using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering
{
    public sealed class ResourceLifecycleManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly List<FrameResources> _pendingFrames = new List<FrameResources>();
        private readonly object _syncRoot = new object();
        private ulong _currentFrameNumber;

        public ResourceLifecycleManager(VulkanContext context)
        {
            _context = context;
        }

        private struct FrameResources
        {
            public VkFence Fence;
            public List<IDisposable> Resources;
            public ulong FrameNumber;
        }

        public void BeginFrame()
        {
            _currentFrameNumber++;
        }

        public void RegisterFrameResources(VkFence fence)
        {
            lock (_syncRoot)
            {
                _pendingFrames.Add(new FrameResources
                {
                    Fence = fence,
                    Resources = new List<IDisposable>(),
                    FrameNumber = _currentFrameNumber
                });
            }
        }

        public void ScheduleDisposal(IDisposable resource)
        {
            lock (_syncRoot)
            {
                var frame = _pendingFrames.Find(f => f.FrameNumber == _currentFrameNumber);
                frame.Resources?.Add(resource);
            }
        }

        public void ProcessCompletedFrames()
        {
            lock (_syncRoot)
            {
                for (int i = _pendingFrames.Count - 1; i >= 0; i--)
                {
                    foreach (var resource in _pendingFrames[i].Resources)
                    {
                        resource.Dispose();
                    }
                    _pendingFrames[i].Fence.Dispose();
                    _pendingFrames.RemoveAt(i);
                }
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                foreach (var frame in _pendingFrames)
                {
                    frame.Fence.Dispose();
                    foreach (var res in frame.Resources)
                    {
                        res.Dispose();
                    }
                }
                _pendingFrames.Clear();
            }
        }
    }
}