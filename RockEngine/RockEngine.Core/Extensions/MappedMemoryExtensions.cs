using RockEngine.Vulkan;

using System.Runtime.InteropServices;

namespace RockEngine.Core.Extensions
{
    public static class MappedMemoryExtensions
    {
        public static unsafe void WriteStrided<T>(this MappedMemory memory, int index, ulong stride, in T value)
            where T : unmanaged
        {
            // Validate that stride >= sizeof(T) and fits within the mapped range
            ulong offset = (ulong)index * stride;
            var span = memory.GetSpan().Slice((int)offset, sizeof(T));
            MemoryMarshal.Write(span, in value);
        }
    }
}
