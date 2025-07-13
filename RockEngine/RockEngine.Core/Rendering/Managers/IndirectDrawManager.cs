using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RockEngine.Core.Rendering.Managers
{
    /// <summary>
    /// Управляет косвенными командами отрисовки для Vulkan рендерера.
    /// Оптимизирует группировку объектов по слоям, пайплайнам и материалам.
    /// </summary>
    public sealed class IndirectCommandManager : IDisposable
    {
        private readonly VulkanContext _context;
        private readonly IndirectBuffer _indirectBuffer;
        private readonly List<MeshRenderCommand> _commands = new List<MeshRenderCommand>();
        private readonly ConcurrentQueue<IRenderCommand> _otherCommands = new ConcurrentQueue<IRenderCommand>();
        private readonly Dictionary<RenderLayerType, List<DrawGroup>> _drawGroupsByLayer = new Dictionary<RenderLayerType, List<DrawGroup>>();
        private readonly uint _transformBufferCapacity;
        private bool _isDirty;
        private static readonly ulong _commandStride = (ulong)Marshal.SizeOf<DrawIndexedIndirectCommand>();
        private readonly List<DrawIndexedIndirectCommand> _indirectCommandsList = new List<DrawIndexedIndirectCommand>();

        public IndirectBuffer IndirectBuffer => _indirectBuffer;
        public ConcurrentQueue<IRenderCommand> OtherCommands => _otherCommands;

        /// <summary>
        /// Инициализирует менеджер команд с указанным контекстом Vulkan
        /// </summary>
        /// <param name="context">Контекст Vulkan</param>
        /// <param name="transformBufferCapacity">Максимальное количество трансформ</param>
        public IndirectCommandManager(VulkanContext context, uint transformBufferCapacity)
        {
            _context = context;
            _transformBufferCapacity = transformBufferCapacity;
            _indirectBuffer = new IndirectBuffer(context, 10_000 * _commandStride);
        }

        /// <summary>
        /// Добавляет меш для отрисовки с указанным индексом трансформации
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">При невалидном индексе трансформации</exception>
        public void AddMesh(Mesh mesh, int transformIndex)
        {
            if (transformIndex < 0 || transformIndex >= _transformBufferCapacity)
                throw new ArgumentOutOfRangeException(nameof(transformIndex), "Transform index exceeds buffer capacity");

            _commands.Add(new MeshRenderCommand(mesh, (uint)transformIndex, mesh.Entity.Layer));
            _isDirty = true;
        }


        /// <summary>
        /// Обновляет команды отрисовки. Должен вызываться перед рендерингом.
        /// </summary>
        public Task UpdateAsync()
        {
            if (!_isDirty)
                return Task.CompletedTask;

            // Sort commands for optimal grouping
            _commands.Sort(MeshRenderCommandComparer.Default);
            Span<MeshRenderCommand> commandsSpan = CollectionsMarshal.AsSpan(_commands);

            // Clear reused collections
            _indirectCommandsList.Clear();
            _drawGroupsByLayer.Clear();

            if (commandsSpan.Length == 0)
            {
                _isDirty = false;
                return Task.CompletedTask;
            }

            // Group commands by boundaries
            int groupStartIndex = 0;
            for (int i = 1; i < commandsSpan.Length; i++)
            {
                if (IsNewGroup(in commandsSpan[i - 1], in commandsSpan[i]))
                {
                    AddGroup(commandsSpan.Slice(groupStartIndex, i - groupStartIndex));
                    groupStartIndex = i;
                }
            }
            // Add final group
            AddGroup(commandsSpan.Slice(groupStartIndex));

            // Submit commands to GPU
            var batch = _context.SubmitContext.CreateBatch();
            batch.CommandBuffer.LabelObject("IndirectDrawManager cmd");
            _indirectBuffer.StageCommands(batch, CollectionsMarshal.AsSpan(_indirectCommandsList));
            batch.Submit();

            _commands.Clear();
            _isDirty = false;
            return Task.CompletedTask;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsNewGroup(in MeshRenderCommand a, in MeshRenderCommand b)
        {
            return a.Layer != b.Layer ||
                   !ReferenceEquals(a.Mesh.Material.Pipeline, b.Mesh.Material.Pipeline) ||
                   !ReferenceEquals(a.Mesh.Material, b.Mesh.Material) ||
                   !ReferenceEquals(a.Mesh, b.Mesh);
        }

        /// <summary>
        /// Добавляет группу команд в буфер и регистрирует DrawGroup
        /// </summary>
        /// <param name="groupSpan">Срез команд для группы</param>
        /// <param name="indirectCommands">Целевой список команд</param>
        private void AddGroup(Span<MeshRenderCommand> groupSpan)
        {
            // Sort group by transform index for consecutive GPU access
            groupSpan.Sort((a, b) => a.TransformIndex.CompareTo(b.TransformIndex));
            ref readonly MeshRenderCommand firstCmd = ref groupSpan[0];

            // Create indirect command
            _indirectCommandsList.Add(new DrawIndexedIndirectCommand
            {
                IndexCount = firstCmd.Mesh.IndicesCount,
                InstanceCount = (uint)groupSpan.Length,
                FirstIndex = 0,
                VertexOffset = 0,
                FirstInstance = firstCmd.TransformIndex
            });

            // Register draw group
            var drawGroups = GetOrAddLayerGroups(firstCmd.Layer);
            drawGroups.Add(new DrawGroup(
                firstCmd.Mesh.Material.Pipeline,
                firstCmd.Mesh.Material,
                firstCmd.Mesh,
                (uint)groupSpan.Length,
                (ulong)(_indirectCommandsList.Count - 1) * _commandStride
            ));
        }

        private List<DrawGroup> GetOrAddLayerGroups(RenderLayerType layer)
        {
            if (!_drawGroupsByLayer.TryGetValue(layer, out var groups))
            {
                groups = new List<DrawGroup>();
                _drawGroupsByLayer[layer] = groups;
            }
            return groups;
        }

        /// <summary>
        /// Возвращает группы отрисовки для указанного слоя
        /// </summary>
        public IEnumerable<DrawGroup> GetDrawGroups(RenderLayerType layerType)
            => _drawGroupsByLayer.TryGetValue(layerType, out var groups)
                ? groups
                : Enumerable.Empty<DrawGroup>();


        /// <summary>
        /// Освобождает ресурсы Vulkan
        /// </summary>
        public void Dispose() => _indirectBuffer.Dispose();

        /// <summary>
        /// Добавляет произвольную команду рендеринга (потокобезопасно)
        /// </summary>
        internal void AddCommand(IRenderCommand command) => _otherCommands.Enqueue(command);

        #region Inner components
        // Внутренние структуры ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

        /// <summary>
        /// Команда рендеринга меша (readonly для безопасности)
        /// </summary>
        private readonly struct MeshRenderCommand
        {
            public readonly Mesh Mesh;             // Ссылка на меш
            public readonly uint TransformIndex;   // Индекс в трансформ-буфере
            public readonly RenderLayerType Layer; // Слой рендеринга

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public MeshRenderCommand(Mesh mesh, uint transformIndex, RenderLayerType layer)
            {
                Mesh = mesh;
                TransformIndex = transformIndex;
                Layer = layer;
            }
        }

        /// <summary>
        /// Компаратор для сортировки команд по критериям:
        /// 1. Слой рендеринга
        /// 2. Subpass пайплайна
        /// 3. Пайплайн (по хешу)
        /// 4. Материал (по хешу)
        /// 5. Меш (по хешу)
        /// </summary>
        private sealed class MeshRenderCommandComparer : IComparer<MeshRenderCommand>
        {
            public static readonly MeshRenderCommandComparer Default = new();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int Compare(MeshRenderCommand x, MeshRenderCommand y)
            {
                // Сортировка по слою
                int layerCompare = x.Layer.CompareTo(y.Layer);
                if (layerCompare != 0) return layerCompare;

                // Сортировка по subpass пайплайна
                int subPassCompare = x.Mesh.Material.Pipeline.SubPass.CompareTo(y.Mesh.Material.Pipeline.SubPass);
                if (subPassCompare != 0) return subPassCompare;

                // Сравнение пайплайнов по хешу (для группировки одинаковых объектов)
                int pipelineCompare = RuntimeHelpers.GetHashCode(x.Mesh.Material.Pipeline)
                    .CompareTo(RuntimeHelpers.GetHashCode(y.Mesh.Material.Pipeline));
                if (pipelineCompare != 0) return pipelineCompare;

                // Сравнение материалов
                int materialCompare = RuntimeHelpers.GetHashCode(x.Mesh.Material)
                    .CompareTo(RuntimeHelpers.GetHashCode(y.Mesh.Material));
                if (materialCompare != 0) return materialCompare;

                // Сравнение мешей
                return RuntimeHelpers.GetHashCode(x.Mesh)
                    .CompareTo(RuntimeHelpers.GetHashCode(y.Mesh));
            }
        }

        /// <summary>
        /// Группа объектов для отрисовки
        /// </summary>
        /// <param name="Pipeline">Vulkan пайплайн</param>
        /// <param name="Material">Материал</param>
        /// <param name="Mesh">Геометрия</param>
        /// <param name="Count">Количество инстансов</param>
        /// <param name="ByteOffset">Смещение в командном буфере</param>
        public readonly record struct DrawGroup(
            VkPipeline Pipeline,
            Material Material,
            Mesh Mesh,
            uint Count,
            ulong ByteOffset
        );
        #endregion
    }
}