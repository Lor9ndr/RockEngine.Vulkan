using Microsoft.Extensions.ObjectPool;

using NLog;

using RockEngine.Core.DI;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Vulkan;

using Silk.NET.Core;
using Silk.NET.Vulkan;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using static RockEngine.Core.Rendering.Buffers.GlobalGeometryBuffer;

namespace RockEngine.Core.Rendering.Managers
{
    public sealed class IndirectCommandManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly IndirectBuffer _indirectBuffer;
        private readonly Bool32 _supportsMultiDraw;
        private readonly List<MeshRenderCommand> _commands = new List<MeshRenderCommand>();
        private readonly Queue<IRenderCommand> _otherCommands = new Queue<IRenderCommand>();
        private readonly Dictionary<string, List<DrawGroup>> _drawGroupsBySubpass = new();
        private readonly Lock _otherCommandsLock = new Lock();
        private readonly uint _transformBufferCapacity;
        private bool _isDirty;
        private TransformManager _transformManager;
        private static readonly ulong _commandStride = (ulong)Marshal.SizeOf<DrawIndexedIndirectCommand>();
        private readonly List<DrawIndexedIndirectCommand> _indirectCommandsList = new List<DrawIndexedIndirectCommand>();

        private static readonly ObjectPool<List<DrawGroup>> DrawGroupPool = ObjectPool.Create<List<DrawGroup>>(new ListPolicy<DrawGroup>());

        public IndirectBuffer IndirectBuffer => _indirectBuffer;
        public Queue<IRenderCommand> OtherCommands => _otherCommands;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public IndirectCommandManager(VulkanContext context, uint transformBufferCapacity, TransformManager transformManager)
        {
            _context = context;
            _transformBufferCapacity = transformBufferCapacity;
            _indirectBuffer = new IndirectBuffer(context, 10_000 * _commandStride);
            _supportsMultiDraw = context.Device.PhysicalDevice.Features2.Features.MultiDrawIndirect;
            _transformManager = transformManager;

        }
      

        public void AddMesh(MeshRenderer mesh, uint transformIndex)
        {
            if (transformIndex < 0 || transformIndex >= _transformBufferCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(transformIndex), "Transform index exceeds buffer capacity");
            }

            // Check which subpasses this mesh's material participates in
            var material = mesh.Material;
            foreach (var subpassName in material.Passes.Keys)
            {
                _commands.Add(new MeshRenderCommand(mesh, transformIndex, subpassName));
            }

            _isDirty = true;
        }


        /// <summary>
        /// Removes mesh from commands
        /// </summary>
        /// <param name="meshRenderer">remove by meshRenderer</param>
        /// <returns>transform index</returns>
        public uint RemoveMesh(MeshRenderer meshRenderer)
        {
            var command = _commands.First(s=>s.Mesh == meshRenderer);
            _commands.RemoveAll(s => s.Mesh == meshRenderer);
            _isDirty = true;
            return command.TransformIndex;
        }

        public async ValueTask UpdateAsync()
        {
            if (!_isDirty)
            {
                return;
            }

            _commands.Sort(MeshRenderCommandComparer.Default);
            Span<MeshRenderCommand> commandsSpan = CollectionsMarshal.AsSpan(_commands);

            if (commandsSpan.Length == 0)
            {
                _isDirty = false;
                return;
            }

            _indirectCommandsList.Clear();
             _drawGroupsBySubpass.Clear(); 

            _indirectCommandsList.Capacity = Math.Max(_indirectCommandsList.Capacity, commandsSpan.Length);

            int groupStartIndex = 0;
            for (int i = 1; i < commandsSpan.Length; i++)
            {
                if (IsNewGroup(in commandsSpan[i - 1], in commandsSpan[i]))
                {
                    AddGroup(commandsSpan[groupStartIndex..i]);
                    groupStartIndex = i;
                }
            }
            AddGroup(commandsSpan[groupStartIndex..]);

            var batch = _context.GraphicsSubmitContext.CreateBatch();
            batch.LabelObject("IndirectDrawManager cmd");
            _indirectBuffer.StageCommands(batch, CollectionsMarshal.AsSpan(_indirectCommandsList));

            var bufferBarrier = new BufferMemoryBarrier
            {
                SType = StructureType.BufferMemoryBarrier,
                SrcAccessMask = AccessFlags.TransferWriteBit,
                DstAccessMask = AccessFlags.IndirectCommandReadBit,
                Buffer = _indirectBuffer.Buffer,
                Offset = 0,
                Size = Vk.WholeSize
            };

            batch.PipelineBarrier(
                srcStage: PipelineStageFlags.TransferBit,
                dstStage: PipelineStageFlags.DrawIndirectBit,
                bufferMemoryBarriers: new[] { bufferBarrier }
            );
            batch.Submit();


            _isDirty = false;
            return;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsNewGroup(in MeshRenderCommand a, in MeshRenderCommand b)
        {
            // Fast path: check subpass first
            if (a.SubpassName != b.SubpassName) return true;

            // Then check if materials have the same pipeline for this subpass
            var aPass = a.Mesh.Material.GetPass(a.SubpassName);
            var bPass = b.Mesh.Material.GetPass(b.SubpassName);

            return !ReferenceEquals(aPass?.Pipeline, bPass?.Pipeline);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool PipelineRequirementsMatch(Material a, Material b, string subpassName)
        {
            var aPass = a.GetPass(subpassName);
            var bPass = b.GetPass(subpassName);

            if (aPass == null || bPass == null) return false;

            return ReferenceEquals(aPass.Pipeline, bPass.Pipeline);
        }

        private void AddGroup(Span<MeshRenderCommand> groupSpan)
        {
            // Group by the criteria that determines when we can batch
            var batchGroups = new Dictionary<(string Subpass, Material Material, IMesh Mesh), List<MeshRenderCommand>>();

            foreach (ref readonly var cmd in groupSpan)
            {
                var key = (cmd.SubpassName, cmd.Mesh.Material, cmd.Mesh.Mesh);
                if (!batchGroups.TryGetValue(key, out var batchGroup))
                {
                    batchGroup = new List<MeshRenderCommand>();
                    batchGroups[key] = batchGroup;
                }
                batchGroup.Add(cmd);
            }

            if (!_drawGroupsBySubpass.TryGetValue(groupSpan[0].SubpassName, out var drawGroups))
            {
                drawGroups = new List<DrawGroup>();
                _drawGroupsBySubpass[groupSpan[0].SubpassName] = drawGroups;
            }

            var globalGeometryBuffer = IoC.Container.GetInstance<GlobalGeometryBuffer>();

            foreach (var (key, batchGroup) in batchGroups)
            {
                var (subpassName, material, mesh) = key;
                var materialPass = material.GetPass(subpassName);

                if (materialPass == null)
                {
                    _logger.Warn($"Material '{material.Name}' has no pass for subpass '{subpassName}'");
                    continue;
                }

                var allocation = globalGeometryBuffer.GetMeshAllocation(mesh.ID);
                var vertexStride = globalGeometryBuffer.GetVertexStride(allocation.MeshID);

                if (allocation.VertexOffset % vertexStride != 0)
                {
                    _logger.Error($"Vertex offset {allocation.VertexOffset} is not aligned to vertex stride {vertexStride} for mesh {allocation.MeshID}");
                    continue;
                }

                uint vertexOffsetInVertices = (uint)(allocation.VertexOffset / vertexStride);
                uint firstIndex = (uint)(allocation.IndexOffset / sizeof(uint));

                // Get consecutive indices for this mesh group from TransformManager
                var consecutiveIndices = _transformManager.GetConsecutiveIndicesForMeshGroup(material, mesh);
                bool areTransformsConsecutive = _transformManager.AreMeshGroupIndicesConsecutive(material, mesh);

                // Sort batch group by transform index to match the consecutive order
                batchGroup.Sort((a, b) => a.TransformIndex.CompareTo(b.TransformIndex));

                if (_supportsMultiDraw && batchGroup.Count > 1 && areTransformsConsecutive)
                {
                    // Single multi-draw command for all instances
                    drawGroups.Add(new DrawGroup(
                        materialPass,
                        batchGroup[0].Mesh,
                        (uint)batchGroup.Count,
                        (ulong)_indirectCommandsList.Count * _commandStride,
                        true
                    ));

                    _indirectCommandsList.Add(new DrawIndexedIndirectCommand
                    {
                        IndexCount = allocation.IndexCount,
                        InstanceCount = (uint)batchGroup.Count,
                        FirstIndex = firstIndex,
                        VertexOffset = (int)vertexOffsetInVertices,
                        FirstInstance = (uint)consecutiveIndices[0] // Use first consecutive index
                    });

                    _logger.Debug($"Created multi-draw: {batchGroup.Count} instances of mesh {mesh.ID} with material {material.Name}, firstInstance={consecutiveIndices[0]}");
                }
                else
                {
                    // Multiple individual draws
                    CreateIndividualDraws(drawGroups, materialPass, batchGroup, allocation,
                        firstIndex, vertexOffsetInVertices);
                }
            }

            _logger.Info($"Processed {batchGroups.Count} batch groups with total {_indirectCommandsList.Count} indirect commands");

            // Log batching statistics
            foreach (var batch in batchGroups.Where(b => b.Value.Count > 1))
            {
                var areConsecutive = _transformManager.AreMeshGroupIndicesConsecutive(batch.Key.Material, batch.Key.Mesh);
                _logger.Info($"Batch: {batch.Value.Count} instances of mesh {batch.Key.Mesh.ID}, consecutive={areConsecutive}");
            }
        }

        private void CreateIndividualDraws(List<DrawGroup> drawGroups, MaterialPass materialPass,
            List<MeshRenderCommand> batchGroup, MeshAllocation allocation,
            uint firstIndex, uint vertexOffsetInVertices)
        {
            ulong byteOffset = (ulong)_indirectCommandsList.Count * _commandStride;

            drawGroups.Add(new DrawGroup(
                materialPass,
                batchGroup[0].Mesh,
                (uint)batchGroup.Count,
                byteOffset,
                false
            ));

            foreach (var cmd in batchGroup)
            {
                _indirectCommandsList.Add(new DrawIndexedIndirectCommand
                {
                    IndexCount = allocation.IndexCount,
                    InstanceCount = 1,
                    FirstIndex = firstIndex,
                    VertexOffset = (int)vertexOffsetInVertices,
                    FirstInstance = cmd.TransformIndex
                });
            }

            _logger.Debug($"Created {batchGroup.Count} individual draws for mesh {batchGroup[0].Mesh.Mesh.ID}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool AreTransformIndicesConsecutive(ReadOnlySpan<MeshRenderCommand> commands)
        {
            if (commands.Length <= 1) return true;

            uint expected = commands[0].TransformIndex + 1;
            for (int i = 1; i < commands.Length; i++)
            {
                if (commands[i].TransformIndex != expected) return false;
                expected++;
            }
            return true;
        }


        public List<DrawGroup> GetDrawGroups(string subpassName)
        {
            return _drawGroupsBySubpass.TryGetValue(subpassName, out var groups)
                ? groups.ToList()
                : new List<DrawGroup>();
        }

        public void Dispose() => _indirectBuffer.Dispose();

        public void AddCommand(IRenderCommand command)
        {
            lock (_otherCommandsLock)
            {
                _otherCommands.Enqueue(command);
            }
        }

        public bool TryDequeue(out IRenderCommand command)
        {
            lock (_otherCommandsLock)
            {
                return _otherCommands.TryDequeue(out command);
            }
        }

        private readonly struct MeshRenderCommand
        {
            public readonly MeshRenderer Mesh;
            public readonly uint TransformIndex;
            public readonly string SubpassName;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public MeshRenderCommand(MeshRenderer mesh, uint transformIndex, string subpassName)
            {
                Mesh = mesh;
                TransformIndex = transformIndex;
                SubpassName = subpassName;
            }
        }

        private sealed class MeshRenderCommandComparer : IComparer<MeshRenderCommand>
        {
            public static readonly MeshRenderCommandComparer Default = new();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int Compare(MeshRenderCommand x, MeshRenderCommand y)
            {
                // Sort by subpass name first
                int subpassCompare = string.Compare(x.SubpassName, y.SubpassName, StringComparison.Ordinal);
                if (subpassCompare != 0)
                {
                    return subpassCompare;
                }

                // Then by pipeline within the subpass (this is the main batching criteria)
                var xPass = x.Mesh.Material.GetPass(x.SubpassName);
                var yPass = y.Mesh.Material.GetPass(y.SubpassName);

                // Use pipeline handle for comparison
                long pipelineCompare = xPass.Pipeline.VkPipeline.VkObjectNative.Handle
                    .CompareTo(yPass.Pipeline.VkPipeline.VkObjectNative.Handle);
                if (pipelineCompare != 0)
                {
                    return pipelineCompare > 0 ? 1 : -1;
                }

                // Then by material (less important for batching)
                int materialCompare = x.Mesh.Material.GetHashCode()
                    .CompareTo(y.Mesh.Material.GetHashCode());
                if (materialCompare != 0)
                {
                    return materialCompare;
                }

                // Finally by mesh - this allows multiple instances of the same mesh to batch
                return x.Mesh.Mesh.ID.CompareTo(y.Mesh.Mesh.ID);
            }
        }

        public readonly record struct DrawGroup(
             MaterialPass MaterialPass,
             MeshRenderer MeshRenderer,
             uint Count,
             ulong ByteOffset,
             bool IsMultiDraw);
    }
    public class ListPolicy<T> : IPooledObjectPolicy<List<T>>
    {
        public List<T> Create() => [];

        public bool Return(List<T> obj)
        {
            obj.Clear(); // чистим список перед возвратом
            return true;
        }
    }
}