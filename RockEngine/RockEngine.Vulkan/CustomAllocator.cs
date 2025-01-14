using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    [DebuggerStepThrough]
    public static unsafe class CustomAllocator
    {
        private static readonly ConcurrentDictionary<IntPtr, AllocationInfo> _allocations = new();
        private static long _totalAllocatedBytes;
        private static long _peakAllocatedBytes;
        private static int _totalAllocationCount;
        private static readonly Dictionary<Type, AllocationCallbacks> _callbacksMap = new Dictionary<Type, AllocationCallbacks>();

        private struct AllocationInfo
        {
            public nuint Size;
            public SystemAllocationScope Scope;
            public string StackTrace;
            public string AllocatorType;
        }


        private static void* Allocate<T>(void* pUserData, nuint size, nuint alignment, SystemAllocationScope allocationScope)
        {
            var ptr = (void*)Marshal.AllocHGlobal((int)size);
            var name = typeof(T).Name;
            var allocInfo = new AllocationInfo
            {
                Size = size,
                Scope = allocationScope,
                StackTrace = Environment.StackTrace,
                AllocatorType = name
            };
            if (name == null)
            {

            }

            _allocations[(IntPtr)ptr] = allocInfo;
            Interlocked.Add(ref _totalAllocatedBytes, (long)size);
            Interlocked.Increment(ref _totalAllocationCount);
            UpdatePeakMemoryUsage();

            Console.WriteLine($"{typeof(T)} [ALLOC] Size: {size} bytes, Alignment: {alignment}, Scope: {allocationScope}, Address: 0x{(nint)ptr:X}");
            return ptr;
        }

        private static void* Reallocate<T>(void* pUserData, void* pOriginal, nuint size, nuint alignment, SystemAllocationScope allocationScope)
        {
            var oldSize = _allocations[(IntPtr)pOriginal].Size;
            var ptr = (void*)Marshal.ReAllocHGlobal((nint)pOriginal, (IntPtr)size);

           
            _allocations[(IntPtr)ptr] = new AllocationInfo
            {
                Size = size,
                Scope = allocationScope,
                StackTrace = Environment.StackTrace,
                AllocatorType = typeof(T).Name
            };

            Interlocked.Add(ref _totalAllocatedBytes, (long)size - (long)oldSize);
            UpdatePeakMemoryUsage();

            Console.WriteLine($"{typeof(T)}[REALLOC] Old Address: 0x{(nint)pOriginal:X}, New Address: 0x{(nint)ptr:X}, New Size: {size} bytes, Alignment: {alignment}, Scope: {allocationScope}");
            return ptr;
        }

        private static void Free<T>(void* pUserData, void* pMemory)
        {
            if (_allocations.TryRemove((IntPtr)pMemory, out var allocInfo))
            {
                Interlocked.Add(ref _totalAllocatedBytes, -(long)allocInfo.Size);
                Interlocked.Decrement(ref _totalAllocationCount);
            }

            Console.WriteLine($"{typeof(T)} [FREE] Address: 0x{(nint)pMemory:X}");
            Marshal.FreeHGlobal((nint)pMemory);
        }

        private static void InternalAllocationNotification<T>(void* pUserData, nuint size, InternalAllocationType allocationType, SystemAllocationScope allocationScope)
        {
            Console.WriteLine($"{typeof(T)} [INTERNAL ALLOC] Size: {size} bytes, Type: {allocationType}, Scope: {allocationScope}");
        }

        private static void InternalFreeNotification<T>(void* pUserData, nuint size, InternalAllocationType allocationType, SystemAllocationScope allocationScope)
        {
            Console.WriteLine($"{typeof(T)} [INTERNAL FREE] Size: {size} bytes, Type: {allocationType}, Scope: {allocationScope}");
        }

        private static void UpdatePeakMemoryUsage()
        {
            long currentTotal = Interlocked.Read(ref _totalAllocatedBytes);
            long currentPeak;
            do
            {
                currentPeak = Interlocked.Read(ref _peakAllocatedBytes);
                if (currentTotal <= currentPeak) break;
            } while (Interlocked.CompareExchange(ref _peakAllocatedBytes, currentTotal, currentPeak) != currentPeak);
        }

        public static ref AllocationCallbacks CreateCallbacks<T>()
        {
            var type = typeof(T);
            if (!_callbacksMap.ContainsKey(type))
            {
                Console.WriteLine($"[CUSTOM ALLOCATOR] Creating allocation callbacks for type {type.Name}");
                _callbacksMap[type] = new AllocationCallbacks
                {
                    PfnAllocation = new PfnAllocationFunction(Allocate<T>),
                    PfnReallocation = new PfnReallocationFunction(Reallocate<T>),
                    PfnFree = new PfnFreeFunction(Free<T>),
                    PfnInternalAllocation = new PfnInternalAllocationNotification(InternalAllocationNotification<T>),
                    PfnInternalFree = new PfnInternalFreeNotification(InternalFreeNotification<T>)
                };
            }
            return ref CollectionsMarshal.GetValueRefOrAddDefault(_callbacksMap, type, out _);
        }
        public static void GetMemoryStats(out long totalAllocatedBytes, out long peakAllocatedBytes, out int totalAllocationCount, out int currentActiveAllocations)
        {
            totalAllocatedBytes = Interlocked.Read(ref _totalAllocatedBytes);
            peakAllocatedBytes = Interlocked.Read(ref _peakAllocatedBytes);
            totalAllocationCount = _totalAllocationCount;
            currentActiveAllocations = _allocations.Count;
        }

        public static void PrintMemoryStats()
        {
            Console.WriteLine($"Total Allocated Memory: {_totalAllocatedBytes:N0} bytes");
            Console.WriteLine($"Peak Allocated Memory: {_peakAllocatedBytes:N0} bytes");
            Console.WriteLine($"Total Allocation Count: {_totalAllocationCount}");
            Console.WriteLine($"Current Active Allocations: {_allocations.Count}");
        }
        public static void GetAllocationInfo(Action<(IntPtr Address, nuint Size, SystemAllocationScope Scope, string StackTrace, string AllocatorType)> callback)
        {
            foreach (var kvp in _allocations)
            {
                if (kvp.Value.AllocatorType is null)
                {

                }
                callback((kvp.Key, kvp.Value.Size, kvp.Value.Scope, kvp.Value.StackTrace, kvp.Value.AllocatorType));
            }
        }

        private static void PrintAllocationTree(AllocationNode node, string indent = "")
        {
            Console.WriteLine($"{indent}{node.Name} - {FormatBytes(node.TotalSize)} ({node.Count} allocation(s))");

            foreach (var child in node.Children.OrderByDescending(c => c.TotalSize))
            {
                PrintAllocationTree(child, indent + "  ");
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return $"{size:0.##} {suffixes[order]}";
        }

        private class AllocationNode
        {
            public string Name { get; }
            public long TotalSize { get; private set; }
            public int Count { get; private set; }
            public List<AllocationNode> Children { get; } = new List<AllocationNode>();

            public AllocationNode(string name, long size)
            {
                Name = name;
                TotalSize = size;
                Count = size > 0 ? 1 : 0;
            }

            public AllocationNode AddChild(string name, long size)
            {
                var existingChild = Children.FirstOrDefault(c => c.Name == name);
                if (existingChild != null)
                {
                    existingChild.TotalSize += size;
                    existingChild.Count++;
                    return existingChild;
                }

                var newChild = new AllocationNode(name, size);
                Children.Add(newChild);
                return newChild;
            }
        }
    }
}
