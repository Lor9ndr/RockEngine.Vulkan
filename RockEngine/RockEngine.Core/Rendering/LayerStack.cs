using RockEngine.Vulkan;

using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace RockEngine.Core.Rendering
{
    public interface ILayer
    {
        Task OnAttach();
        void OnDetach();
        void OnUpdate();
        void OnRender(VkCommandBuffer vkCommandBuffer);
        Task OnImGuiRender(VkCommandBuffer vkCommandBuffer);
    }

    public class LayerStack : ILayerStack, IDisposable
    {
        private ILayer[] _activeLayers = Array.Empty<ILayer>();
        private int _activeLayerCount = 0;

        // Thread-safe queues for layer operations
        private ConcurrentQueue<ILayer> _layersToAdd = new ConcurrentQueue<ILayer>();
        private ConcurrentQueue<ILayer> _layersToRemove = new ConcurrentQueue<ILayer>();
        private ConcurrentQueue<Task> _pendingAttachmentTasks = new ConcurrentQueue<Task>();

        // Track layers that need to be removed after all updates
        private ConcurrentQueue<ILayer> _pendingRemovals = new ConcurrentQueue<ILayer>();
        private bool _shouldProcessRemovals = false;

        private readonly object _syncLock = new object();
        private bool _isProcessing = false;
        private bool _disposed = false;

        public int Count => _activeLayerCount;

        public async Task PushLayer(ILayer layer)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LayerStack));
            }

            if (_activeLayers.Contains(layer))
            {
                return;
            }
            // Start the attachment process asynchronously
            var attachTask = layer.OnAttach();

            // If we're currently processing layers, queue the addition
            if (_isProcessing)
            {
                _layersToAdd.Enqueue(layer);
                _pendingAttachmentTasks.Enqueue(attachTask);
            }
            else
            {
                // Wait for attachment to complete before adding to active layers
                await attachTask;
                AddLayerInternal(layer);
            }
        }

        public void PopLayer(ILayer layer)
        {
            if (_disposed)
            {
                return;
            }

            // Queue the removal for processing after all updates
            _pendingRemovals.Enqueue(layer);
            _shouldProcessRemovals = true;
        }

        public void Update()
        {
            if (_disposed)
            {
                return;
            }

            ProcessPendingOperations();

            // Use a stack-allocated span for iteration (no heap allocation)
            Span<ILayer> layersToUpdate = _activeLayers.AsSpan(0, _activeLayerCount);

            for (int i = 0; i < layersToUpdate.Length; i++)
            {
                layersToUpdate[i].OnUpdate();
            }

            // Process removals after all updates are complete
            ProcessRemovals();
        }
        public void Render(VkCommandBuffer vkCommandBuffer)
        {
            if (_disposed)
            {
                return;
            }

            ProcessPendingOperations();

            // Use a stack-allocated span for iteration (no heap allocation)
            Span<ILayer> layersToRender = _activeLayers.AsSpan(0, _activeLayerCount);

            for (int i = 0; i < layersToRender.Length; i++)
            {
                layersToRender[i].OnRender(vkCommandBuffer);
            }
        }

        public async Task RenderImGui(VkCommandBuffer commandBuffer)
        {
            if (_disposed)
            {
                return;
            }

            ProcessPendingOperations();

            // Use a stack-allocated span for iteration (no heap allocation)

            for (int i = 0; i < _activeLayerCount; i++)
            {
                await _activeLayers[i].OnImGuiRender(commandBuffer);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddLayerInternal(ILayer layer)
        {
            lock (_syncLock)
            {
                // Resize array if needed (minimal allocation)
                if (_activeLayerCount >= _activeLayers.Length)
                {
                    int newSize = Math.Max(4, _activeLayers.Length * 2);
                    var newArray = ArrayPool<ILayer>.Shared.Rent(newSize);

                    if (_activeLayerCount > 0)
                    {
                        Array.Copy(_activeLayers, newArray, _activeLayerCount);
                    }

                    // Return old array to pool
                    if (_activeLayers.Length > 0)
                    {
                        ArrayPool<ILayer>.Shared.Return(_activeLayers);
                    }

                    _activeLayers = newArray;
                }

                _activeLayers[_activeLayerCount++] = layer;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveLayerInternal(ILayer layer)
        {
            lock (_syncLock)
            {
                for (int i = 0; i < _activeLayerCount; i++)
                {
                    if (ReferenceEquals(_activeLayers[i], layer))
                    {
                        // Shift elements to fill the gap
                        if (i < _activeLayerCount - 1)
                        {
                            Array.Copy(_activeLayers, i + 1, _activeLayers, i, _activeLayerCount - i - 1);
                        }

                        _activeLayers[--_activeLayerCount] = null;
                        layer.OnDetach();
                        break;
                    }
                }

                // Shrink array if it's too empty (optional optimization)
                if (_activeLayerCount > 0 && _activeLayers.Length > 16 && _activeLayerCount < _activeLayers.Length / 4)
                {
                    int newSize = _activeLayers.Length / 2;
                    var newArray = ArrayPool<ILayer>.Shared.Rent(newSize);
                    Array.Copy(_activeLayers, newArray, _activeLayerCount);

                    ArrayPool<ILayer>.Shared.Return(_activeLayers);
                    _activeLayers = newArray;
                }
            }
        }

        private void ProcessPendingOperations()
        {
            _isProcessing = true;

            // Process removals first
            while (_layersToRemove.TryDequeue(out var layerToRemove))
            {
                RemoveLayerInternal(layerToRemove);
            }

            // Process additions
            while (_layersToAdd.TryPeek(out var nextLayerToAdd) &&
                   _pendingAttachmentTasks.TryPeek(out var nextTask))
            {
                if (nextTask.IsCompleted)
                {
                    _layersToAdd.TryDequeue(out _);
                    _pendingAttachmentTasks.TryDequeue(out _);
                    AddLayerInternal(nextLayerToAdd);
                }
                else
                {
                    break;
                }
            }

            _isProcessing = false;
        }

        private void ProcessRemovals()
        {
            if (!_shouldProcessRemovals)
            {
                return;
            }

            _isProcessing = true;

            // Process all pending removals
            while (_pendingRemovals.TryDequeue(out var layerToRemove))
            {
                RemoveLayerInternal(layerToRemove);
            }

            _shouldProcessRemovals = false;
            _isProcessing = false;
        }

        // Optional: Method to wait for all pending attachments to complete
        public async Task WaitForPendingAttachments()
        {
            while (!_pendingAttachmentTasks.IsEmpty)
            {
                if (_pendingAttachmentTasks.TryPeek(out var task))
                {
                    await task;
                    _pendingAttachmentTasks.TryDequeue(out _);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Process any pending removals first
            ProcessRemovals();

            // Detach all layers
            for (int i = 0; i < _activeLayerCount; i++)
            {
                _activeLayers[i]?.OnDetach();
            }

            // Return array to pool
            if (_activeLayers.Length > 0)
            {
                ArrayPool<ILayer>.Shared.Return(_activeLayers);
            }

            _activeLayers = Array.Empty<ILayer>();
            _activeLayerCount = 0;

            // Clear queues
            while (_layersToAdd.TryDequeue(out _)) { }
            while (_layersToRemove.TryDequeue(out _)) { }
            while (_pendingAttachmentTasks.TryDequeue(out _)) { }
            while (_pendingRemovals.TryDequeue(out _)) { }
        }
    }
}