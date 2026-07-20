using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;
namespace RockEngine.Core.Rendering
{
    public static class Consts
    {
        public const uint NOT_ACQUIRED_IMAGE = uint.MaxValue;
    }

    public sealed class GraphicsContext : IDisposable
    {
        private readonly VulkanContext _vkContext;
        private readonly uint _desiredMaxFramesInFlight;
        private uint _maxFramesInFlight;
        private readonly List<VkSwapchain> _swapchains = new();
        private readonly Dictionary<VkSwapchain, SwapchainSyncData> _syncMap = new();

        private uint _currentFlightIndex;
        private UploadBatch? _currentFrameBatch;
        private SubmitOperation?[] _flightOperations;   // per flight slot
        private bool _disposed;

        public uint FrameIndex => _currentFlightIndex;
        public VkSwapchain? MainSwapchain { get; private set; }

        // Per‑swapchain sync objects (same as before)
        private sealed class SwapchainSyncData
        {
            private readonly VulkanContext _context;
            public VkSemaphore[] ImageAvailableSemaphores;
            public VkSemaphore[] RenderFinishedSemaphores;   // per image
            public uint[] AcquiredImageIndices;

            public SwapchainSyncData(uint maxFramesInFlight, uint imageCount, VulkanContext context)
            {
                ImageAvailableSemaphores = new VkSemaphore[maxFramesInFlight];
                RenderFinishedSemaphores = new VkSemaphore[imageCount];
                AcquiredImageIndices = new uint[maxFramesInFlight];

                for (int i = 0; i < maxFramesInFlight; i++)
                {
                    ImageAvailableSemaphores[i] = VkSemaphore.Create(context);
                    AcquiredImageIndices[i] = Consts.NOT_ACQUIRED_IMAGE;
                }
                for (int i = 0; i < imageCount; i++)
                {
                    RenderFinishedSemaphores[i] = VkSemaphore.Create(context);
                }

                _context = context;
            }

            public void Dispose()
            {
                foreach (var s in ImageAvailableSemaphores)
                {
                    if(s is not null)
                    {
                        _context.GraphicsSubmitContext.AddDependency(s);
                    }
                }

                foreach (var s in RenderFinishedSemaphores)
                {
                    if (s is not null)
                    {
                        _context.GraphicsSubmitContext.AddDependency(s);
                    }
                }
            }

            public void ReinitializeRenderFinishedSemaphores(uint newImageCount)
            {
                foreach (var s in RenderFinishedSemaphores)
                {
                    if (s is not null)
                    {
                        _context.GraphicsSubmitContext.AddDependency(s);
                    }
                }

                RenderFinishedSemaphores = new VkSemaphore[newImageCount];
                for (int i = 0; i < newImageCount; i++)
                {
                    RenderFinishedSemaphores[i] = VkSemaphore.Create(_context);
                }
            }
        }

        public GraphicsContext(VulkanContext vkContext)
        {
            _vkContext = vkContext ?? throw new ArgumentNullException(nameof(vkContext));
            _desiredMaxFramesInFlight = _vkContext.MaxFramesPerFlight;
            _maxFramesInFlight = _vkContext.MaxFramesPerFlight;   // will be refined when swapchains are added
            _flightOperations = new SubmitOperation[_maxFramesInFlight];
        }

        // ---------- Swapchain registration ----------
        public void AddSwapchain(VkSwapchain swapchain)
        {
            ThrowDisposedIfNeeded();
            if (_syncMap.ContainsKey(swapchain))
            {
                return;
            }

            MainSwapchain ??= swapchain;

            // Ensure maxFramesInFlight is less than the number of swapchain images
            uint imageCount = (uint)swapchain.SwapChainImagesCount;
            uint allowedMax = imageCount > 1 ? imageCount - 1 : 1;
            uint newMax = Math.Min(_desiredMaxFramesInFlight, allowedMax);
            if (newMax != _maxFramesInFlight)
            {
                _maxFramesInFlight = newMax;
                // Reinitialise per‑flight array (discarding old operations after waiting)
                foreach (var op in _flightOperations)
                {
                    op?.Wait();   // block until GPU finishes
                }

                _flightOperations = new SubmitOperation[_maxFramesInFlight];
                // Recreate sync data for all existing swapchains with the new max
                foreach (var kvp in _syncMap)
                {
                    kvp.Value.Dispose();
                    var sync = new SwapchainSyncData(_maxFramesInFlight, (uint)kvp.Key.SwapChainImagesCount, _vkContext);
                    _syncMap[kvp.Key] = sync;
                }
            }

            var newSync = new SwapchainSyncData(_maxFramesInFlight, imageCount, _vkContext);
            _syncMap[swapchain] = newSync;
            _swapchains.Add(swapchain);
        }

        public void RemoveSwapchain(VkSwapchain swapchain)
        {
            if (_syncMap.TryGetValue(swapchain, out var sync))
            {
                sync.Dispose();
                _syncMap.Remove(swapchain);
                _swapchains.Remove(swapchain);
                if (MainSwapchain == swapchain)
                {
                    MainSwapchain = _syncMap.FirstOrDefault().Key;
                }
            }
        }

        // ---------- Per‑flight image acquisition ----------
        public uint GetAcquiredImageIndex(VkSwapchain swapchain, uint flightIndex)
        {
            ThrowDisposedIfNeeded();
            if (!_syncMap.TryGetValue(swapchain, out var sync))
            {
                throw new InvalidOperationException("Swapchain not registered.");
            }

            flightIndex %= _maxFramesInFlight;
            if (sync.AcquiredImageIndices[flightIndex] == Consts.NOT_ACQUIRED_IMAGE)
            {
                AcquireImageForFlight(swapchain, sync, flightIndex);
            }

            return sync.AcquiredImageIndices[flightIndex];
        }

        public VkSemaphore GetImageAvailableSemaphore(VkSwapchain swapchain, uint flightIndex)
        {
            ThrowDisposedIfNeeded();
            flightIndex %= _maxFramesInFlight;
            return _syncMap[swapchain].ImageAvailableSemaphores[flightIndex];
        }

        public VkSemaphore GetRenderFinishedSemaphore(VkSwapchain swapchain, uint flightIndex)
        {
            ThrowDisposedIfNeeded();
            GetAcquiredImageIndex(swapchain, flightIndex);
            uint imageIndex = _syncMap[swapchain].AcquiredImageIndices[flightIndex];
            return _syncMap[swapchain].RenderFinishedSemaphores[imageIndex];
        }

        // ---------- Frame lifecycle (optimised non‑blocking) ----------
        public UploadBatch? BeginFrame()
        {
            ThrowDisposedIfNeeded();

            _currentFlightIndex = (_currentFlightIndex + 1) % _maxFramesInFlight;

            // Wait for the previous submission on this slot to finish (GPU done + cleanup)
            var previousOp = _flightOperations[_currentFlightIndex];
            previousOp?.Wait();           // ensures GPU is idle, batches recycled, semaphores reusable
            previousOp?.Dispose();
            _flightOperations[_currentFlightIndex] = null;

            // Acquire new images for all swapchains
            foreach (var swapchain in _swapchains)
            {
                var sync = _syncMap[swapchain];
                AcquireImageForFlight(swapchain, sync, _currentFlightIndex);
            }

            // Create a batch that waits on all image‑available semaphores and signals per‑image render‑finished
            var batch = _vkContext.GraphicsSubmitContext.CreateBatch();
            foreach (var swapchain in _swapchains)
            {
                var sync = _syncMap[swapchain];
                uint imageIndex = sync.AcquiredImageIndices[_currentFlightIndex];
                batch.AddWaitSemaphore(sync.ImageAvailableSemaphores[_currentFlightIndex],
                    PipelineStageFlags.ColorAttachmentOutputBit);
                batch.AddSignalSemaphore(sync.RenderFinishedSemaphores[imageIndex]);
            }

            _currentFrameBatch = batch;
            return batch;
        }

        public bool SubmitAndPresent()
        {
            ThrowDisposedIfNeeded();
            if (_currentFrameBatch == null)
            {
                return false;
            }

            // Transition images for presentation
            foreach (var swapchain in _swapchains)
            {
                TransitionSwapchainToPresent(swapchain);
            }

            _currentFrameBatch.Submit();

            // Submit all accumulated batches to the GPU – get a non‑blocking operation
            var submitOp = _vkContext.GraphicsSubmitContext.Submit();
            _flightOperations[_currentFlightIndex] = submitOp;   // will be waited on next time this slot is used

            // Present immediately; the present engine will wait on the render‑finished semaphore
            bool allPresentSuccessful = true;
            foreach (var swapchain in _swapchains)
            {
                var sync = _syncMap[swapchain];
                uint imageIndex = sync.AcquiredImageIndices[_currentFlightIndex];
                var presentResult = PresentSwapchain(swapchain, imageIndex,
                    sync.RenderFinishedSemaphores[imageIndex]);

                if (presentResult != Result.Success)
                {
                    allPresentSuccessful = false;
                    if (presentResult == Result.ErrorOutOfDateKhr || presentResult == Result.SuboptimalKhr)
                    {
                        RecreateSwapchainOnDemand(swapchain);
                    }
                    else if (presentResult < 0)
                    {
                        Debug.Fail($"Present failed: {presentResult}");
                    }
                }
            }

            // Reset acquired indices for the next frame
            foreach (var sync in _syncMap.Values)
            {
                sync.AcquiredImageIndices[_currentFlightIndex] = Consts.NOT_ACQUIRED_IMAGE;
            }

            _currentFrameBatch = null;
            return allPresentSuccessful;
        }

        public void TransitionSwapchainToPresent(VkSwapchain swapchain)
        {
            if (_currentFrameBatch == null)
            {
                return;
            }

            var sync = _syncMap[swapchain];
            uint imageIndex = sync.AcquiredImageIndices[_currentFlightIndex];
            var image = swapchain.VkImages[imageIndex];

            // (Ideally track actual layout; using Undefined is safe only if image hasn't been written yet)
            image.TransitionImageLayout(_currentFrameBatch,
                ImageLayout.Undefined, ImageLayout.PresentSrcKhr);
        }

        // ---------- Helpers (unchanged except for fence removal) ----------
        private void AcquireImageForFlight(VkSwapchain swapchain, SwapchainSyncData sync, uint flightIndex)
        {
            // Uses the image‑available semaphore (no fence needed)
            var result = swapchain.AcquireNextImage(sync.ImageAvailableSemaphores[flightIndex],
                null, out uint imageIndex);

            if (result == Result.Success || result == Result.SuboptimalKhr)
            {
                sync.AcquiredImageIndices[flightIndex] = imageIndex;
            }
            else if (result == Result.ErrorOutOfDateKhr)
            {
                RecreateSwapchainOnDemand(swapchain);
                result = swapchain.AcquireNextImage(sync.ImageAvailableSemaphores[flightIndex],
                    null, out imageIndex);
                if (result == Result.Success || result == Result.SuboptimalKhr)
                {
                    sync.AcquiredImageIndices[flightIndex] = imageIndex;
                }
                else
                {
                    throw new VulkanException(result, "Acquire image after recreation failed");
                }
            }
            else
            {
                throw new VulkanException(result, "AcquireNextImage failed");
            }
        }

        private unsafe Result PresentSwapchain(VkSwapchain swapchain, uint imageIndex, VkSemaphore waitSemaphore)
        {
            var semaphore = waitSemaphore.VkObjectNative;
            var sc = swapchain.VkObjectNative;
            var index = imageIndex;

            var presentInfo = new PresentInfoKHR
            {
                SType = StructureType.PresentInfoKhr,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = &semaphore,
                SwapchainCount = 1,
                PSwapchains = &sc,
                PImageIndices = &index,
                PResults = null
            };
            return swapchain.SwapchainApi.QueuePresent(
                _vkContext.Device.PresentQueue!.VkObjectNative, in presentInfo);
        }

        private void RecreateSwapchainOnDemand(VkSwapchain swapchain)
        {
            if (swapchain.RecreateSwapchain() && _syncMap.TryGetValue(swapchain, out var sync))
            {
                sync.ReinitializeRenderFinishedSemaphores((uint)swapchain.SwapChainImagesCount);
            }
            else
            {
                Debug.WriteLine("Swapchain recreation failed");
            }
        }

        private void ThrowDisposedIfNeeded()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Wait for all pending flight operations and dispose them
            foreach (var op in _flightOperations)
            {
                op?.Wait();
                op?.Dispose();
            }

            foreach (var sync in _syncMap.Values)
            {
                sync.Dispose();
            }

            _syncMap.Clear();
            _swapchains.Clear();
            _currentFrameBatch = null;
        }
    }
}
