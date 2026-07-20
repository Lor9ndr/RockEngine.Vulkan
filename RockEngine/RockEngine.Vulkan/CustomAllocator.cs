using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using NLog;
using Silk.NET.Vulkan;

namespace RockEngine.Vulkan
{
    public static unsafe class VulkanAllocator
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        // --------------------------------------------------------------------
        // Detailed tracking can be entirely disabled in release builds
        // --------------------------------------------------------------------
#if DEBUG
        private const bool EnableDetailedTracking = true;
#else
        private const bool EnableDetailedTracking = false;   // no allocations, no stack traces
#endif

        // --------------------------------------------------------------------
        // Callbacks cache (allocated once per type – negligible)
        // --------------------------------------------------------------------
        private static readonly Dictionary<Type, AllocationCallbacks> _callbacksCache = new();
        private static readonly Lock _cacheLock = new();

        // --------------------------------------------------------------------
        // Lightweight global counters (always active)
        // --------------------------------------------------------------------
        private static long _totalHostAllocated;
        private static long _totalDeviceAllocated;
        private static long _peakHostAllocated;
        private static long _peakDeviceAllocated;
        private static int _hostAllocationCount;
        private static int _deviceAllocationCount;
        private static int _hostFreeCount;
        private static int _deviceFreeCount;

        // --------------------------------------------------------------------
        // Per‑type statistics (struct avoids per‑type object allocations)
        // --------------------------------------------------------------------
        private struct ResourceStats
        {
            public long HostAllocated;
            public long HostFreed;
            public int HostAllocationCount;
            public int HostFreeCount;

            public long DeviceAllocated;
            public long DeviceFreed;
            public int DeviceAllocationCount;
            public int DeviceFreeCount;

            public long CurrentHost => HostAllocated - HostFreed;
            public long CurrentDevice => DeviceAllocated - DeviceFreed;
            public long CurrentTotal => CurrentHost + CurrentDevice;
            // Type name is the dictionary key, so no need to store it
        }
        private static readonly ConcurrentDictionary<string, ResourceStats> _statsByType = new();

        // --------------------------------------------------------------------
        // Allocations dictionary – only compiled/used when detailed tracking is on
        // --------------------------------------------------------------------
#if DEBUG
        private static readonly ConcurrentDictionary<IntPtr, AllocationInfo> _allocations = new();
#endif

        private class AllocationInfo
        {
            public nuint Size;
            public string TypeName = string.Empty;
            public string? StackTrace;
            public DateTime AllocationTime;
            public SystemAllocationScope Scope;
            public bool IsDeviceMemory;
            public DeviceMemory? AssociatedDeviceMemory;
        }

        // --------------------------------------------------------------------
        // DeviceMemoryTracker – only compiled when detailed tracking is on
        // --------------------------------------------------------------------
        public static class DeviceMemoryTracker
        {
            private static readonly ConcurrentDictionary<DeviceMemory, List<VulkanObjectReference>> _deviceMemoryToObjects = new();
            private static readonly ConcurrentDictionary<IntPtr, DeviceMemory> _objectToDeviceMemory = new();

            // Store actual device memory handles with detailed info
            private static readonly ConcurrentDictionary<DeviceMemory, DeviceMemoryInfo> _deviceMemoryObjects = new();
            // Track relationships between device memory and host allocations
            private static readonly ConcurrentDictionary<DeviceMemory, List<HostAllocationReference>> _deviceToHostMappings = new();
            private static readonly ConcurrentDictionary<IntPtr, DeviceMemory> _hostToDeviceMappings = new();
            // Track allocation chains
            private static readonly ConcurrentDictionary<DeviceMemory, AllocationChainInfo> _allocationChains = new();

            public class VulkanObjectReference
            {
                public string Type { get; set; } = string.Empty;
                public ulong Handle;
                public ulong Size;
                public ulong Offset;
                public DateTime BindingTime;
                public string StackTrace { get; set; } = string.Empty;
                public object? UserData;
            }

            public class HostAllocationReference
            {
                public IntPtr HostPtr;
                public long Size;
                public string TypeName = string.Empty;
                public string StackTrace = string.Empty;
                public DateTime AllocationTime;
            }

            public class AllocationChainInfo
            {
                public DeviceMemory DeviceMemory;
                public DeviceMemoryInfo DeviceInfo = null!;
                public List<HostAllocationReference> HostSources { get; } = new();
                public string FullCallChain = string.Empty;
                public DateTime CreationTime;
            }

            public class DeviceMemoryInfo
            {
                public DeviceMemory DeviceMemory;
                public ulong AllocationSize;
                public MemoryPropertyFlags MemoryPropertyFlags;
                public string TypeName = string.Empty;
                public string StackTrace = string.Empty;
                public string CallChain = string.Empty;
                public List<string> RelatedStackTraces { get; } = new();
            }

            public static void RegisterDeviceMemory(DeviceMemory memory, ulong size,
                MemoryPropertyFlags flags, string typeName,
                List<IntPtr>? relatedHostAllocations = null)
            {
#if DEBUG

                var stackTrace = Debugger.IsAttached ? new StackTrace(3, true).ToString() : string.Empty;
                var callChain = GetCallChain(5);

                var info = new DeviceMemoryInfo
                {
                    DeviceMemory = memory,
                    AllocationSize = size,
                    MemoryPropertyFlags = flags,
                    TypeName = typeName,
                    StackTrace = stackTrace,
                    CallChain = callChain
                };

                if (relatedHostAllocations != null)
                {
                    foreach (var hostPtr in relatedHostAllocations)
                    {
                        if (_allocations.TryGetValue(hostPtr, out var hostAlloc))
                        {
                            info.RelatedStackTraces.Add(hostAlloc.StackTrace ?? string.Empty);
                            var hostRef = new HostAllocationReference
                            {
                                HostPtr = hostPtr,
                                Size = (long)hostAlloc.Size,
                                TypeName = hostAlloc.TypeName,
                                StackTrace = hostAlloc.StackTrace ?? string.Empty,
                                AllocationTime = hostAlloc.AllocationTime
                            };
                            _deviceToHostMappings.AddOrUpdate(memory,
                                new List<HostAllocationReference> { hostRef },
                                (key, existing) =>
                                {
                                    existing.Add(hostRef);
                                    return existing;
                                });
                            _hostToDeviceMappings[hostPtr] = memory;
                        }
                    }
                }

                _deviceMemoryObjects[memory] = info;

                var chainInfo = new AllocationChainInfo
                {
                    DeviceMemory = memory,
                    DeviceInfo = info,
                    FullCallChain = GetFullCallChain(memory, info),
                    CreationTime = DateTime.UtcNow
                };
                _allocationChains[memory] = chainInfo;

                if ((flags & MemoryPropertyFlags.DeviceLocalBit) != 0)
                {
                    Interlocked.Add(ref _totalDeviceAllocated, (long)size);
                    Interlocked.Increment(ref _deviceAllocationCount);

                    long newDeviceTotal = Interlocked.Read(ref _totalDeviceAllocated);
                    long currentDevicePeak = Interlocked.Read(ref _peakDeviceAllocated);
                    while (newDeviceTotal > currentDevicePeak)
                    {
                        long prevPeak = Interlocked.CompareExchange(ref _peakDeviceAllocated, newDeviceTotal, currentDevicePeak);
                        if (prevPeak == currentDevicePeak)
                        {
                            break;
                        }

                        currentDevicePeak = prevPeak;
                    }

                    var stats = _statsByType.GetOrAdd(typeName, _ => new ResourceStats());
                    Interlocked.Add(ref stats.DeviceAllocated, (long)size);
                    Interlocked.Increment(ref stats.DeviceAllocationCount);
                }

                _logger.Trace("Registered device memory: {Size} for {Type}", FormatSize((long)size), typeName);
#endif
            }

            public static void AssociateObject(DeviceMemory deviceMemory, ulong objectHandle, string objectType,
                ulong size, ulong offset = 0, object? userData = null)
            {
#if DEBUG

                var stackTrace = Debugger.IsAttached ? new StackTrace(2, true).ToString() : string.Empty;
                var objRef = new VulkanObjectReference
                {
                    Type = objectType,
                    Handle = objectHandle,
                    Size = size,
                    Offset = offset,
                    BindingTime = DateTime.UtcNow,
                    StackTrace = stackTrace,
                    UserData = userData
                };
                _deviceMemoryToObjects.AddOrUpdate(deviceMemory,
                    new List<VulkanObjectReference> { objRef },
                    (key, existing) =>
                    {
                        existing.Add(objRef);
                        return existing;
                    });
                _objectToDeviceMemory[(IntPtr)objectHandle] = deviceMemory;
#endif
            }

            public static void DisassociateObject(ulong objectHandle)
            {
#if DEBUG

                if (_objectToDeviceMemory.TryRemove((IntPtr)objectHandle, out var deviceMemory))
                {
                    if (_deviceMemoryToObjects.TryGetValue(deviceMemory, out var objects))
                    {
                        objects.RemoveAll(obj => obj.Handle == objectHandle);
                    }
                }
#endif
            }

            public static void UnregisterDeviceMemory(DeviceMemory memory)
            {
#if DEBUG

                if (_deviceMemoryObjects.TryRemove(memory, out var info))
                {
                    if (_deviceToHostMappings.TryRemove(memory, out var hostRefs))
                    {
                        foreach (var hostRef in hostRefs)
                        {
                            _hostToDeviceMappings.TryRemove(hostRef.HostPtr, out _);
                        }
                    }
                    _allocationChains.TryRemove(memory, out _);

                    if ((info.MemoryPropertyFlags & MemoryPropertyFlags.DeviceLocalBit) != 0)
                    {
                        Interlocked.Add(ref _totalDeviceAllocated, -(long)info.AllocationSize);
                        Interlocked.Increment(ref _deviceFreeCount);
                        var stats = _statsByType.GetOrAdd(info.TypeName, _ => new ResourceStats());
                        Interlocked.Add(ref stats.DeviceFreed, (long)info.AllocationSize);
                        Interlocked.Increment(ref stats.DeviceFreeCount);
                    }
                    _logger.Trace("Unregistered device memory: {Size}", FormatSize((long)info.AllocationSize));
                }
#endif
            }

            public static long GetActualDeviceMemoryUsage() => Interlocked.Read(ref _totalDeviceAllocated);
            public static long GetActualHostMemoryUsage() => Interlocked.Read(ref _totalHostAllocated);
            public static DeviceMemoryInfo[] GetDeviceMemoryDetails() => _deviceMemoryObjects.Values.ToArray();
            public static AllocationChainInfo[] GetAllocationChains() => _allocationChains.Values.ToArray();

            public static DeviceMemory? GetDeviceMemoryForHostAllocation(IntPtr hostPtr)
            {
                _hostToDeviceMappings.TryGetValue(hostPtr, out var deviceMemory);
                return deviceMemory;
            }

            public static HostAllocationReference[] GetHostAllocationsForDeviceMemory(DeviceMemory deviceMemory)
            {
                if (_deviceToHostMappings.TryGetValue(deviceMemory, out var hostRefs))
                {
                    return hostRefs.ToArray();
                }

                return Array.Empty<HostAllocationReference>();
            }

            public static void DumpAllocationChains()
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== Allocation Chains ===");
                foreach (var chain in _allocationChains.Values)
                {
                    sb.AppendLine($"Device Memory: {chain.DeviceInfo.TypeName} ({FormatSize((long)chain.DeviceInfo.AllocationSize)})");
                    sb.AppendLine($"Created: {chain.CreationTime:yyyy-MM-dd HH:mm:ss.fff}");
                    sb.AppendLine($"Call Chain: {chain.DeviceInfo.CallChain}");
                    sb.AppendLine($"Stack Trace: {chain.DeviceInfo.StackTrace}");
                    if (chain.HostSources.Count > 0)
                    {
                        sb.AppendLine("Related Host Allocations:");
                        foreach (var host in chain.HostSources)
                        {
                            sb.AppendLine($"  - {host.TypeName}: {FormatSize(host.Size)}");
                        }
                    }
                    sb.AppendLine();
                }
                _logger.Info(sb.ToString());
            }

            public static string GetCallChain(int depth)
            {
                if (!Debugger.IsAttached)
                {
                    return "Stack traces disabled";
                }

                var stackTrace = new StackTrace(3, true);
                var frames = stackTrace.GetFrames();
                if (frames == null || frames.Length == 0)
                {
                    return "No call chain available";
                }

                var sb = new StringBuilder();
                int takeFrames = Math.Min(depth, frames.Length);
                for (int i = 0; i < takeFrames; i++)
                {
                    var method = frames[i].GetMethod();
                    if (method != null)
                    {
                        sb.Append($"{method.DeclaringType?.Name}.{method.Name}");
                        if (i < takeFrames - 1)
                        {
                            sb.Append(" → ");
                        }
                    }
                }
                if (frames.Length > takeFrames)
                {
                    sb.Append(" → ...");
                }

                return sb.ToString();
            }

            public static Dictionary<DeviceMemory, VulkanObjectReference[]> GetAllDeviceMemoryObjects()
            {
                return _deviceMemoryToObjects.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray());
            }

            public static VulkanObjectReference[] GetObjectsForDeviceMemory(DeviceMemory deviceMemory)
            {
                if (_deviceMemoryToObjects.TryGetValue(deviceMemory, out var objects))
                {
                    return objects.ToArray();
                }

                return Array.Empty<VulkanObjectReference>();
            }

            private static string GetFullCallChain(DeviceMemory memory, DeviceMemoryInfo info)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Device Memory: {info.TypeName}");
                sb.AppendLine($"Size: {FormatSize((long)info.AllocationSize)}");
                sb.AppendLine($"Flags: {info.MemoryPropertyFlags}");
                if (_deviceToHostMappings.TryGetValue(memory, out var hostRefs))
                {
                    sb.AppendLine($"Linked to {hostRefs.Count} host allocation(s):");
                    foreach (var host in hostRefs)
                    {
                        sb.AppendLine($"  Host: {host.TypeName} ({FormatSize(host.Size)})");
                    }
                }
                return sb.ToString();
            }
        }

                // --------------------------------------------------------------------
                // Public statistics helpers
                // --------------------------------------------------------------------
        public static class MemoryTracker
        {
            public static long TotalHostMemory => _totalHostAllocated;
            public static long TotalDeviceMemory => _totalDeviceAllocated;
            public static long TotalMemory => _totalHostAllocated + _totalDeviceAllocated;
            public static long PeakHostMemory => _peakHostAllocated;
            public static long PeakDeviceMemory => _peakDeviceAllocated;
            public static long PeakMemory => Math.Max(_peakHostAllocated, _peakDeviceAllocated);

            public static int ActiveHostAllocations => _hostAllocationCount - _hostFreeCount;
            public static int ActiveDeviceAllocations => _deviceAllocationCount - _deviceFreeCount;
            public static int ActiveAllocations => ActiveHostAllocations + ActiveDeviceAllocations;

            public static void DumpStats()
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== Vulkan Memory Statistics ===");
                sb.AppendLine($"Host Memory (System RAM): {FormatSize(_totalHostAllocated)}");
                sb.AppendLine($"  Peak: {FormatSize(_peakHostAllocated)}");
                sb.AppendLine($"  Active Allocations: {ActiveHostAllocations}");
                sb.AppendLine($"Device Memory (VRAM): {FormatSize(_totalDeviceAllocated)}");
                sb.AppendLine($"  Peak: {FormatSize(_peakDeviceAllocated)}");
                sb.AppendLine($"  Active Allocations: {ActiveDeviceAllocations}");
                sb.AppendLine($"Total Memory: {FormatSize(TotalMemory)}");
                sb.AppendLine($"  Peak: {FormatSize(PeakMemory)}");
                sb.AppendLine($"  Active: {ActiveAllocations}");

                sb.AppendLine("\n=== By Resource Type ===");
                foreach (var (type, stats) in _statsByType)
                {
                    if (stats.CurrentTotal > 0)
                    {
                        sb.AppendLine($"{type}:");
                        sb.AppendLine($"  Host: {FormatSize(stats.CurrentHost)} (Allocs: {stats.HostAllocationCount}/{stats.HostFreeCount})");
                        sb.AppendLine($"  Device: {FormatSize(stats.CurrentDevice)} (Allocs: {stats.DeviceAllocationCount}/{stats.DeviceFreeCount})");
                    }
                }
                _logger.Info(sb.ToString());
            }

            public static void LogLeaks()
            {
                if (_totalHostAllocated > 0 || _totalDeviceAllocated > 0)
                {
                    _logger.Warn("Potential memory leaks:");
                    _logger.Warn("  Host Memory: {Host}", FormatSize(_totalHostAllocated));
                    _logger.Warn("  Device Memory: {Device}", FormatSize(_totalDeviceAllocated));
                    foreach (var (type, stats) in _statsByType)
                    {
                        if (stats.CurrentTotal > 0)
                        {
                            _logger.Warn("  {Type}: Host={Host}, Device={Device}",
                                type, FormatSize(stats.CurrentHost), FormatSize(stats.CurrentDevice));
                        }
                    }
#if DEBUG
                    var chains = DeviceMemoryTracker.GetAllocationChains();
                    if (chains.Length > 0)
                    {
                        _logger.Warn("Leaked device memory allocation chains:");
                        foreach (var chain in chains)
                        {
                            _logger.Warn("  {Type}: {Size}", chain.DeviceInfo.TypeName, FormatSize((long)chain.DeviceInfo.AllocationSize));
                            _logger.Warn("    Created: {Time}", chain.CreationTime);
                            _logger.Warn("    Call Chain: {Chain}", chain.DeviceInfo.CallChain);
                        }
                    }
#endif
                }
            }
        }

        // --------------------------------------------------------------------
        // Static constructor
        // --------------------------------------------------------------------
        static VulkanAllocator()
        {
            CreateCallbacks<object>();
        }

        // --------------------------------------------------------------------
        // Memory allocation callbacks
        // --------------------------------------------------------------------
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* Allocate<T>(void* pUserData, nuint size, nuint alignment, SystemAllocationScope allocationScope)
        {
            var ptr = (void*)Marshal.AllocHGlobal((nint)size);

            // Global host memory counters
            Interlocked.Add(ref _totalHostAllocated, (long)size);
            Interlocked.Increment(ref _hostAllocationCount);

            // Update host peak
            long newHostTotal = Interlocked.Read(ref _totalHostAllocated);
            long currentHostPeak = Interlocked.Read(ref _peakHostAllocated);
            while (newHostTotal > currentHostPeak)
            {
                long prevPeak = Interlocked.CompareExchange(ref _peakHostAllocated, newHostTotal, currentHostPeak);
                if (prevPeak == currentHostPeak)
                {
                    break;
                }

                currentHostPeak = prevPeak;
            }

            // Per‑type statistics (struct, no allocation)
            string typeName = typeof(T).Name;
            _statsByType.AddOrUpdate(typeName,
                _ => new ResourceStats { HostAllocated = (long)size, HostAllocationCount = 1 },
                (_, existing) =>
                {
                    existing.HostAllocated += (long)size;
                    existing.HostAllocationCount++;
                    return existing;
                });

#if DEBUG
            // Detailed per‑allocation record (only when enabled)
            var info = new AllocationInfo
            {
                Size = size,
                TypeName = typeName,
                AllocationTime = DateTime.UtcNow,
                Scope = allocationScope,
                StackTrace = Debugger.IsAttached ? new StackTrace(2, true).ToString() : null,
                IsDeviceMemory = false
            };
            _allocations[(IntPtr)ptr] = info;
#endif
            return ptr;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* Reallocate<T>(void* pUserData, void* pOriginal, nuint size, nuint alignment, SystemAllocationScope allocationScope)
        {
            if (pOriginal == null)
            {
                return Allocate<T>(pUserData, size, alignment, allocationScope);
            }

            nuint oldSize = 0;
#if DEBUG
            if (_allocations.TryGetValue((IntPtr)pOriginal, out var oldInfo))
            {
                oldSize = oldInfo.Size;
            }
#endif
            var ptr = (void*)Marshal.ReAllocHGlobal((nint)pOriginal, (IntPtr)size);

            long sizeDiff = (long)size - (long)oldSize;
            if (sizeDiff != 0)
            {
                Interlocked.Add(ref _totalHostAllocated, -(long)oldSize);
                Interlocked.Add(ref _totalHostAllocated, (long)size);

                long newHostTotal = Interlocked.Read(ref _totalHostAllocated);
                long currentHostPeak = Interlocked.Read(ref _peakHostAllocated);
                while (newHostTotal > currentHostPeak)
                {
                    long prevPeak = Interlocked.CompareExchange(ref _peakHostAllocated, newHostTotal, currentHostPeak);
                    if (prevPeak == currentHostPeak)
                    {
                        break;
                    }

                    currentHostPeak = prevPeak;
                }

                string typeName = typeof(T).Name;
                _statsByType.AddOrUpdate(typeName,
                    _ => new ResourceStats { HostAllocated = (long)size },
                    (_, existing) =>
                    {
                        existing.HostAllocated += sizeDiff;
                        return existing;
                    });
            }

#if DEBUG
            var newInfo = new AllocationInfo
            {
                Size = size,
                TypeName = typeof(T).Name,
                AllocationTime = DateTime.UtcNow,
                Scope = allocationScope,
                StackTrace = Debugger.IsAttached ? new StackTrace(2, true).ToString() : null,
                IsDeviceMemory = false,
                AssociatedDeviceMemory = oldInfo?.AssociatedDeviceMemory
            };
            _allocations[(IntPtr)ptr] = newInfo;
#endif
            return ptr;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Free<T>(void* pUserData, void* pMemory)
        {
            if (pMemory == null)
            {
                return;
            }

            nuint size = 0;
#if DEBUG
            if (_allocations.TryRemove((IntPtr)pMemory, out var info))
            {
                size = info.Size;
                // (device mapping cleanup is done inside DeviceMemoryTracker)
            }
#endif
            // Global counters
            Interlocked.Add(ref _totalHostAllocated, -(long)size);
            Interlocked.Increment(ref _hostFreeCount);

            // Per‑type statistics
            string typeName = typeof(T).Name;
            _statsByType.AddOrUpdate(typeName,
                _ => new ResourceStats { HostFreed = (long)size, HostFreeCount = 1 },
                (_, existing) =>
                {
                    existing.HostFreed += (long)size;
                    existing.HostFreeCount++;
                    return existing;
                });

            Marshal.FreeHGlobal((nint)pMemory);
        }

        private static void InternalAllocationNotification<T>(void* pUserData, nuint size, InternalAllocationType allocationType, SystemAllocationScope allocationScope)
        {
            _logger.Trace("Internal allocation: {Size} bytes, type: {AllocType}, scope: {Scope}", size, allocationType, allocationScope);
        }

        private static void InternalFreeNotification<T>(void* pUserData, nuint size, InternalAllocationType allocationType, SystemAllocationScope allocationScope)
        {
            _logger.Trace("Internal free: {Size} bytes, type: {AllocType}, scope: {Scope}", size, allocationType, allocationScope);
        }

        // --------------------------------------------------------------------
        // Callback creation (unchanged, minimal allocation)
        // --------------------------------------------------------------------
        public static ref AllocationCallbacks CreateCallbacks<T>()
        {
            var type = typeof(T);
            lock (_cacheLock)
            {
                if (!_callbacksCache.TryGetValue(type, out var callbacks))
                {
                    callbacks = new AllocationCallbacks
                    {
                        PfnAllocation = new PfnAllocationFunction(Allocate<T>),
                        PfnReallocation = new PfnReallocationFunction(Reallocate<T>),
                        PfnFree = new PfnFreeFunction(Free<T>),
                        PfnInternalAllocation = new PfnInternalAllocationNotification(InternalAllocationNotification<T>),
                        PfnInternalFree = new PfnInternalFreeNotification(InternalFreeNotification<T>)
                    };
                    _callbacksCache[type] = callbacks;
                }
                return ref CollectionsMarshal.GetValueRefOrNullRef(_callbacksCache, type);
            }
        }

        // --------------------------------------------------------------------
        // Public statistics for ImGui / monitoring
        // --------------------------------------------------------------------
        public static MemoryStatistics GetStatistics()
        {
            var byType = _statsByType
                .Select(kvp => new ResourceTypeStatistics
                {
                    TypeName = kvp.Key,
                    CurrentHost = kvp.Value.CurrentHost,
                    CurrentDevice = kvp.Value.CurrentDevice,
                    CurrentTotal = kvp.Value.CurrentTotal,
                    HostAllocationCount = kvp.Value.HostAllocationCount,
                    HostFreeCount = kvp.Value.HostFreeCount,
                    DeviceAllocationCount = kvp.Value.DeviceAllocationCount,
                    DeviceFreeCount = kvp.Value.DeviceFreeCount
                })
                .OrderByDescending(s => s.CurrentTotal)
                .ToList();

            return new MemoryStatistics
            {
                TotalHost = _totalHostAllocated,
                TotalDevice = _totalDeviceAllocated,
                TotalMemory = _totalHostAllocated + _totalDeviceAllocated,
                PeakHost = _peakHostAllocated,
                PeakDevice = _peakDeviceAllocated,
                ActiveHostAllocations = _hostAllocationCount - _hostFreeCount,
                ActiveDeviceAllocations = _deviceAllocationCount - _deviceFreeCount,
                ByResourceType = byType
            };
        }

        private static string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            double size = bytes;
            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }
            return $"{size:0.##} {suffixes[suffixIndex]}";
        }

        // --------------------------------------------------------------------
        // Public statistics structures
        // --------------------------------------------------------------------
        public struct MemoryStatistics
        {
            public long TotalHost;
            public long TotalDevice;
            public long TotalMemory;
            public long PeakHost;
            public long PeakDevice;
            public int ActiveHostAllocations;
            public int ActiveDeviceAllocations;
            public List<ResourceTypeStatistics> ByResourceType;
        }

        public struct ResourceTypeStatistics
        {
            public string TypeName;
            public long CurrentHost;
            public long CurrentDevice;
            public long CurrentTotal;
            public int HostAllocationCount;
            public int HostFreeCount;
            public int DeviceAllocationCount;
            public int DeviceFreeCount;
        }
    }
}