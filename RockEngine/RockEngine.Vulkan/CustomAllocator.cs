using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public unsafe class CustomAllocator
    {
        private static readonly ConcurrentDictionary<IntPtr, AllocationInfo> _allocations = new();
        private static long _totalAllocatedBytes;
        private static long _peakAllocatedBytes;
        private static int _totalAllocationCount;

        private struct AllocationInfo
        {
            public nuint Size;
            public SystemAllocationScope Scope;
            public string StackTrace;
        }

        private static void* Allocate(void* pUserData, nuint size, nuint alignment, SystemAllocationScope allocationScope)
        {
            var ptr = (void*)Marshal.AllocHGlobal((int)size);
            var allocInfo = new AllocationInfo
            {
                Size = size,
                Scope = allocationScope,
                StackTrace = Environment.StackTrace
            };

            _allocations[(IntPtr)ptr] = allocInfo;
            Interlocked.Add(ref _totalAllocatedBytes, (long)size);
            Interlocked.Increment(ref _totalAllocationCount);
            UpdatePeakMemoryUsage();

            Console.WriteLine($"[ALLOC] Size: {size} bytes, Alignment: {alignment}, Scope: {allocationScope}, Address: 0x{(nint)ptr:X}");
            return ptr;
        }

        private static void* Reallocate(void* pUserData, void* pOriginal, nuint size, nuint alignment, SystemAllocationScope allocationScope)
        {
            var oldSize = _allocations[(IntPtr)pOriginal].Size;
            var ptr = (void*)Marshal.ReAllocHGlobal((nint)pOriginal, (IntPtr)size);

            _allocations.TryRemove((IntPtr)pOriginal, out _);
            _allocations[(IntPtr)ptr] = new AllocationInfo
            {
                Size = size,
                Scope = allocationScope,
                StackTrace = Environment.StackTrace
            };

            Interlocked.Add(ref _totalAllocatedBytes, (long)size - (long)oldSize);
            UpdatePeakMemoryUsage();

            Console.WriteLine($"[REALLOC] Old Address: 0x{(nint)pOriginal:X}, New Address: 0x{(nint)ptr:X}, New Size: {size} bytes, Alignment: {alignment}, Scope: {allocationScope}");
            return ptr;
        }

        private static void Free(void* pUserData, void* pMemory)
        {
            if (_allocations.TryRemove((IntPtr)pMemory, out var allocInfo))
            {
                Interlocked.Add(ref _totalAllocatedBytes, -(long)allocInfo.Size);
                Interlocked.Decrement(ref _totalAllocationCount);
            }

            Console.WriteLine($"[FREE] Address: 0x{(nint)pMemory:X}");
            Marshal.FreeHGlobal((nint)pMemory);
        }

        private static void InternalAllocationNotification(void* pUserData, nuint size, InternalAllocationType allocationType, SystemAllocationScope allocationScope)
        {
            Console.WriteLine($"[INTERNAL ALLOC] Size: {size} bytes, Type: {allocationType}, Scope: {allocationScope}");
        }

        private static void InternalFreeNotification(void* pUserData, nuint size, InternalAllocationType allocationType, SystemAllocationScope allocationScope)
        {
            Console.WriteLine($"[INTERNAL FREE] Size: {size} bytes, Type: {allocationType}, Scope: {allocationScope}");
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

        public static AllocationCallbacks CreateCallbacks()
        {
            Console.WriteLine("[CUSTOM ALLOCATOR] Creating allocation callbacks");
            return new AllocationCallbacks
            {
                PfnAllocation = new PfnAllocationFunction(Allocate),
                PfnReallocation = new PfnReallocationFunction(Reallocate),
                PfnFree = new PfnFreeFunction(Free),
                PfnInternalAllocation = new PfnInternalAllocationNotification(InternalAllocationNotification),
                PfnInternalFree = new PfnInternalFreeNotification(InternalFreeNotification)
            };
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
        public static void GetAllocationInfo(Action<(IntPtr Address, nuint Size, SystemAllocationScope Scope, string StackTrace)> callback)
        {
            foreach (var kvp in _allocations)
            {
                callback((kvp.Key, kvp.Value.Size, kvp.Value.Scope, kvp.Value.StackTrace));
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
