using RockEngine.Vulkan;

using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Texturing
{
    public class TextureStreamer : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly Renderer _renderer;
        private PriorityQueue<StreamRequest, float> _queue = new();
        private readonly List<Worker> _workers = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly MemoryBudgetTracker _memoryTracker;

        public TextureStreamer(VulkanContext context, Renderer renderer, ulong vramBudgetMB = 2048, int workerCount = 4)
        {
            _context = context;
            _renderer = renderer;
            _memoryTracker = new MemoryBudgetTracker(vramBudgetMB * 1024 * 1024);

            for (var i = 0; i < workerCount; i++)
                _workers.Add(new Worker(this));
        }
        public void EvictTexture(StreamableTexture texture)
        {
            lock (_queue)
            {
                _memoryTracker.Untrack(texture);
                texture.Dispose();
            }
        }

        public void RequestStream(StreamableTexture texture, uint targetMip, float priority)
        {
            lock (_queue)
            {
                _queue.Enqueue(new StreamRequest(texture, targetMip), priority);
                _memoryTracker.Track(texture);
            }
        }

        public void UpdatePriorities(IEnumerable<StreamPriority> priorities)
        {
            lock (_queue)
            {
                // Для обновления приоритетов потребуется пересоздать очередь
                var newQueue = new PriorityQueue<StreamRequest, float>();
                while (_queue.TryDequeue(out var item, out var oldPriority))
                {
                    foreach (var priority in priorities)
                    {
                        if (item.Texture == priority.Texture)
                        {
                            newQueue.Enqueue(item, priority.NewPriority);
                            break;
                        }
                    }
                }
                _queue = newQueue;
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            foreach (var worker in _workers) worker.Dispose();
            _memoryTracker.Dispose();
        }

        private class Worker : IDisposable
        {
            private readonly TextureStreamer _parent;
            private readonly Thread _thread;

            public Worker(TextureStreamer parent)
            {
                _parent = parent;
                _thread = new Thread(WorkLoop);
                _thread.Start();
            }

            private async void WorkLoop()
            {
                while (!_parent._cts.IsCancellationRequested)
                {
                    StreamRequest? request = null;
                    lock (_parent._queue)
                    {
                        if (_parent._queue.Count > 0 &&
                            _parent._memoryTracker.CanAllocate())
                        {
                            _parent._queue.TryDequeue(out request, out _);
                        }
                    }

                    if (request != null)
                    {
                        await ProcessRequest(request);
                    }
                    else
                    {
                        await Task.Delay(10);
                    }
                }
            }


            private async Task ProcessRequest(StreamRequest request)
            {
                if (request.Texture.LoadedMipLevels > request.TargetMip)
                    return;

                var (data, size) = await MipDataProvider.LoadMipAsync(
                    request.Texture,
                    request.TargetMip); // Load the specific target mip

                request.Texture.UpdateMipLevel(request.TargetMip, data, size);
                Marshal.FreeHGlobal(data);

                _parent._memoryTracker.UpdateUsage(size);
            }

            public void Dispose() => _thread.Join();
        }

        private record StreamRequest(StreamableTexture Texture, uint TargetMip);

        public record StreamPriority(StreamableTexture Texture, float NewPriority);
    }
}