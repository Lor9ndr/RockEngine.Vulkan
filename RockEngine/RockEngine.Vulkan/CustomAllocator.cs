using NLog;

using Silk.NET.Vulkan;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace RockEngine.Vulkan
{
    public static unsafe class VulkanAllocator
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        // Store callbacks to prevent garbage collection
        private static readonly Dictionary<Type, AllocationCallbacks> _callbacksCache = new();
        private static readonly Lock _cacheLock = new Lock();

        // Track ALL memory through the allocator
        private static readonly ConcurrentDictionary<IntPtr, AllocationInfo> _allocations = new();
        private static readonly ConcurrentDictionary<string, ResourceStats> _statsByType = new();

        // Separate totals for host and device memory
        private static long _totalHostAllocated;
        private static long _totalDeviceAllocated;
        private static long _peakHostAllocated;
        private static long _peakDeviceAllocated;
        private static int _hostAllocationCount;
        private static int _deviceAllocationCount;
        private static int _hostFreeCount;
        private static int _deviceFreeCount;

        private static readonly bool _enableStackTrace = Debugger.IsAttached;

        // Store actual device memory handles with detailed info
        private static readonly ConcurrentDictionary<DeviceMemory, DeviceMemoryInfo> _deviceMemoryObjects = new();

        // Track relationships between device memory and host allocations
        private static readonly ConcurrentDictionary<DeviceMemory, List<HostAllocationReference>> _deviceToHostMappings = new();
        private static readonly ConcurrentDictionary<IntPtr, DeviceMemory> _hostToDeviceMappings = new();

        // Track allocation chains (device memory created from host allocations)
        private static readonly ConcurrentDictionary<DeviceMemory, AllocationChainInfo> _allocationChains = new();

        public class HostAllocationReference
        {
            public IntPtr HostPtr { get; set; }
            public long Size { get; set; }
            public string TypeName { get; set; } = string.Empty;
            public string StackTrace { get; set; } = string.Empty;
            public DateTime AllocationTime { get; set; }
        }

        public class AllocationChainInfo
        {
            public DeviceMemory DeviceMemory { get; set; }
            public DeviceMemoryInfo DeviceInfo { get; set; } = null!;
            public List<HostAllocationReference> HostSources { get; } = new();
            public string FullCallChain { get; set; } = string.Empty;
            public DateTime CreationTime { get; set; }
        }

        public class DeviceMemoryInfo
        {
            public DeviceMemory DeviceMemory { get; set; }  // Add this line!
            public ulong AllocationSize { get; set; }
            public MemoryPropertyFlags MemoryPropertyFlags { get; set; }
            public string TypeName { get; set; } = string.Empty;
            public string StackTrace { get; set; } = string.Empty;
            public string CallChain { get; set; } = string.Empty;
            public List<string> RelatedStackTraces { get; } = new();
        }

        private class AllocationInfo
        {
            public nuint Size { get; set; }
            public string TypeName { get; set; } = string.Empty;
            public string? StackTrace { get; set; }
            public DateTime AllocationTime { get; set; }
            public SystemAllocationScope Scope { get; set; }
            public bool IsDeviceMemory { get; set; }
            public DeviceMemory? AssociatedDeviceMemory { get; set; }
        }

        private class ResourceStats
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
            public string TypeName = string.Empty;
        }

        // Custom methods for getting real device memory usage with stack trace tracking
        public static class DeviceMemoryTracker
        {
            private static readonly ConcurrentDictionary<DeviceMemory, List<VulkanObjectReference>> _deviceMemoryToObjects = new();
            private static readonly ConcurrentDictionary<IntPtr, DeviceMemory> _objectToDeviceMemory = new();
            public class VulkanObjectReference
            {
                public string Type { get; set; } = string.Empty; // "Buffer", "Image", etc.
                public ulong Handle { get; set; }
                public ulong Size { get; set; }
                public ulong Offset { get; set; }
                public DateTime BindingTime { get; set; }
                public string StackTrace { get; set; } = string.Empty;
                public object? UserData { get; set; } // For storing custom info like the VkBuffer object
            }
            /// <summary>
            /// Register a device memory allocation with full call chain tracking
            /// </summary>
            public static void RegisterDeviceMemory(DeviceMemory memory, ulong size,
                MemoryPropertyFlags flags, string typeName,
                List<IntPtr> relatedHostAllocations = null)
            {
                // Capture full stack trace and call chain
                var stackTrace = _enableStackTrace ? new StackTrace(3, true).ToString() : string.Empty;
                var callChain = GetCallChain(5); // Get 5 levels of call chain

                var info = new DeviceMemoryInfo
                {
                    DeviceMemory = memory,
                    AllocationSize = size,
                    MemoryPropertyFlags = flags,
                    TypeName = typeName,
                    StackTrace = stackTrace,
                    CallChain = callChain
                };

                // Track related host allocations if provided
                if (relatedHostAllocations != null)
                {
                    foreach (var hostPtr in relatedHostAllocations)
                    {
                        if (_allocations.TryGetValue(hostPtr, out var hostAlloc))
                        {
                            info.RelatedStackTraces.Add(hostAlloc.StackTrace ?? string.Empty);

                            // Create mapping
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
                                (key, existing) => { existing.Add(hostRef); return existing; });

                            _hostToDeviceMappings[hostPtr] = memory;
                        }
                    }
                }

                _deviceMemoryObjects[memory] = info;

                // Create allocation chain info
                var chainInfo = new AllocationChainInfo
                {
                    DeviceMemory = memory,
                    DeviceInfo = info,
                    FullCallChain = GetFullCallChain(memory, info),
                    CreationTime = DateTime.UtcNow
                };

                _allocationChains[memory] = chainInfo;

                // Update device memory statistics
                if ((flags & MemoryPropertyFlags.DeviceLocalBit) != 0)
                {
                    Interlocked.Add(ref _totalDeviceAllocated, (long)size);
                    Interlocked.Increment(ref _deviceAllocationCount);

                    // Update device peak
                    long newDeviceTotal = Interlocked.Read(ref _totalDeviceAllocated);
                    long currentDevicePeak = Interlocked.Read(ref _peakDeviceAllocated);
                    while (newDeviceTotal > currentDevicePeak)
                    {
                        long prevPeak = Interlocked.CompareExchange(ref _peakDeviceAllocated, newDeviceTotal, currentDevicePeak);
                        if (prevPeak == currentDevicePeak)
                            break;
                        currentDevicePeak = prevPeak;
                    }

                    // Update type statistics
                    var stats = _statsByType.GetOrAdd(typeName, _ => new ResourceStats { TypeName = typeName });
                    Interlocked.Add(ref stats.DeviceAllocated, (long)size);
                    Interlocked.Increment(ref stats.DeviceAllocationCount);
                }

                _logger.Trace($"Registered device memory: {FormatSize((long)size)} for {typeName}");
            }

            /// <summary>
            /// Associate a Vulkan object (Buffer, Image) with device memory
            /// </summary>
            public static void AssociateObject(DeviceMemory deviceMemory, ulong objectHandle, string objectType,
                ulong size, ulong offset = 0, object? userData = null)
            {
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

                // Add to device memory -> objects mapping
                _deviceMemoryToObjects.AddOrUpdate(deviceMemory,
                    new List<VulkanObjectReference> { objRef },
                    (key, existing) => { existing.Add(objRef); return existing; });

                // Add to object -> device memory mapping
                _objectToDeviceMemory[(IntPtr)objectHandle] = deviceMemory;
            }

            /// <summary>
            /// Remove association when object is destroyed
            /// </summary>
            public static void DisassociateObject(ulong objectHandle)
            {
                if (_objectToDeviceMemory.TryRemove((IntPtr)objectHandle, out var deviceMemory))
                {
                    if (_deviceMemoryToObjects.TryGetValue(deviceMemory, out var objects))
                    {
                        objects.RemoveAll(obj => obj.Handle == objectHandle);
                    }
                }
            }


            /// <summary>
            /// Unregister a device memory allocation
            /// </summary>
            public static void UnregisterDeviceMemory(DeviceMemory memory)
            {
                if (_deviceMemoryObjects.TryRemove(memory, out var info))
                {
                    // Clean up mappings
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

                        // Update type statistics
                        var stats = _statsByType.GetOrAdd(info.TypeName, _ => new ResourceStats { TypeName = info.TypeName });
                        Interlocked.Add(ref stats.DeviceFreed, (long)info.AllocationSize);
                        Interlocked.Increment(ref stats.DeviceFreeCount);
                    }

                    _logger.Trace($"Unregistered device memory: {FormatSize((long)info.AllocationSize)}");
                }
            }

            /// <summary>
            /// Get actual device memory usage (VRAM)
            /// </summary>
            public static long GetActualDeviceMemoryUsage() => Interlocked.Read(ref _totalDeviceAllocated);

            /// <summary>
            /// Get actual host memory usage (RAM)
            /// </summary>
            public static long GetActualHostMemoryUsage() => Interlocked.Read(ref _totalHostAllocated);

            /// <summary>
            /// Get detailed device memory information
            /// </summary>
            public static DeviceMemoryInfo[] GetDeviceMemoryDetails() => _deviceMemoryObjects.Values.ToArray();

            /// <summary>
            /// Get allocation chains for analysis
            /// </summary>
            public static AllocationChainInfo[] GetAllocationChains() => _allocationChains.Values.ToArray();

            /// <summary>
            /// Get device memory for a specific host allocation
            /// </summary>
            public static DeviceMemory? GetDeviceMemoryForHostAllocation(IntPtr hostPtr)
            {
                _hostToDeviceMappings.TryGetValue(hostPtr, out var deviceMemory);
                return deviceMemory;
            }

            /// <summary>
            /// Get host allocations for a specific device memory
            /// </summary>
            public static HostAllocationReference[] GetHostAllocationsForDeviceMemory(DeviceMemory deviceMemory)
            {
                if (_deviceToHostMappings.TryGetValue(deviceMemory, out var hostRefs))
                    return hostRefs.ToArray();
                return Array.Empty<HostAllocationReference>();
            }

            /// <summary>
            /// Dump allocation chains for debugging
            /// </summary>
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
                            sb.AppendLine($"    Stack: {host.StackTrace}");
                        }
                    }
                    sb.AppendLine();
                }

                _logger.Info(sb.ToString());
            }

            /// <summary>
            /// Get full call chain for device memory
            /// </summary>
            private static string GetFullCallChain(DeviceMemory memory, DeviceMemoryInfo info)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Device Memory: {info.TypeName}");
                sb.AppendLine($"Size: {FormatSize((long)info.AllocationSize)}");
                sb.AppendLine($"Flags: {info.MemoryPropertyFlags}");
                sb.AppendLine($"Stack Depth: {info.StackTrace?.Count(c => c == '\n') ?? 0} lines");

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

            /// <summary>
            /// Get call chain as string
            /// </summary>
            public static string GetCallChain(int depth)
            {
                if (!_enableStackTrace) return "Stack traces disabled";

                var stackTrace = new StackTrace(3, true); // Skip 3 frames to get to the actual caller
                var frames = stackTrace.GetFrames();
                if (frames == null || frames.Length == 0) return "No call chain available";

                var sb = new StringBuilder();
                int takeFrames = Math.Min(depth, frames.Length);

                for (int i = 0; i < takeFrames; i++)
                {
                    var frame = frames[i];
                    var method = frame.GetMethod();
                    if (method != null)
                    {
                        sb.Append($"{method.DeclaringType?.Name}.{method.Name}");
                        if (i < takeFrames - 1) sb.Append(" → ");
                    }
                }

                if (frames.Length > takeFrames) sb.Append(" → ...");

                return sb.ToString();
            }

            /// <summary>
            /// Get all device memories with their associated objects
            /// </summary>
            public static Dictionary<DeviceMemory, VulkanObjectReference[]> GetAllDeviceMemoryObjects()
            {
                return _deviceMemoryToObjects.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ToArray()
                );
            }

            /// <summary>
            /// Get all objects using a specific device memory
            /// </summary>
            public static VulkanObjectReference[] GetObjectsForDeviceMemory(DeviceMemory deviceMemory)
            {
                if (_deviceMemoryToObjects.TryGetValue(deviceMemory, out var objects))
                    return [.. objects];
                return [];
            }

        }

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
                    _logger.Warn($"Potential memory leaks:");
                    _logger.Warn($"  Host Memory: {FormatSize(_totalHostAllocated)}");
                    _logger.Warn($"  Device Memory: {FormatSize(_totalDeviceAllocated)}");

                    foreach (var (type, stats) in _statsByType)
                    {
                        if (stats.CurrentTotal > 0)
                        {
                            _logger.Warn($"  {type}: Host={FormatSize(stats.CurrentHost)}, Device={FormatSize(stats.CurrentDevice)}");
                        }
                    }

                    // Log allocation chains for leaked device memory
                    var chains = DeviceMemoryTracker.GetAllocationChains();
                    if (chains.Length > 0)
                    {
                        _logger.Warn($"Leaked device memory allocation chains:");
                        foreach (var chain in chains)
                        {
                            _logger.Warn($"  {chain.DeviceInfo.TypeName}: {FormatSize((long)chain.DeviceInfo.AllocationSize)}");
                            _logger.Warn($"    Created: {chain.CreationTime}");
                            _logger.Warn($"    Call Chain: {chain.DeviceInfo.CallChain}");
                        }
                    }
                }
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
        }

        static VulkanAllocator()
        {
            CreateCallbacks<object>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* Allocate<T>(void* pUserData, nuint size, nuint alignment, SystemAllocationScope allocationScope)
        {
            try
            {
                var ptr = (void*)Marshal.AllocHGlobal((nint)size);

                // Capture stack trace
                string? stackTrace = null;
                string callChain = string.Empty;

                if (_enableStackTrace)
                {
                    stackTrace = new StackTrace(2, true).ToString();
                    callChain = DeviceMemoryTracker.GetCallChain(3);
                }

                Interlocked.Add(ref _totalHostAllocated, (long)size);
                Interlocked.Increment(ref _hostAllocationCount);

                // Update host peak
                long newHostTotal = Interlocked.Read(ref _totalHostAllocated);
                long currentHostPeak = Interlocked.Read(ref _peakHostAllocated);
                while (newHostTotal > currentHostPeak)
                {
                    long prevPeak = Interlocked.CompareExchange(ref _peakHostAllocated, newHostTotal, currentHostPeak);
                    if (prevPeak == currentHostPeak)
                        break;
                    currentHostPeak = prevPeak;
                }

                // Track allocation details with stack trace
                var info = new AllocationInfo
                {
                    Size = size,
                    TypeName = typeof(T).Name,
                    AllocationTime = DateTime.UtcNow,
                    Scope = allocationScope,
                    StackTrace = stackTrace,
                    IsDeviceMemory = false
                };
                _allocations[(IntPtr)ptr] = info;

                // Update type statistics
                var stats = _statsByType.GetOrAdd(typeof(T).Name, _ => new ResourceStats { TypeName = typeof(T).Name });
                Interlocked.Add(ref stats.HostAllocated, (long)size);
                Interlocked.Increment(ref stats.HostAllocationCount);

                //_logger.Trace($"Allocated {FormatSize((long)size)} for {typeof(T).Name} (Host memory)");
                // if (_enableStackTrace)
                    //_logger.Trace($"  Call Chain: {callChain}");

                return ptr;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to allocate {size} bytes for {typeof(T).Name}");
                throw;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void* Reallocate<T>(void* pUserData, void* pOriginal, nuint size, nuint alignment, SystemAllocationScope allocationScope)
        {
            if (pOriginal == null)
                return Allocate<T>(pUserData, size, alignment, allocationScope);

            try
            {
                // Get old allocation info
                nuint oldSize = 0;
                if (_allocations.TryGetValue((IntPtr)pOriginal, out var oldInfo))
                {
                    oldSize = oldInfo.Size;
                }

                var ptr = (void*)Marshal.ReAllocHGlobal((nint)pOriginal, (IntPtr)size);

                // Update tracking based on size change
                var sizeDiff = (long)size - (long)oldSize;
                if (sizeDiff != 0)
                {
                    Interlocked.Add(ref _totalHostAllocated, -(long)oldSize);
                    Interlocked.Add(ref _totalHostAllocated, (long)size);

                    // Update host peak
                    long newHostTotal = Interlocked.Read(ref _totalHostAllocated);
                    long currentHostPeak = Interlocked.Read(ref _peakHostAllocated);
                    while (newHostTotal > currentHostPeak)
                    {
                        long prevPeak = Interlocked.CompareExchange(ref _peakHostAllocated, newHostTotal, currentHostPeak);
                        if (prevPeak == currentHostPeak)
                            break;
                        currentHostPeak = prevPeak;
                    }

                    // Update type statistics
                    var stats = _statsByType.GetOrAdd(typeof(T).Name, _ => new ResourceStats { TypeName = typeof(T).Name });
                    Interlocked.Add(ref stats.HostAllocated, -(long)oldSize);
                    Interlocked.Add(ref stats.HostAllocated, (long)size);
                }

                // Update allocation details
                var newInfo = new AllocationInfo
                {
                    Size = size,
                    TypeName = typeof(T).Name,
                    AllocationTime = DateTime.UtcNow,
                    Scope = allocationScope,
                    StackTrace = _enableStackTrace ? new StackTrace(2, true).ToString() : null,
                    IsDeviceMemory = false,
                    AssociatedDeviceMemory = oldInfo?.AssociatedDeviceMemory
                };
                _allocations[(IntPtr)ptr] = newInfo;

                //_logger.Trace($"Reallocated {FormatSize((long)oldSize)} to {FormatSize((long)size)} for {typeof(T).Name}");

                return ptr;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to reallocate memory for {typeof(T).Name}");
                throw;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Free<T>(void* pUserData, void* pMemory)
        {
            if (pMemory == null)
                return;

            try
            {
                // Get allocation info before freeing
                if (_allocations.TryRemove((IntPtr)pMemory, out var info))
                {
                    Interlocked.Add(ref _totalHostAllocated, -(long)info.Size);
                    Interlocked.Increment(ref _hostFreeCount);

                    // Update type statistics
                    var stats = _statsByType.GetOrAdd(info.TypeName, _ => new ResourceStats { TypeName = info.TypeName });
                    Interlocked.Add(ref stats.HostFreed, (long)info.Size);
                    Interlocked.Increment(ref stats.HostFreeCount);

                    //_logger.Trace($"Freed {FormatSize((long)info.Size)} from {info.TypeName} (Host memory)");

                    // Remove from device mappings if associated
                    if (info.AssociatedDeviceMemory.HasValue)
                    {
                        _hostToDeviceMappings.TryRemove((IntPtr)pMemory, out _);
                    }
                }

                Marshal.FreeHGlobal((nint)pMemory);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to free memory");
                throw;
            }
        }

        private static void InternalAllocationNotification<T>(void* pUserData, nuint size, InternalAllocationType allocationType, SystemAllocationScope allocationScope)
        {
            _logger.Trace($"Internal allocation: {size} bytes, type: {allocationType}, scope: {allocationScope}");
        }

        private static void InternalFreeNotification<T>(void* pUserData, nuint size, InternalAllocationType allocationType, SystemAllocationScope allocationScope)
        {
            _logger.Trace($"Internal free: {size} bytes, type: {allocationType}, scope: {allocationScope}");
        }

        // Create allocation callbacks
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

        // Get statistics for ImGui
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

        // Public structures for statistics
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