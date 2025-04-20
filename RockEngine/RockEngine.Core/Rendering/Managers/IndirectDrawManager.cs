using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Managers
{
    public class IndirectCommandManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly IndirectBuffer _indirectBuffer;
        private readonly List<MeshRenderCommand> _commands = new List<MeshRenderCommand>();
        private readonly ConcurrentQueue<IRenderCommand> _otherCommands = new ConcurrentQueue<IRenderCommand>();
        private readonly List<DrawGroup> _drawGroups = new List<DrawGroup>(); 

        public IndirectBuffer IndirectBuffer => _indirectBuffer;

        public ConcurrentQueue<IRenderCommand> OtherCommands => _otherCommands;
        private uint _maxTransformIndex;
        private readonly uint _transformBufferCapacity;
        public IndirectCommandManager(VulkanContext context, uint transformBufferCapacity)
        {
            _context = context;
            _transformBufferCapacity = transformBufferCapacity;

            _indirectBuffer = new IndirectBuffer(
                context,
                10_000 * (ulong)Marshal.SizeOf<DrawIndexedIndirectCommand>()
            );
        }


        public void AddMesh(Mesh mesh, int transformIndex)
        {
            // Validate transform index
            if (transformIndex < 0 || transformIndex >= _transformBufferCapacity)
                throw new ArgumentOutOfRangeException(nameof(transformIndex),
                    "Transform index exceeds buffer capacity");

            _commands.Add(new MeshRenderCommand(mesh)
            {
                TransformIndex = (uint)transformIndex
            });

            // Track maximum index
            _maxTransformIndex = Math.Max(_maxTransformIndex, (uint)transformIndex);
        }

        public void AddCommand(IRenderCommand command)
        {
            _otherCommands.Enqueue(command);
        }

        public Task Update()
        {
            // Clear previous data
            var commands = new List<DrawIndexedIndirectCommand>();
            _drawGroups.Clear();

           

            // Group by pipeline then mesh
            var pipelineGroups = _commands
                .GroupBy(c => c.Mesh.Material.Pipeline)
                .OrderBy(g => g.Key);

            foreach (var pipelineGroup in pipelineGroups)
            {
                var meshGroups = pipelineGroup
                    .GroupBy(c => c.Mesh);

                foreach (var meshGroup in meshGroups)
                {
                    // Calculate BYTE offset for this group
                    ulong byteOffset = (ulong)commands.Count *
                        (ulong)Marshal.SizeOf<DrawIndexedIndirectCommand>();

                    int count = 0;
                    foreach (var cmd in meshGroup)
                    {
                        commands.Add(new DrawIndexedIndirectCommand
                        {
                            IndexCount = (uint)cmd.Mesh.Indices.Length,
                            InstanceCount = 1,
                            FirstIndex = 0,
                            VertexOffset = 0,
                            FirstInstance = cmd.TransformIndex // Correct usage
                        });
                        count++;
                    }

                    _drawGroups.Add(new DrawGroup(
                        Pipeline: pipelineGroup.Key,
                        Mesh: meshGroup.Key,
                        Count: (uint)count,
                        ByteOffset: byteOffset // Store BYTE offset
                    ));
                }
            }

            // Stage commands
            var batch = _context.SubmitContext.CreateBatch();
            _indirectBuffer.StageCommands(batch, commands.ToArray());
             batch.Submit();

            _commands.Clear();
            _maxTransformIndex = 0;
            return Task.CompletedTask;
        }

        public IEnumerable<DrawGroup> GetDrawGroups() => _drawGroups;

        public void Dispose()
        {
            _indirectBuffer.Dispose();
        }

        public record struct DrawGroup(
             VkPipeline Pipeline,
             Mesh Mesh,
             uint Count,
             ulong ByteOffset 
         );
    }
}