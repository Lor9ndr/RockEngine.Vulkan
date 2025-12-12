using NLog;

using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;

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
        private readonly Dictionary<Guid, VertexFormat> _vertexFormats = new();
        private readonly Dictionary<Guid, Type> _vertexTypes = new();
        private readonly Dictionary<Guid, uint> _vertexStrides = new();
        private readonly Lock _allocationLock = new();

        // Defragmentation tracking
        private ulong _vertexFragmentationScore;
        private ulong _indexFragmentationScore;
        private const ulong FRAGMENTATION_THRESHOLD = 1024 * 1024; // 1MB
        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

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
                BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.DeviceLocalBit
            );

            _indexBuffer = VkBuffer.Create(
                _context,
                _indexBufferSize,
                BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.DeviceLocalBit
            );
        }

        public async ValueTask<MeshAllocation> AddMeshAsync<T>(Guid meshID, T[] vertices, uint[] indices) where T : unmanaged, IVertex
        {
            uint vertexStride = (uint)Unsafe.SizeOf<T>();
            ulong vertexSize = (ulong)(vertices.Length * vertexStride);
            ulong indexSize = (ulong)(indices.Length * sizeof(uint));

            // Ensure proper alignment for the vertex data
            if (vertexStride % 4 != 0)
            {
                throw new InvalidOperationException($"Vertex stride {vertexStride} is not 4-byte aligned for type {typeof(T).Name}");
            }

            FreeBlock vertexAllocation, indexAllocation;

            lock (_allocationLock)
            {
                // Find and allocate vertex block with proper alignment
                var vertexResult = FindAndAllocateAlignedBlock(_vertexFreeList, vertexSize, vertexStride);
                if (!vertexResult.HasValue)
                {
                    ExpandVertexBuffer(vertexSize);
                    vertexResult = FindAndAllocateAlignedBlock(_vertexFreeList, vertexSize, vertexStride);
                    if (!vertexResult.HasValue)
                    {
                        throw new InvalidOperationException("Failed to allocate vertex buffer space after expansion");
                    }
                }
                vertexAllocation = vertexResult.Value;

                // Verify alignment immediately after allocation
                if (vertexAllocation.Offset % vertexStride != 0)
                {
                    throw new InvalidOperationException($"Critical: Allocated vertex block at {vertexAllocation.Offset} is not aligned to stride {vertexStride}");
                }
             

                // Find and allocate index block
                var indexResult = FindAndAllocateBlock(_indexFreeList, indexSize);
                if (!indexResult.HasValue)
                {
                    ExpandIndexBuffer(indexSize);
                    indexResult = FindAndAllocateBlock(_indexFreeList, indexSize);
                    if (!indexResult.HasValue)
                    {
                        throw new InvalidOperationException("Failed to allocate index buffer space after expansion");
                    }
                }
                indexAllocation = indexResult.Value;

                var allocation = new MeshAllocation(meshID, vertexAllocation, indexAllocation,
                                                  (uint)vertices.Length, (uint)indices.Length);

                _meshAllocations[meshID] = allocation;

                // Store vertex type information
                _vertexTypes[meshID] = typeof(T);
                _vertexStrides[meshID] = vertexStride;
                _vertexFormats[meshID] = new VertexFormat(
                    T.GetBindingDescription(),
                    T.GetAttributeDescriptions()
                );

                _logger.Info($"Mesh {meshID} allocated at vertex offset {vertexAllocation.Offset} (aligned to {vertexStride})");
            }

            return await UploadMeshData(meshID, vertices, indices, vertexSize, indexSize,
                                      vertexAllocation.Offset, indexAllocation.Offset);
        }


        private ulong AlignUp(ulong value, ulong alignment)
        {
            if (alignment == 0)
            {
                return value;
            }

            ulong remainder = value % alignment;
            return remainder == 0 ? value : value + alignment - remainder;
        }
        public uint GetVertexStride(Guid meshID)
        {
            lock (_allocationLock)
            {
                return _vertexStrides.TryGetValue(meshID, out var stride)
                    ? stride
                    : throw new KeyNotFoundException($"Mesh {meshID} not found");
            }
        }


        private async ValueTask<MeshAllocation> UploadMeshData<T>(Guid meshID, T[] vertices, uint[] indices,
             ulong vertexSize, ulong indexSize, ulong vertexOffset, ulong indexOffset) where T : unmanaged, IVertex
        {
            var transferBatch = _context.TransferSubmitContext.CreateBatch();


            // Copy vertex data to staging buffer
            transferBatch.StageToBuffer<T>(vertices, _vertexBuffer, vertexOffset, vertexSize);
            transferBatch.StageToBuffer<uint>(indices, _indexBuffer, indexOffset, indexSize);


            await _context.TransferSubmitContext.FlushSingle(transferBatch, VkFence.CreateNotSignaled(_context));

            return new MeshAllocation(meshID,
                new FreeBlock(vertexOffset, vertexSize),
                new FreeBlock(indexOffset, indexSize),
                (uint)vertices.Length, (uint)indices.Length);
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

                    // Remove from allocations and formats
                    _meshAllocations.Remove(meshID);
                    _vertexFormats.Remove(meshID);

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

        // Functional approach: Direct access to vertex format without type storage
        public VertexInputBindingDescription GetVertexBindingDescription(Guid meshID)
        {
            lock (_allocationLock)
            {
                return _vertexFormats.TryGetValue(meshID, out var format)
                    ? format.BindingDescription
                    : throw new KeyNotFoundException($"Mesh {meshID} not found");
            }
        }

        public VertexInputAttributeDescription[] GetVertexAttributeDescriptions(Guid meshID)
        {
            lock (_allocationLock)
            {
                return _vertexFormats.TryGetValue(meshID, out var format)
                    ? format.AttributeDescriptions
                    : throw new KeyNotFoundException($"Mesh {meshID} not found");
            }
        }

        // Functional programming style: Higher-order function for mesh operations
        public TResult WithMeshFormat<TResult>(Guid meshID, Func<VertexInputBindingDescription, VertexInputAttributeDescription[], TResult> operation)
        {
            lock (_allocationLock)
            {
                if (!_vertexFormats.TryGetValue(meshID, out var format))
                {
                    throw new KeyNotFoundException($"Mesh {meshID} not found");
                }

                return operation(format.BindingDescription, format.AttributeDescriptions);
            }
        }

        // Pipeline configuration helper using functional composition
        public Action<CommandBuffer> CreatePipelineConfigurator(Guid meshID, Action<VertexInputBindingDescription, VertexInputAttributeDescription[]> pipelineSetup)
        {
            return cmd =>
            {
                WithMeshFormat(meshID, (binding, attributes) =>
                {
                    pipelineSetup(binding, attributes);
                    return 0; // Return value doesn't matter for action
                });
            };
        }

        public void ForEachMesh(Action<Guid, MeshAllocation, VertexFormat> action)
        {
            lock (_allocationLock)
            {
                foreach (var (meshId, allocation) in _meshAllocations)
                {
                    if (_vertexFormats.TryGetValue(meshId, out var format))
                    {
                        action(meshId, allocation, format);
                    }
                }
            }
        }
        public async ValueTask DefragmentAsync()
        {
            lock (_allocationLock)
            {
                if (_vertexFragmentationScore < FRAGMENTATION_THRESHOLD &&
                    _indexFragmentationScore < FRAGMENTATION_THRESHOLD)
                {
                    return;
                }

                _vertexFragmentationScore = 0;
                _indexFragmentationScore = 0;
            }

            var newVertexBuffer = VkBuffer.Create(
                _context,
                _vertexBufferSize,
                BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.DeviceLocalBit
            );

            var newIndexBuffer = VkBuffer.Create(
                _context,
                _indexBufferSize,
                BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.DeviceLocalBit
            );

            var transferBatch = _context.TransferSubmitContext.CreateBatch();
            ulong newVertexOffset = 0;
            ulong newIndexOffset = 0;

            var updatedAllocations = new Dictionary<Guid, MeshAllocation>();

            foreach (var allocation in _meshAllocations.Values)
            {
                transferBatch.CopyBuffer(_vertexBuffer, newVertexBuffer, allocation.VertexOffset, newVertexOffset, allocation.VertexSize);
                transferBatch.CopyBuffer(_indexBuffer, newIndexBuffer, allocation.IndexOffset, newIndexOffset, allocation.IndexSize);

                var updatedAllocation = new MeshAllocation(
                    allocation.MeshID,
                    newVertexOffset, allocation.VertexSize,
                    newIndexOffset, allocation.IndexSize,
                    allocation.VertexCount, allocation.IndexCount
                );

                updatedAllocations[allocation.MeshID] = updatedAllocation;

                newVertexOffset += allocation.VertexSize;
                newIndexOffset += allocation.IndexSize;
            }

            await _context.TransferSubmitContext.FlushSingle(transferBatch, VkFence.CreateNotSignaled(_context));

            lock (_allocationLock)
            {
                _context.GraphicsSubmitContext.AddDependency(_vertexBuffer);
                _context.GraphicsSubmitContext.AddDependency(_indexBuffer);


                _vertexBuffer = newVertexBuffer;
                _indexBuffer = newIndexBuffer;

                foreach (var updatedAllocation in updatedAllocations)
                {
                    _meshAllocations[updatedAllocation.Key] = updatedAllocation.Value;
                }

                _vertexFreeList.Clear();
                _vertexFreeList.AddLast(new FreeBlock(newVertexOffset, _vertexBufferSize - newVertexOffset));

                _indexFreeList.Clear();
                _indexFreeList.AddLast(new FreeBlock(newIndexOffset, _indexBufferSize - newIndexOffset));
            }
        }

        private void AddToFreeList(LinkedList<FreeBlock> freeList, ulong offset, ulong size)
        {
            var newBlock = new FreeBlock(offset, size);
            var currentNode = freeList.First;
            LinkedListNode<FreeBlock> insertBefore = null;

            while (currentNode != null && currentNode.Value.Offset < offset)
            {
                insertBefore = currentNode;
                currentNode = currentNode.Next;
            }

            LinkedListNode<FreeBlock> newNode;
            if (insertBefore != null)
            {
                newNode = freeList.AddBefore(insertBefore, newBlock);
            }
            else
            {
                newNode = freeList.AddFirst(newBlock);
            }

            MergeAdjacentBlocks(freeList, newNode);
        }

        private void MergeAdjacentBlocks(LinkedList<FreeBlock> freeList, LinkedListNode<FreeBlock> node)
        {
            var next = node.Next;
            if (next != null && node.Value.Offset + node.Value.Size == next.Value.Offset)
            {
                node.Value = new FreeBlock(node.Value.Offset, node.Value.Size + next.Value.Size);
                freeList.Remove(next);
            }

            var prev = node.Previous;
            if (prev != null && prev.Value.Offset + prev.Value.Size == node.Value.Offset)
            {
                prev.Value = new FreeBlock(prev.Value.Offset, prev.Value.Size + node.Value.Size);
                freeList.Remove(node);
            }
        }
        private FreeBlock? FindAndAllocateAlignedBlock(LinkedList<FreeBlock> freeList, ulong requiredSize, ulong alignment)
        {
            var node = freeList.First;
            while (node != null)
            {
                var block = node.Value;

                // Calculate aligned offset within this block
                ulong alignedOffset = AlignUp(block.Offset, alignment);
                ulong alignedEnd = alignedOffset + requiredSize;

                // Verify the final alignment
                ulong finalMod = alignedOffset % alignment;

                if (finalMod != 0)
                {
                    _logger.Error($"Alignment failed! alignedOffset={alignedOffset} is not divisible by {alignment}");
                }

                // Debug logging
                _logger.Debug($"FindAndAllocateAlignedBlock: block=({block.Offset}, {block.Size}), " +
                                 $"alignedOffset={alignedOffset}, alignedEnd={alignedEnd}, " +
                                 $"requiredSize={requiredSize}, alignment={alignment}");

                // Check if the aligned allocation fits in this block
                if (alignedEnd <= block.Offset + block.Size)
                {
                    // Remove the original block from free list
                    freeList.Remove(node);

                    // If there's a gap before the aligned offset, add it back to free list
                    if (alignedOffset > block.Offset)
                    {
                        ulong frontGapSize = alignedOffset - block.Offset;
                        _logger.Debug($"  Adding front gap: ({block.Offset}, {frontGapSize})");
                        AddToFreeList(freeList, block.Offset, frontGapSize);
                    }

                    // If there's a gap after the allocation, add it back to free list
                    ulong remainingSizeAfterAllocation = (block.Offset + block.Size) - alignedEnd;
                    if (remainingSizeAfterAllocation > 0)
                    {
                        _logger.Debug($"  Adding back gap: ({alignedEnd}, {remainingSizeAfterAllocation})");
                        AddToFreeList(freeList, alignedEnd, remainingSizeAfterAllocation);
                    }

                    _logger.Debug($"  Allocated: ({alignedOffset}, {requiredSize})");

                    // Return the allocated block
                    return new FreeBlock(alignedOffset, requiredSize);
                }
                node = node.Next;
            }

            _logger.Debug($"FindAndAllocateAlignedBlock: No suitable block found");
            return null;
        }

        private FreeBlock? FindAndAllocateBlock(LinkedList<FreeBlock> freeList, ulong requiredSize)
        {
            var node = freeList.First;
            while (node != null)
            {
                var block = node.Value;

                if (block.Size >= requiredSize)
                {
                    // Remove the block from free list
                    freeList.Remove(node);

                    // If the block is larger than needed, add the remainder back to free list
                    if (block.Size > requiredSize)
                    {
                        ulong remainderOffset = block.Offset + requiredSize;
                        ulong remainderSize = block.Size - requiredSize;
                        AddToFreeList(freeList, remainderOffset, remainderSize);
                    }

                    // Return the allocated block
                    return new FreeBlock(block.Offset, requiredSize);
                }
                node = node.Next;
            }
            return null;
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
                BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.DeviceLocalBit
            );

            var transferBatch = _context.TransferSubmitContext.CreateBatch();
            transferBatch.CopyBuffer(_vertexBuffer, newBuffer, 0, 0, _vertexBufferSize);

            AddToFreeList(_vertexFreeList, _vertexBufferSize, newSize - _vertexBufferSize);

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
                BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.DeviceLocalBit
            );

            var transferBatch = _context.TransferSubmitContext.CreateBatch();
            transferBatch.CopyBuffer(_indexBuffer, newBuffer, 0, 0, _indexBufferSize);

            AddToFreeList(_indexFreeList, _indexBufferSize, newSize - _indexBufferSize);

            _indexBuffer.Dispose();
            _indexBuffer = newBuffer;
            _indexBufferSize = newSize;
        }

     

        public void Bind(UploadBatch batch)
        {
            _vertexBuffer.BindVertexBuffer(batch);
            _indexBuffer.BindIndexBuffer(batch, 0, IndexType.Uint32);
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

        public readonly record struct FreeBlock(ulong Offset, ulong Size);
        

        public readonly record struct MeshAllocation(
            Guid MeshID,
            ulong VertexOffset,
            ulong VertexSize,
            ulong IndexOffset,
            ulong IndexSize,
            uint VertexCount,
            uint IndexCount
        )
        {
            public MeshAllocation(Guid meshID, FreeBlock vertexBlock, FreeBlock indexBlock, uint vertexCount, uint indexCount)
                : this(meshID, vertexBlock.Offset, vertexBlock.Size, indexBlock.Offset, indexBlock.Size, vertexCount, indexCount)
            {
            }
        }

        public readonly record struct VertexFormat(
            VertexInputBindingDescription BindingDescription,
            VertexInputAttributeDescription[] AttributeDescriptions
        );
    }
}