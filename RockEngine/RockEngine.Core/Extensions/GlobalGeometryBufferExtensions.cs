using RockEngine.Core.Builders;
using RockEngine.Core.Rendering.Buffers;

using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;

namespace RockEngine.Core.Extensions
{
    public static class GlobalGeometryBufferExtensions
    {
        // Functional composition: Chain operations
        public static GlobalGeometryBuffer Tap(this GlobalGeometryBuffer buffer, Action<GlobalGeometryBuffer> action)
        {
            action(buffer);
            return buffer;
        }

      

        // Higher-order function for batch processing
        public static void ProcessMeshesByVertexType<TVertex>(
            this GlobalGeometryBuffer buffer,
            Action<Guid, GlobalGeometryBuffer.MeshAllocation> processor)
            where TVertex : unmanaged, IVertex
        {
            buffer.ForEachMesh((meshId, allocation, format) =>
            {
                // Check if format matches TVertex (simplified check by stride)
                if (format.BindingDescription.Stride == (uint)Unsafe.SizeOf<TVertex>())
                {
                    processor(meshId, allocation);
                }
            });
        }

        // LINQ-style aggregation
        public static ulong GetTotalVertexCount(this GlobalGeometryBuffer buffer)
        {
            ulong total = 0;
            buffer.ForEachMesh((_, allocation, _) => total += allocation.VertexCount);
            return total;
        }

        public static ulong GetTotalIndexCount(this GlobalGeometryBuffer buffer)
        {
            ulong total = 0;
            buffer.ForEachMesh((_, allocation, _) => total += allocation.IndexCount);
            return total;
        }

        // Memoization helper for expensive operations
        public static Func<Guid, VertexInputAttributeDescription[]> CreateAttributeMemoizer(
            this GlobalGeometryBuffer buffer)
        {
            var cache = new Dictionary<Guid, VertexInputAttributeDescription[]>();
            return meshID =>
            {
                if (!cache.TryGetValue(meshID, out var attributes))
                {
                    attributes = buffer.GetVertexAttributeDescriptions(meshID);
                    cache[meshID] = attributes;
                }
                return attributes;
            };
        }
    }
}