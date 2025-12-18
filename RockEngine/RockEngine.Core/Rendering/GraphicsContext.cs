using RockEngine.Vulkan;

using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

using System.Diagnostics;

using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RockEngine.Core.Rendering
{
    public sealed class GraphicsContext : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly KhrSwapchain _swapchainApi;
        private readonly SwapchainEntry[] _swapchains;
        private readonly FrameState[] _frames;
        private readonly int _frameCount;
        private int _currentFrameIndex;
        private int _activeSwapchainCount;
        private bool _disposed;
        private ulong _frameNumber;

        // Pre-allocated arrays for presentation (reused each frame)
        private readonly SwapchainKHR[] _presentSwapchains = new SwapchainKHR[16];
        private readonly uint[] _presentImageIndices = new uint[16];
        private readonly Semaphore[] _presentWaitSemaphores = new Semaphore[16];

        // Pool of available semaphores (to avoid allocations)
        private readonly Queue<VkSemaphore> _availableSemaphores = new Queue<VkSemaphore>();
        private const int SEMAPHORE_POOL_SIZE = 16;

        public VkSwapchain MainSwapchain { get; private set; }
        public uint FrameIndex => (uint)_currentFrameIndex;
        public int MaxFramesInFlight => _frameCount;

        private struct SwapchainEntry
        {
            public VkSwapchain Swapchain;
            public bool NeedsRecreation;
            public bool IsMain;

            // PER-FRAME ACQUIRE SEMAPHORES (not per-image)
            public VkSemaphore[] ImageAvailableSemaphores;  // One per frame in flight

            // PER-IMAGE RENDER SEMAPHORES
            public VkSemaphore[] RenderCompleteSemaphores;  // One per swapchain image

            // Track semaphore usage state
            public bool[] ImageAvailableInUse;  // Is the frame's acquire semaphore in use?
            public bool[] RenderCompleteInUse;  // Is the image's render semaphore in use?

            // Track which frame is currently using each image
            public int[] ImageUserFrameIndex;   // Frame index using the image, or -1 if free
            public ulong[] ImageLastUsedFrame;  // Frame number when image was last used

            // Track which image index each frame acquired
            public uint[] FrameAcquiredImageIndex; // Image index for each frame
        }

        private sealed class FrameState : IDisposable
        {
            public VkFence InFlightFence;
            public UploadBatch CurrentBatch;
            public ulong FrameNumber;
            public int AcquiredSwapchainCount;

            public readonly int[] AcquiredSwapchainIndices = new int[16];
            public readonly uint[] AcquiredImageIndices = new uint[16];

            public readonly List<VkSemaphore> SemaphoresToReturn = new List<VkSemaphore>(8);
            public readonly List<IDisposable> Resources = new List<IDisposable>(32);
            public FlushOperation FlushOperation;

            public void Reset()
            {
                for (int i = Resources.Count - 1; i >= 0; i--)
                {
                    var resource = Resources[i];
                    if (resource is not VkSemaphore)
                    {
                        VulkanContext.GetCurrent().GraphicsSubmitContext.AddDependency(resource);
                        Resources.RemoveAt(i);
                    }
                }

                AcquiredSwapchainCount = 0;
                CurrentBatch = null;
                FlushOperation = null;
                SemaphoresToReturn.Clear();
            }

            public void Dispose()
            {
                FlushOperation?.Dispose();
                InFlightFence?.Dispose();
                Reset();
            }
        }

        public GraphicsContext(VulkanContext context)
        {
            _context = context;
            _swapchainApi = new KhrSwapchain(VulkanContext.Vk.Context);
            _frameCount = context.MaxFramesPerFlight;

            _swapchains = new SwapchainEntry[32];
            _frames = new FrameState[_frameCount];

            // Pre-allocate semaphore pool
            for (int i = 0; i < SEMAPHORE_POOL_SIZE; i++)
            {
                _availableSemaphores.Enqueue(VkSemaphore.Create(_context));
            }

            for (int i = 0; i < _frameCount; i++)
            {
                _frames[i] = new FrameState
                {
                    InFlightFence = VkFence.CreateNotSignaled(context)
                };
            }
        }

        public void AddSwapchain(VkSwapchain swapchain)
        {
            if (_disposed) ThrowDisposed();

            for (int i = 0; i < _swapchains.Length; i++)
            {
                if (_swapchains[i].Swapchain == null)
                {
                    ref var entry = ref _swapchains[i];

                    entry.Swapchain = swapchain;
                    entry.IsMain = (MainSwapchain == null);

                    // Get actual image count from swapchain
                    int imageCount = (int)swapchain.SwapChainImagesCount;

                    // Create per-frame acquire semaphores
                    entry.ImageAvailableSemaphores = new VkSemaphore[_frameCount];
                    entry.ImageAvailableInUse = new bool[_frameCount];
                    entry.FrameAcquiredImageIndex = new uint[_frameCount];

                    // Create per-image render semaphores
                    entry.RenderCompleteSemaphores = new VkSemaphore[imageCount];
                    entry.RenderCompleteInUse = new bool[imageCount];
                    entry.ImageUserFrameIndex = new int[imageCount];
                    entry.ImageLastUsedFrame = new ulong[imageCount];

                    // Allocate semaphores from pool
                    for (int frameIdx = 0; frameIdx < _frameCount; frameIdx++)
                    {
                        entry.ImageAvailableSemaphores[frameIdx] = AllocateSemaphoreFromPool();
                        entry.ImageAvailableInUse[frameIdx] = false;
                        entry.FrameAcquiredImageIndex[frameIdx] = uint.MaxValue; // Mark as not acquired
                    }

                    for (int imgIdx = 0; imgIdx < imageCount; imgIdx++)
                    {
                        entry.RenderCompleteSemaphores[imgIdx] = AllocateSemaphoreFromPool();
                        entry.RenderCompleteInUse[imgIdx] = false;
                        entry.ImageUserFrameIndex[imgIdx] = -1;
                        entry.ImageLastUsedFrame[imgIdx] = 0;
                    }

                    MainSwapchain ??= swapchain;
                    _activeSwapchainCount = Math.Max(_activeSwapchainCount, i + 1);
                    return;
                }
            }

            throw new InvalidOperationException("Maximum swapchain count reached");
        }

        private VkSemaphore AllocateSemaphoreFromPool()
        {
            if (_availableSemaphores.Count > 0)
            {
                return _availableSemaphores.Dequeue();
            }

            // Expand pool if needed
            Debug.WriteLine("Semaphore pool expanded - consider increasing SEMAPHORE_POOL_SIZE");
            for (int i = 0; i < 4; i++)
            {
                _availableSemaphores.Enqueue(VkSemaphore.Create(_context));
            }

            return _availableSemaphores.Dequeue();
        }

        private void ReturnSemaphoreToPool(VkSemaphore semaphore)
        {
            if (semaphore != null && !semaphore.IsDisposed)
            {
                _availableSemaphores.Enqueue(semaphore);
            }
        }

        public UploadBatch BeginFrame()
        {
            if (_disposed) ThrowDisposed();

            var frame = _frames[_currentFrameIndex];
            frame.FrameNumber = Interlocked.Increment(ref _frameNumber);

            // Wait for previous frame's flush operation to complete
            frame.FlushOperation?.Wait();

            // Return semaphores from previous frame to pool
            foreach (var semaphore in frame.SemaphoresToReturn)
            {
                ReturnSemaphoreToPool(semaphore);
            }
            frame.SemaphoresToReturn.Clear();

            frame.InFlightFence.Reset();
            frame.Reset();

            frame.AcquiredSwapchainCount = 0;
            bool anySwapchainInvalid = false;

            // Acquire images from all swapchains
            for (int i = 0; i < _activeSwapchainCount; i++)
            {
                ref var entry = ref _swapchains[i];
                if (entry.Swapchain == null) continue;

                if (entry.NeedsRecreation)
                {
                    anySwapchainInvalid = true;
                    continue;
                }

                try
                {
                    // Check if the frame's acquire semaphore is safe to use
                    if (entry.ImageAvailableInUse[_currentFrameIndex])
                    {
                        // Wait for the frame that was using this semaphore
                        // This should be the current frame from a previous use that wasn't cleared
                        _frames[_currentFrameIndex].FlushOperation?.Wait();
                        entry.ImageAvailableInUse[_currentFrameIndex] = false;
                    }

                    var semaphore = entry.ImageAvailableSemaphores[_currentFrameIndex];
                    uint imageIndex = 0;

                    // Reset the frame's acquired image index
                    entry.FrameAcquiredImageIndex[_currentFrameIndex] = uint.MaxValue;

                    var result = entry.Swapchain.AcquireNextImage(semaphore, out imageIndex);

                    if (result == Result.ErrorOutOfDateKhr || result == Result.SuboptimalKhr)
                    {
                        entry.NeedsRecreation = true;
                        anySwapchainInvalid = true;
                    }
                    else if (result == Result.Success)
                    {
                        // Mark frame's acquire semaphore as in use
                        entry.ImageAvailableInUse[_currentFrameIndex] = true;
                        entry.FrameAcquiredImageIndex[_currentFrameIndex] = imageIndex;

                        // Check if the image's render semaphore is safe to use
                        if (entry.RenderCompleteInUse[imageIndex])
                        {
                            // Wait for the frame that was using this image
                            int userFrame = entry.ImageUserFrameIndex[imageIndex];
                            if (userFrame >= 0 && userFrame != _currentFrameIndex)
                            {
                                _frames[userFrame].FlushOperation?.Wait();
                                entry.RenderCompleteInUse[imageIndex] = false;
                            }
                        }

                        entry.ImageUserFrameIndex[imageIndex] = _currentFrameIndex;
                        entry.ImageLastUsedFrame[imageIndex] = frame.FrameNumber;

                        // Store acquired indices
                        frame.AcquiredSwapchainIndices[frame.AcquiredSwapchainCount] = i;
                        frame.AcquiredImageIndices[frame.AcquiredSwapchainCount] = imageIndex;
                        frame.AcquiredSwapchainCount++;
                    }
                    else
                    {
                        Debug.WriteLine($"AcquireNextImage failed with result: {result}");
                        entry.NeedsRecreation = true;
                        anySwapchainInvalid = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to acquire image for swapchain {i}: {ex.Message}");
                    entry.NeedsRecreation = true;
                    anySwapchainInvalid = true;
                }
            }

            // Handle swapchain recreation if needed
            if (anySwapchainInvalid)
            {
                RecreateInvalidSwapchains();
            }

            // Create upload batch
            frame.CurrentBatch = _context.GraphicsSubmitContext.CreateBatch();

            // Add wait semaphores for all acquired swapchains
            for (int i = 0; i < frame.AcquiredSwapchainCount; i++)
            {
                int swapchainIdx = frame.AcquiredSwapchainIndices[i];
                uint imageIdx = frame.AcquiredImageIndices[i];

                var semaphore = _swapchains[swapchainIdx].ImageAvailableSemaphores[_currentFrameIndex];

                if (semaphore != null && !semaphore.IsDisposed)
                {
                    frame.CurrentBatch.AddWaitSemaphore(
                        semaphore,
                        PipelineStageFlags.ColorAttachmentOutputBit
                    );
                }
            }

            return frame.CurrentBatch;
        }

        public bool SubmitAndPresent()
        {
            if (_disposed) return false;

            var frame = _frames[_currentFrameIndex];

            if (frame.CurrentBatch == null || frame.AcquiredSwapchainCount == 0)
                return false;

            // Add signal semaphores for rendering completion
            for (int i = 0; i < frame.AcquiredSwapchainCount; i++)
            {
                int swapchainIdx = frame.AcquiredSwapchainIndices[i];
                uint imageIdx = frame.AcquiredImageIndices[i];

                var semaphore = _swapchains[swapchainIdx].RenderCompleteSemaphores[imageIdx];

                // The semaphore should already be checked for safety in BeginFrame
                if (semaphore != null && !semaphore.IsDisposed)
                {
                    frame.CurrentBatch.AddSignalSemaphore(semaphore);
                }
            }

            // Submit the batch
            frame.CurrentBatch.Submit();

            // Flush with fence
            frame.FlushOperation = _context.GraphicsSubmitContext.Flush(frame.InFlightFence);

            // Present all acquired swapchains
            bool presentSuccess = PresentFrame(frame);

            // Move to next frame
            _currentFrameIndex = (_currentFrameIndex + 1) % _frameCount;

            return presentSuccess;
        }

        private unsafe bool PresentFrame(FrameState frame)
        {
            if (frame.AcquiredSwapchainCount == 0) return false;

            // Prepare arrays for presentation
            for (int i = 0; i < frame.AcquiredSwapchainCount; i++)
            {
                int swapchainIdx = frame.AcquiredSwapchainIndices[i];
                uint imageIdx = frame.AcquiredImageIndices[i];

                ref var entry = ref _swapchains[swapchainIdx];

                _presentSwapchains[i] = entry.Swapchain.VkObjectNative;
                _presentImageIndices[i] = imageIdx;

                var semaphore = entry.RenderCompleteSemaphores[imageIdx];
                if (semaphore == null || semaphore.IsDisposed)
                {
                    Debug.WriteLine($"Presentation semaphore is null or disposed for swapchain {swapchainIdx}, image {imageIdx}");
                    _presentWaitSemaphores[i] = default;
                }
                else
                {
                    _presentWaitSemaphores[i] = semaphore.VkObjectNative;
                }
            }

            fixed (SwapchainKHR* pSwapchains = _presentSwapchains)
            fixed (uint* pImageIndices = _presentImageIndices)
            fixed (Semaphore* pWaitSemaphores = _presentWaitSemaphores)
            {
                var presentInfo = new PresentInfoKHR
                {
                    SType = StructureType.PresentInfoKhr,
                    WaitSemaphoreCount = (uint)frame.AcquiredSwapchainCount,
                    PWaitSemaphores = pWaitSemaphores,
                    SwapchainCount = (uint)frame.AcquiredSwapchainCount,
                    PSwapchains = pSwapchains,
                    PImageIndices = pImageIndices,
                    PResults = null
                };

                var result = _swapchainApi.QueuePresent(_context.Device.PresentQueue, in presentInfo);

                // Handle presentation errors
                if (result == Result.ErrorOutOfDateKhr || result == Result.SuboptimalKhr)
                {
                    for (int i = 0; i < frame.AcquiredSwapchainCount; i++)
                    {
                        int swapchainIdx = frame.AcquiredSwapchainIndices[i];
                        //_swapchains[swapchainIdx].NeedsRecreation = true;
                    }
                }

                return result == Result.Success;
            }
        }

        private void RecreateInvalidSwapchains()
        {
            _context.Device.WaitIdle();

            for (int i = 0; i < _activeSwapchainCount; i++)
            {
                ref var entry = ref _swapchains[i];
                if (!entry.NeedsRecreation || entry.Swapchain == null) continue;

                try
                {
                    // Return semaphores to pool before recreating
                    if (entry.ImageAvailableSemaphores != null)
                    {
                        foreach (var semaphore in entry.ImageAvailableSemaphores)
                        {
                            ReturnSemaphoreToPool(semaphore);
                        }
                    }

                    if (entry.RenderCompleteSemaphores != null)
                    {
                        foreach (var semaphore in entry.RenderCompleteSemaphores)
                        {
                            ReturnSemaphoreToPool(semaphore);
                        }
                    }

                    // Recreate swapchain
                    entry.Swapchain.RecreateSwapchain();
                    entry.NeedsRecreation = false;

                    // Get new image count
                    int imageCount = (int)entry.Swapchain.SwapChainImagesCount;

                    // Reallocate arrays
                    entry.ImageAvailableSemaphores = new VkSemaphore[_frameCount];
                    entry.ImageAvailableInUse = new bool[_frameCount];
                    entry.FrameAcquiredImageIndex = new uint[_frameCount];
                    entry.RenderCompleteSemaphores = new VkSemaphore[imageCount];
                    entry.RenderCompleteInUse = new bool[imageCount];
                    entry.ImageUserFrameIndex = new int[imageCount];
                    entry.ImageLastUsedFrame = new ulong[imageCount];

                    // Allocate new semaphores from pool
                    for (int frameIdx = 0; frameIdx < _frameCount; frameIdx++)
                    {
                        entry.ImageAvailableSemaphores[frameIdx] = AllocateSemaphoreFromPool();
                        entry.ImageAvailableInUse[frameIdx] = false;
                        entry.FrameAcquiredImageIndex[frameIdx] = uint.MaxValue;
                    }

                    for (int imgIdx = 0; imgIdx < imageCount; imgIdx++)
                    {
                        entry.RenderCompleteSemaphores[imgIdx] = AllocateSemaphoreFromPool();
                        entry.RenderCompleteInUse[imgIdx] = false;
                        entry.ImageUserFrameIndex[imgIdx] = -1;
                        entry.ImageLastUsedFrame[imgIdx] = 0;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to recreate swapchain {i}: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Wait for all frames to complete
            for (int i = 0; i < _frameCount; i++)
            {
                _frames[i].FlushOperation?.Wait();
                _frames[i].Dispose();
            }

            // Cleanup swapchains
            for (int i = 0; i < _activeSwapchainCount; i++)
            {
                ref var entry = ref _swapchains[i];

                entry.Swapchain?.Dispose();

                // Return semaphores to pool
                if (entry.ImageAvailableSemaphores != null)
                {
                    foreach (var semaphore in entry.ImageAvailableSemaphores)
                    {
                        ReturnSemaphoreToPool(semaphore);
                    }
                }

                if (entry.RenderCompleteSemaphores != null)
                {
                    foreach (var semaphore in entry.RenderCompleteSemaphores)
                    {
                        ReturnSemaphoreToPool(semaphore);
                    }
                }
            }

            // Dispose all semaphores in the pool
            while (_availableSemaphores.Count > 0)
            {
                var semaphore = _availableSemaphores.Dequeue();
                semaphore?.Dispose();
            }

            GC.SuppressFinalize(this);
        }

        private static void ThrowDisposed() =>
            throw new ObjectDisposedException(nameof(GraphicsContext));

        public void RemoveSwapchain(VkSwapchain swapchain)
        {
            if (_disposed) return;

            for (int i = 0; i < _activeSwapchainCount; i++)
            {
                if (_swapchains[i].Swapchain == swapchain)
                {
                    ref var entry = ref _swapchains[i];

                    // Mark for removal
                    entry.Swapchain = null;
                    entry.NeedsRecreation = false;
                    _frames[_currentFrameIndex].Resources.Add(swapchain);

                    if (MainSwapchain == swapchain)
                    {
                        // Find new main swapchain
                        for (int j = 0; j < _activeSwapchainCount; j++)
                        {
                            if (_swapchains[j].Swapchain != null)
                            {
                                MainSwapchain = _swapchains[j].Swapchain;
                                _swapchains[j].IsMain = true;
                                break;
                            }
                        }
                    }

                    // Return semaphores to pool
                    if (entry.ImageAvailableSemaphores != null)
                    {
                        foreach (var semaphore in entry.ImageAvailableSemaphores)
                        {
                            ReturnSemaphoreToPool(semaphore);
                        }
                    }

                    if (entry.RenderCompleteSemaphores != null)
                    {
                        foreach (var semaphore in entry.RenderCompleteSemaphores)
                        {
                            ReturnSemaphoreToPool(semaphore);
                        }
                    }

                    break;
                }
            }
        }
    }
}