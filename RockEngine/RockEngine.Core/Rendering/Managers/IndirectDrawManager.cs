using NLog;

using RockEngine.Core.DI;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Vulkan;

using Silk.NET.Core;
using Silk.NET.Vulkan;

using SkiaSharp;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
        private static readonly ulong _commandStride = (ulong)Marshal.SizeOf<DrawIndexedIndirectCommand>();
        private readonly List<DrawIndexedIndirectCommand> _indirectCommandsList = new List<DrawIndexedIndirectCommand>();

        public IndirectBuffer IndirectBuffer => _indirectBuffer;
        public Queue<IRenderCommand> OtherCommands => _otherCommands;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public IndirectCommandManager(VulkanContext context, uint transformBufferCapacity)
        {
            _context = context;
            _transformBufferCapacity = transformBufferCapacity;
            _indirectBuffer = new IndirectBuffer(context, 10_000 * _commandStride);
            _supportsMultiDraw = context.Device.PhysicalDevice.Features2.Features.MultiDrawIndirect;
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
                _commands.Add(new MeshRenderCommand(mesh, (uint)transformIndex, subpassName));
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
            // DON'T clear _drawGroupsBySubpass here - only clear per-subpass lists as needed
             _drawGroupsBySubpass.Clear(); // REMOVE THIS LINE

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
            batch.CommandBuffer.LabelObject("IndirectDrawManager cmd");
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

            await _context.GraphicsSubmitContext.FlushSingle(batch, VkFence.CreateNotSignaled(_context));

            _isDirty = false;
            return;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsNewGroup(in MeshRenderCommand a, in MeshRenderCommand b)
        {
            return a.SubpassName != b.SubpassName ||
                   !ReferenceEquals(a.Mesh.Material, b.Mesh.Material) ||
                   !ReferenceEquals(a.Mesh.Mesh, b.Mesh.Mesh); // Compare the actual mesh, not the renderer
        }

        private void AddGroup(Span<MeshRenderCommand> groupSpan)
        {
            groupSpan.Sort((a, b) => a.TransformIndex.CompareTo(b.TransformIndex));

            ref readonly var firstCmd = ref groupSpan[0];
            var subpassName = firstCmd.SubpassName;

            // Group by material AND mesh to ensure correct geometry
            var meshGroups = new Dictionary<(Material, IMesh), List<MeshRenderCommand>>();

            foreach (ref readonly var cmd in groupSpan)
            {
                var key = (cmd.Mesh.Material, cmd.Mesh.Mesh);
                if (!meshGroups.TryGetValue(key, out var meshGroup))
                {
                    meshGroup = new List<MeshRenderCommand>();
                    meshGroups[key] = meshGroup;
                }
                meshGroup.Add(cmd);
            }

            if (!_drawGroupsBySubpass.TryGetValue(subpassName, out var drawGroups))
            {
                drawGroups = new List<DrawGroup>();
                _drawGroupsBySubpass[subpassName] = drawGroups;
            }

            var globalGeometryBuffer = IoC.Container.GetInstance<GlobalGeometryBuffer>();

            foreach (var (key, meshGroup) in meshGroups)
            {
                var (material, mesh) = key;
                var materialPass = material.GetPass(subpassName);

                // Ensure we have a valid material pass
                if (materialPass == null)
                {
                    _logger.Warn($"Material '{material.Name}' has no pass for subpass '{subpassName}'");
                    continue;
                }

                var allocation = globalGeometryBuffer.GetMeshAllocation(mesh.ID);
                var vertexStride = globalGeometryBuffer.GetVertexStride(allocation.MeshID);

                // CRITICAL: Verify vertex offset alignment
                if (allocation.VertexOffset % vertexStride != 0)
                {
                    _logger.Error($"Vertex offset {allocation.VertexOffset} is not aligned to vertex stride {vertexStride} for mesh {allocation.MeshID}");
                    continue;
                }

                uint vertexOffsetInVertices = (uint)(allocation.VertexOffset / vertexStride);
                uint firstIndex = (uint)(allocation.IndexOffset / sizeof(uint)); // Index buffer uses uint32

                if (_supportsMultiDraw)
                {
                    drawGroups.Add(new DrawGroup(
                        materialPass,
                        meshGroup[0].Mesh,
                        (uint)meshGroup.Count,
                        (ulong)_indirectCommandsList.Count * _commandStride
                    ));

                    _indirectCommandsList.Add(new DrawIndexedIndirectCommand
                    {
                        IndexCount = allocation.IndexCount,
                        InstanceCount = (uint)meshGroup.Count,
                        FirstIndex = firstIndex,
                        VertexOffset = (int)vertexOffsetInVertices, // This is VERTEX count, not bytes!
                        FirstInstance = meshGroup[0].TransformIndex
                    });
                }
                else
                {
                    ulong byteOffset = (ulong)_indirectCommandsList.Count * _commandStride;
                    drawGroups.Add(new DrawGroup(
                        materialPass,
                        meshGroup[0].Mesh,
                        (uint)meshGroup.Count,
                        byteOffset
                    ));

                    foreach (var cmd in meshGroup)
                    {
                        _indirectCommandsList.Add(new DrawIndexedIndirectCommand
                        {
                            IndexCount = allocation.IndexCount,
                            InstanceCount = 1,
                            FirstIndex = firstIndex,
                            VertexOffset = (int)vertexOffsetInVertices, // This is VERTEX count, not bytes!
                            FirstInstance = cmd.TransformIndex
                        });
                    }
                }
            }
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

                // Then by pipeline within the subpass
                var xPass = x.Mesh.Material.GetPass(x.SubpassName);
                var yPass = y.Mesh.Material.GetPass(y.SubpassName);

                int pipelineCompare = xPass.Pipeline.VkPipeline.VkObjectNative.Handle
                    .CompareTo(yPass.Pipeline.VkPipeline.VkObjectNative.Handle);
                if (pipelineCompare != 0)
                {
                    return pipelineCompare;
                }

                // Then by material
                int materialCompare = x.Mesh.Material.GetHashCode()
                    .CompareTo(y.Mesh.Material.GetHashCode());
                if (materialCompare != 0)
                {
                    return materialCompare;
                }

                // Finally by mesh
                return x.Mesh.Mesh.ID.CompareTo(y.Mesh.Mesh.ID);
            }
        }

        public readonly record struct DrawGroup(
            MaterialPass MaterialPass,
            MeshRenderer MeshRenderer,
            uint Count,
            ulong ByteOffset
        );
    }
}