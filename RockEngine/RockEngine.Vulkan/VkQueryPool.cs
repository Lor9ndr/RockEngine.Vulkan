using Silk.NET.Vulkan;

using System;
using System.Runtime.CompilerServices;

namespace RockEngine.Vulkan
{
    public sealed class VkQueryPool : VkObject<QueryPool>
    {
        private readonly VulkanContext _context;
        public readonly uint QueryCount;

        private VkQueryPool(QueryPool vkObject, VulkanContext context, uint queryCount)
            : base(vkObject)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            QueryCount = queryCount;
        }

        public override void LabelObject(string name)
        {
            _context.DebugUtils.SetDebugUtilsObjectName(_vkObject, ObjectType.QueryPool, name);
        }

        public static VkQueryPool Create(VulkanContext context, in QueryPoolCreateInfo createInfo)
        {
            VulkanContext.Vk.CreateQueryPool(
                context.Device,
                in createInfo,
                in VulkanContext.CustomAllocator<VkQueryPool>(),
                out QueryPool queryPool
            ).VkAssertResult("Failed to create Query pool");

            return new VkQueryPool(queryPool, context, createInfo.QueryCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe T[] GetResults<T>(
            uint firstQuery,
            uint queryCount,
            QueryResultFlags flags,
            out Result status) where T : unmanaged
        {
            T[] results = new T[queryCount];
            status = GetResults(firstQuery, queryCount, new Span<T>(results), (uint)sizeof(T), flags);
            return results;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe Result GetResults<T>(
         uint firstQuery,
         uint queryCount,
         Span<T> destination,
         QueryResultFlags flags) where T : unmanaged
        {
            return GetResults(firstQuery, queryCount, destination, (uint)sizeof(T), flags);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe Result GetResults<T>(
            uint firstQuery,
            uint queryCount,
            Span<T> destination,
            uint stride,
            QueryResultFlags flags) where T : unmanaged
        {
            uint elementsNeeded = queryCount * (stride / (uint)sizeof(T));

            if (destination.Length < elementsNeeded)
            {
                throw new ArgumentException($"Destination span is too small. Need {elementsNeeded} elements, got {destination.Length}", nameof(destination));
            }


            fixed (T* ptr = destination)
            {
                return VulkanContext.Vk.GetQueryPoolResults(
                    device: _context.Device,
                    queryPool: _vkObject,
                    firstQuery: firstQuery,
                    queryCount: queryCount,
                    dataSize: (queryCount * stride), 
                    pData: ptr,
                    stride: stride,
                    flags: flags
                );
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // No managed resources to dispose
                }

                // Destroy Vulkan query pool
                VulkanContext.Vk.DestroyQueryPool(
                    _context.Device,
                    _vkObject,
                    in VulkanContext.CustomAllocator<VkQueryPool>()
                );
                _disposed = true;
            }
        }

        public void Reset(uint firstQuery = 0, uint queryCount = 1)
        {
            VulkanContext.Vk.ResetQueryPool(_context.Device, _vkObject, firstQuery, queryCount);
        }

    }
}