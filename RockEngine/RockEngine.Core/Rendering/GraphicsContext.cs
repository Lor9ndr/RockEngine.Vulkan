using RockEngine.Vulkan;

using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

using System.Diagnostics;

using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RockEngine.Core.Rendering
{
    /// <summary>
    /// High-performance graphics context with proper semaphore synchronization
    /// </summary>
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

        public VkSwapchain MainSwapchain { get; private set; }
        public uint FrameIndex => (uint)_currentFrameIndex;
        public int MaxFramesInFlight => _frameCount;

        private struct SwapchainEntry
        {
            public VkSwapchain Swapchain;
            public bool NeedsRecreation;
            public bool IsMain;
            public VkSemaphore[] ImageAvailableSemaphores; // Per-frame
            public VkSemaphore[] RenderCompleteSemaphores; // Per-frame
            public bool[] ImageAvailableInUse; // Track if semaphore is in use
            public bool[] RenderCompleteInUse; // Track if semaphore is in use
        }

        private sealed class FrameState : IDisposable
        {
            public VkFence InFlightFence;
            public UploadBatch CurrentBatch;
            public ulong FrameNumber;
            public int AcquiredSwapchainCount;
            public readonly int[] AcquiredSwapchainIndices = new int[16];
            public readonly List<IDisposable> Resources = new(32);
            public FlushOperation FlushOperation;

            public void Reset()
            {
                // Cleanup resources (but NOT semaphores - they're managed separately)
                // Only dispose resources that are not semaphores
                for (int i = Resources.Count - 1; i >= 0; i--)
                {
                    var resource = Resources[i];
                    if (resource is not VkSemaphore)
                    {
                        VulkanContext.GetCurrent().GraphicsSubmitContext.AddDependency(resource);
                        //resource?.Dispose();
                        Resources.RemoveAt(i);
                    }
                }

                AcquiredSwapchainCount = 0;
                CurrentBatch = null;
                FlushOperation = null;
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

            // Allocate arrays with fixed maximum sizes
            _swapchains = new SwapchainEntry[32]; // Maximum 32 swapchains
            _frames = new FrameState[_frameCount];

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

                    // Create per-frame semaphores
                    entry.ImageAvailableSemaphores = new VkSemaphore[_frameCount];
                    entry.RenderCompleteSemaphores = new VkSemaphore[_frameCount];
                    entry.ImageAvailableInUse = new bool[_frameCount];
                    entry.RenderCompleteInUse = new bool[_frameCount];

                    for (int frameIdx = 0; frameIdx < _frameCount; frameIdx++)
                    {
                        entry.ImageAvailableSemaphores[frameIdx] = VkSemaphore.Create(_context);
                        entry.RenderCompleteSemaphores[frameIdx] = VkSemaphore.Create(_context);
                        entry.ImageAvailableInUse[frameIdx] = false;
                        entry.RenderCompleteInUse[frameIdx] = false;
                    }

                    MainSwapchain ??= swapchain;

                    _activeSwapchainCount = Math.Max(_activeSwapchainCount, i + 1);
                    return;
                }
            }

            throw new InvalidOperationException("Maximum swapchain count reached");
        }
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

                    // Cleanup semaphores
                    for (int frameIdx = 0; frameIdx < _frameCount; frameIdx++)
                    {
                        _frames[_currentFrameIndex].Resources.Add(entry.ImageAvailableSemaphores[frameIdx]);
                        _frames[_currentFrameIndex].Resources.Add(entry.RenderCompleteSemaphores[frameIdx]);
                        entry.ImageAvailableInUse[frameIdx] = false;
                        entry.RenderCompleteInUse[frameIdx] = false;
                    }

                    break;
                }
            }
        }

        public UploadBatch BeginFrame()
        {
            if (_disposed) ThrowDisposed();

            var frame = _frames[_currentFrameIndex];
            frame.FrameNumber = Interlocked.Increment(ref _frameNumber);

            // Wait for previous frame's flush operation to complete
            // This ensures all GPU operations for this frame are done
            frame.FlushOperation?.Wait();

            // Reset the fence for this frame
            frame.InFlightFence.Reset();

            // Reset frame state
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
                    // Wait for the semaphore to become available
                    // We need to ensure the semaphore is not in use
                    WaitForSemaphoreAvailable(ref entry, _currentFrameIndex);

                    var semaphore = entry.ImageAvailableSemaphores[_currentFrameIndex];

                    if (semaphore == null || semaphore.IsDisposed)
                    {
                        Debug.WriteLine($"Semaphore is null or disposed for swapchain {i}, frame {_currentFrameIndex}");
                        entry.NeedsRecreation = true;
                        anySwapchainInvalid = true;
                        continue;
                    }

                    // Mark semaphore as in use
                    entry.ImageAvailableInUse[_currentFrameIndex] = true;

                    var result = entry.Swapchain.AcquireNextImage(semaphore);

                    if (result == Result.ErrorOutOfDateKhr || result == Result.SuboptimalKhr)
                    {
                        entry.ImageAvailableInUse[_currentFrameIndex] = false;
                        entry.NeedsRecreation = true;
                        anySwapchainInvalid = true;
                    }
                    else if (result == Result.Success)
                    {
                        frame.AcquiredSwapchainIndices[frame.AcquiredSwapchainCount] = i;
                        frame.AcquiredSwapchainCount++;
                    }
                    else
                    {
                        entry.ImageAvailableInUse[_currentFrameIndex] = false;
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
                var semaphore = _swapchains[swapchainIdx].ImageAvailableSemaphores[_currentFrameIndex];

                if (semaphore == null || semaphore.IsDisposed)
                {
                    Debug.WriteLine($"Wait semaphore is null or disposed for swapchain {swapchainIdx}");
                    continue;
                }

                frame.CurrentBatch.AddWaitSemaphore(
                    semaphore,
                    PipelineStageFlags.ColorAttachmentOutputBit
                );
            }

            return frame.CurrentBatch;
        }

        private void WaitForSemaphoreAvailable(ref SwapchainEntry entry, int frameIndex)
        {
            // If the semaphore is marked as in use, wait for the fence of that frame
            // This ensures previous operations using this semaphore are complete
            if (entry.ImageAvailableInUse[frameIndex] || entry.RenderCompleteInUse[frameIndex])
            {
                // Wait for the frame's fence to signal
                _frames[frameIndex].FlushOperation?.Wait();

                // Reset the in-use flags
                entry.ImageAvailableInUse[frameIndex] = false;
                entry.RenderCompleteInUse[frameIndex] = false;
            }
        }

        public bool SubmitAndPresent()
        {
            if (_disposed) return false;

            var frame = _frames[_currentFrameIndex];

            if (frame.CurrentBatch == null || frame.AcquiredSwapchainCount == 0)
                return false;

            // Mark render complete semaphores as in use
            for (int i = 0; i < frame.AcquiredSwapchainCount; i++)
            {
                int swapchainIdx = frame.AcquiredSwapchainIndices[i];
                _swapchains[swapchainIdx].RenderCompleteInUse[_currentFrameIndex] = true;
            }

            // Add signal semaphores for rendering completion
            for (int i = 0; i < frame.AcquiredSwapchainCount; i++)
            {
                int swapchainIdx = frame.AcquiredSwapchainIndices[i];
                var semaphore = _swapchains[swapchainIdx].RenderCompleteSemaphores[_currentFrameIndex];

                if (semaphore == null || semaphore.IsDisposed)
                {
                    Debug.WriteLine($"Signal semaphore is null or disposed for swapchain {swapchainIdx}");
                    continue;
                }

                frame.CurrentBatch.AddSignalSemaphore(semaphore);
            }

            // Submit the batch
            frame.CurrentBatch.Submit();

            // Flush with fence - FlushOperation will handle waiting for the fence
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
                ref var entry = ref _swapchains[swapchainIdx];
              
                _presentSwapchains[i] = entry.Swapchain.VkObjectNative;
                _presentImageIndices[i] = entry.Swapchain.CurrentImageIndex;

                var semaphore = entry.RenderCompleteSemaphores[_currentFrameIndex];
                if (semaphore == null || semaphore.IsDisposed)
                {
                    Debug.WriteLine($"Presentation semaphore is null or disposed for swapchain {swapchainIdx}");
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
                        _swapchains[swapchainIdx].NeedsRecreation = true;
                    }
                }

                return result == Result.Success;
            }
        }

        private void RecreateInvalidSwapchains()
        {
            // Wait for device to be idle before recreating swapchains
            _context.Device.WaitIdle();

            for (int i = 0; i < _activeSwapchainCount; i++)
            {
                ref var entry = ref _swapchains[i];
                if (!entry.NeedsRecreation || entry.Swapchain == null) continue;

                try
                {
                    // Dispose old semaphores
                    for (int frameIdx = 0; frameIdx < _frameCount; frameIdx++)
                    {
                        entry.ImageAvailableSemaphores[frameIdx]?.Dispose();
                        entry.RenderCompleteSemaphores[frameIdx]?.Dispose();
                        entry.ImageAvailableInUse[frameIdx] = false;
                        entry.RenderCompleteInUse[frameIdx] = false;
                    }

                    // Recreate swapchain
                    entry.Swapchain.RecreateSwapchain();
                    entry.NeedsRecreation = false;

                    // Recreate semaphores
                    for (int frameIdx = 0; frameIdx < _frameCount; frameIdx++)
                    {
                        entry.ImageAvailableSemaphores[frameIdx] = VkSemaphore.Create(_context);
                        entry.RenderCompleteSemaphores[frameIdx] = VkSemaphore.Create(_context);
                        entry.ImageAvailableInUse[frameIdx] = false;
                        entry.RenderCompleteInUse[frameIdx] = false;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to recreate swapchain {i}: {ex.Message}");
                    // Keep marked for retry
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

            // Cleanup swapchains and their semaphores
            for (int i = 0; i < _activeSwapchainCount; i++)
            {
                ref var entry = ref _swapchains[i];

                entry.Swapchain?.Dispose();

                for (int frameIdx = 0; frameIdx < _frameCount; frameIdx++)
                {
                    entry.ImageAvailableSemaphores?[frameIdx]?.Dispose();
                    entry.RenderCompleteSemaphores?[frameIdx]?.Dispose();
                }
            }

            GC.SuppressFinalize(this);
        }

        private static void ThrowDisposed() =>
            throw new ObjectDisposedException(nameof(GraphicsContext));

        // Performance monitoring
        public struct FrameStats
        {
            public ulong FrameNumber;
            public int AcquiredSwapchains;
            public TimeSpan AcquireTime;
            public TimeSpan PresentTime;
        }

        public FrameStats GetFrameStats() => new FrameStats
        {
            FrameNumber = Interlocked.Read(ref _frameNumber),
            AcquiredSwapchains = _frames[_currentFrameIndex].AcquiredSwapchainCount
        };

        // Helper method for getting semaphores (for compatibility)
        private VkSemaphore GetImageAvailableSemaphore(int frameIndex, int swapchainIndex)
        {
            return _swapchains[swapchainIndex].ImageAvailableSemaphores[frameIndex];
        }

        private VkSemaphore GetRenderCompleteSemaphore(int frameIndex, int swapchainIndex)
        {
            return _swapchains[swapchainIndex].RenderCompleteSemaphores[frameIndex];
        }
    }
}