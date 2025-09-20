using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Buffers
{
    public sealed class GlobalGeometryBuffer : IDisposable
    {
        private readonly VulkanContext _context;
        private VkBuffer _vertexBuffer;
        private VkBuffer _indexBuffer;
        private ulong _vertexBufferSize;
        private ulong _indexBufferSize;

        // Free list management
        private readonly LinkedList<FreeBlock> _vertexFreeList = new();
        private readonly LinkedList<FreeBlock> _indexFreeList = new();
        private readonly Dictionary<Guid, MeshAllocation> _meshAllocations = new();
        private readonly Lock _allocationLock = new();

        // Defragmentation tracking
        private ulong _vertexFragmentationScore;
        private ulong _indexFragmentationScore;
        private const ulong FRAGMENTATION_THRESHOLD = 1024 * 1024; // 1MB

        public GlobalGeometryBuffer(VulkanContext context, ulong initialVertexSize = 64 * 1024 * 1024,
                                   ulong initialIndexSize = 16 * 1024 * 1024)
        {
            _context = context;

            // Initialize with empty free blocks covering the entire buffer
            _vertexFreeList.AddLast(new FreeBlock(0, initialVertexSize));
            _indexFreeList.AddLast(new FreeBlock(0, initialIndexSize));

            _vertexBufferSize = initialVertexSize;
            _indexBufferSize = initialIndexSize;

            // Create initial buffers
            _vertexBuffer = VkBuffer.Create(
                _context,
                _vertexBufferSize,
                BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.DeviceLocalBit
            );

            _indexBuffer = VkBuffer.Create(
                _context,
                _indexBufferSize,
                BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.DeviceLocalBit
            );
        }

        public async ValueTask<MeshAllocation> AddMeshAsync(Guid meshID, Vertex[] vertices, uint[] indices)
        {
            ulong vertexSize = (ulong)(vertices.Length * Marshal.SizeOf<Vertex>());
            ulong indexSize = (ulong)(indices.Length * sizeof(uint));

            // Try to find space in free lists
            var vertexBlock = FindFreeBlock(_vertexFreeList, vertexSize);
            var indexBlock = FindFreeBlock(_indexFreeList, indexSize);

            // If no suitable free blocks, expand buffers
            if (vertexBlock == null)
            {
                ExpandVertexBuffer(vertexSize);
                vertexBlock = FindFreeBlock(_vertexFreeList, vertexSize);
            }

            if (indexBlock == null)
            {
                ExpandIndexBuffer(indexSize);
                indexBlock = FindFreeBlock(_indexFreeList, indexSize);
            }

            // Allocate from free blocks
            var vertexAllocation = AllocateFromBlock(_vertexFreeList, vertexBlock!.Value, vertexSize);
            var indexAllocation = AllocateFromBlock(_indexFreeList, indexBlock!.Value, indexSize);

            // Create mesh allocation
            var allocation = new MeshAllocation(meshID, vertexAllocation, indexAllocation,
                                              (uint)vertices.Length, (uint)indices.Length);
            _meshAllocations[meshID] = allocation;

            return await UploadMeshData(allocation, vertices, indices);
        }

        private async ValueTask<MeshAllocation> UploadMeshData(MeshAllocation allocation, Vertex[] vertices, uint[] indices)
        {
            var transferBatch = _context.TransferSubmitContext.CreateBatch();

            // Stage vertex data
            transferBatch.StageToBuffer(
                vertices.AsSpan(),
                _vertexBuffer,
                allocation.VertexOffset,
                allocation.VertexSize
            );

            // Stage index data
            transferBatch.StageToBuffer(
                indices.AsSpan(),
                _indexBuffer,
                allocation.IndexOffset,
                allocation.IndexSize
            );

            await _context.TransferSubmitContext.FlushSingle(transferBatch, VkFence.CreateNotSignaled(_context));

            return allocation;
        }

        public void RemoveMesh(Guid meshID)
        {
            lock (_allocationLock)
            {
                if (_meshAllocations.TryGetValue(meshID, out var allocation))
                {
                    // Add freed blocks back to free lists
                    AddToFreeList(_vertexFreeList, allocation.VertexOffset, allocation.VertexSize);
                    AddToFreeList(_indexFreeList, allocation.IndexOffset, allocation.IndexSize);

                    // Remove from allocations
                    _meshAllocations.Remove(meshID);

                    // Update fragmentation scores
                    _vertexFragmentationScore += allocation.VertexSize;
                    _indexFragmentationScore += allocation.IndexSize;

                    // Check if defragmentation is needed
                    if (_vertexFragmentationScore > FRAGMENTATION_THRESHOLD ||
                        _indexFragmentationScore > FRAGMENTATION_THRESHOLD)
                    {
                        _ = DefragmentAsync(); // Fire and forget
                    }
                }
            }
        }

        public async ValueTask DefragmentAsync()
        {
            lock (_allocationLock)
            {
                // Don't defragment if not needed
                if (_vertexFragmentationScore < FRAGMENTATION_THRESHOLD &&
                    _indexFragmentationScore < FRAGMENTATION_THRESHOLD)
                {
                    return;
                }

                // Reset fragmentation scores
                _vertexFragmentationScore = 0;
                _indexFragmentationScore = 0;
            }

            // Create new compacted buffers
            var newVertexBuffer = VkBuffer.Create(
                _context,
                _vertexBufferSize,
                BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.DeviceLocalBit
            );

            var newIndexBuffer = VkBuffer.Create(
                _context,
                _indexBufferSize,
                BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.DeviceLocalBit
            );

            // Copy all active meshes to new buffers
            var transferBatch = _context.TransferSubmitContext.CreateBatch();
            ulong newVertexOffset = 0;
            ulong newIndexOffset = 0;

            var updatedAllocations = new Dictionary<Guid, MeshAllocation>();

            foreach (var allocation in _meshAllocations.Values)
            {
                // Copy vertex data
                transferBatch.CopyBuffer(
                    _vertexBuffer, newVertexBuffer,
                    allocation.VertexOffset, newVertexOffset,
                    allocation.VertexSize
                );

                // Copy index data
                transferBatch.CopyBuffer(
                    _indexBuffer, newIndexBuffer,
                    allocation.IndexOffset, newIndexOffset,
                    allocation.IndexSize
                );

                // Update allocation with new offsets
                var updatedAllocation = new MeshAllocation(
                    allocation.MeshId,
                    newVertexOffset, allocation.VertexSize,
                    newIndexOffset, allocation.IndexSize,
                    allocation.VertexCount, allocation.IndexCount
                );

                updatedAllocations[allocation.MeshId] = updatedAllocation;

                // Update offsets
                newVertexOffset += allocation.VertexSize;
                newIndexOffset += allocation.IndexSize;
            }

            await _context.TransferSubmitContext.FlushSingle(transferBatch, VkFence.CreateNotSignaled(_context));

            // Swap buffers
            lock (_allocationLock)
            {
                _vertexBuffer.Dispose();
                _indexBuffer.Dispose();

                _vertexBuffer = newVertexBuffer;
                _indexBuffer = newIndexBuffer;

                // Update allocations
                foreach (var updatedAllocation in updatedAllocations)
                {
                    _meshAllocations[updatedAllocation.Key] = updatedAllocation.Value;
                }

                // Rebuild free lists with single free block at the end
                _vertexFreeList.Clear();
                _vertexFreeList.AddLast(new FreeBlock(newVertexOffset, _vertexBufferSize - newVertexOffset));

                _indexFreeList.Clear();
                _indexFreeList.AddLast(new FreeBlock(newIndexOffset, _indexBufferSize - newIndexOffset));
            }
        }

        private FreeBlock? FindFreeBlock(LinkedList<FreeBlock> freeList, ulong requiredSize)
        {
            // First-fit algorithm
            var node = freeList.First;
            while (node != null)
            {
                if (node.Value.Size >= requiredSize)
                {
                    return node.Value;
                }
                node = node.Next;
            }
            return null;
        }

        private FreeBlock AllocateFromBlock(LinkedList<FreeBlock> freeList, FreeBlock block, ulong size)
        {
            // Find the node containing this block
            var node = freeList.Find(block) ?? throw new InvalidOperationException("Block not found in free list");

            // Remove the block from free list
            freeList.Remove(node);

            // If block is larger than needed, add remainder back to free list
            if (block.Size > size)
            {
                var remainder = new FreeBlock(block.Offset + size, block.Size - size);
                freeList.AddLast(remainder);
            }

            return new FreeBlock(block.Offset, size);
        }

        private void AddToFreeList(LinkedList<FreeBlock> freeList, ulong offset, ulong size)
        {
            var newBlock = new FreeBlock(offset, size);
            var currentNode = freeList.First;
            LinkedListNode<FreeBlock> insertBefore = null;

            // Find insertion point to keep list sorted by offset
            while (currentNode != null && currentNode.Value.Offset < offset)
            {
                insertBefore = currentNode;
                currentNode = currentNode.Next;
            }

            // Insert the new block
            LinkedListNode<FreeBlock> newNode;
            if (insertBefore != null)
            {
                newNode = freeList.AddBefore(insertBefore, newBlock);
            }
            else
            {
                newNode = freeList.AddFirst(newBlock);
            }

            // Merge with adjacent blocks if possible
            MergeAdjacentBlocks(freeList, newNode);
        }

        private void MergeAdjacentBlocks(LinkedList<FreeBlock> freeList, LinkedListNode<FreeBlock> node)
        {
            // Try to merge with next block
            var next = node.Next;
            if (next != null && node.Value.Offset + node.Value.Size == next.Value.Offset)
            {
                node.Value = new FreeBlock(node.Value.Offset, node.Value.Size + next.Value.Size);
                freeList.Remove(next);
            }

            // Try to merge with previous block
            var prev = node.Previous;
            if (prev != null && prev.Value.Offset + prev.Value.Size == node.Value.Offset)
            {
                prev.Value = new FreeBlock(prev.Value.Offset, prev.Value.Size + node.Value.Size);
                freeList.Remove(node);
            }
        }

        private void ExpandVertexBuffer(ulong additionalSize)
        {
            var newSize = _vertexBufferSize * 2;
            while (newSize - _vertexBufferSize < additionalSize)
            {
                newSize *= 2;
            }

            var newBuffer = VkBuffer.Create(
                _context,
                newSize,
                BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.DeviceLocalBit
            );

            // Copy existing data to new buffer
            var transferBatch = _context.TransferSubmitContext.CreateBatch();
            transferBatch.CopyBuffer(_vertexBuffer, newBuffer, 0, 0, _vertexBufferSize);

            // Add the new free space to the free list
            AddToFreeList(_vertexFreeList, _vertexBufferSize, newSize - _vertexBufferSize);

            // Replace the old buffer
            _vertexBuffer.Dispose();
            _vertexBuffer = newBuffer;
            _vertexBufferSize = newSize;
        }

        private void ExpandIndexBuffer(ulong additionalSize)
        {
            var newSize = _indexBufferSize * 2;
            while (newSize - _indexBufferSize < additionalSize)
            {
                newSize *= 2;
            }

            var newBuffer = VkBuffer.Create(
                _context,
                newSize,
                BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.DeviceLocalBit
            );

            // Copy existing data to new buffer
            var transferBatch = _context.TransferSubmitContext.CreateBatch();
            transferBatch.CopyBuffer(_indexBuffer, newBuffer, 0, 0, _indexBufferSize);

            // Add the new free space to the free list
            AddToFreeList(_indexFreeList, _indexBufferSize, newSize - _indexBufferSize);

            // Replace the old buffer
            _indexBuffer.Dispose();
            _indexBuffer = newBuffer;
            _indexBufferSize = newSize;
        }

        public void BindVertexBuffer(VkCommandBuffer cmd)
        {
            _vertexBuffer.BindVertexBuffer(cmd);
        }

        public void BindIndexBuffer(VkCommandBuffer cmd)
        {
            _indexBuffer.BindIndexBuffer(cmd, 0, IndexType.Uint32);
        }

        public MeshAllocation GetMeshAllocation(Guid meshId)
        {
            lock (_allocationLock)
            {
                return _meshAllocations[meshId];
            }
        }

        public void Dispose()
        {
            _vertexBuffer?.Dispose();
            _indexBuffer?.Dispose();
        }

        public struct FreeBlock
        {
            public ulong Offset;
            public ulong Size;

            public FreeBlock(ulong offset, ulong size)
            {
                Offset = offset;
                Size = size;
            }

            public override bool Equals(object? obj)
            {
                return obj is FreeBlock block &&
                       Offset == block.Offset &&
                       Size == block.Size;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Offset, Size);
            }
            public static bool operator ==(FreeBlock left, FreeBlock right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(FreeBlock left, FreeBlock right)
            {
                return !(left == right);
            }
        }

        public struct MeshAllocation
        {
            public Guid MeshId;
            public ulong VertexOffset;
            public ulong VertexSize;
            public ulong IndexOffset;
            public ulong IndexSize;
            public uint VertexCount;
            public uint IndexCount;

            public MeshAllocation(Guid meshId, FreeBlock vertexBlock, FreeBlock indexBlock,
                                uint vertexCount, uint indexCount)
            {
                MeshId = meshId;
                VertexOffset = vertexBlock.Offset;
                VertexSize = vertexBlock.Size;
                IndexOffset = indexBlock.Offset;
                IndexSize = indexBlock.Size;
                VertexCount = vertexCount;
                IndexCount = indexCount;
            }

            public MeshAllocation(Guid meshId, ulong vertexOffset, ulong vertexSize,
                                ulong indexOffset, ulong indexSize,
                                uint vertexCount, uint indexCount)
            {
                MeshId = meshId;
                VertexOffset = vertexOffset;
                VertexSize = vertexSize;
                IndexOffset = indexOffset;
                IndexSize = indexSize;
                VertexCount = vertexCount;
                IndexCount = indexCount;
            }
        }
    }
}