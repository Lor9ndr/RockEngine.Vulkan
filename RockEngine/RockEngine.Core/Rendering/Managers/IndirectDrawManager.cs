using RockEngine.Core.Assets;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering.Buffers;
using RockEngine.Core.Rendering.Commands;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection.Emit;
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
        private readonly Dictionary<(RenderLayerType Layer, uint Subpass), List<DrawGroup>> _drawGroupsByLayerAndSubpass = new();
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
        public void AddMesh(MeshRenderer mesh, int transformIndex)
        {
            if (transformIndex < 0 || transformIndex >= _transformBufferCapacity)
                throw new ArgumentOutOfRangeException(nameof(transformIndex), "Transform index exceeds buffer capacity");

            _commands.Add(new MeshRenderCommand(mesh, (uint)transformIndex, mesh.Entity.Layer));
            _isDirty = true;
        }


        /// <summary>
        /// Обновляет команды отрисовки. Должен вызываться перед рендерингом.
        /// </summary>
        public ValueTask UpdateAsync()
        {
            if (!_isDirty)
                return ValueTask.CompletedTask;

            _commands.Sort(MeshRenderCommandComparer.Default);
            Span<MeshRenderCommand> commandsSpan = CollectionsMarshal.AsSpan(_commands);

            _indirectCommandsList.Clear();
            _drawGroupsByLayerAndSubpass.Clear();

            if (commandsSpan.Length == 0)
            {
                _isDirty = false;
                return ValueTask.CompletedTask;
            }

            // Устанавливаем достаточную емкость для списка команд
            _indirectCommandsList.Capacity = Math.Max(_indirectCommandsList.Capacity, commandsSpan.Length);

            int groupStartIndex = 0;
            for (int i = 1; i < commandsSpan.Length; i++)
            {
                if (IsNewGroup(in commandsSpan[i - 1], in commandsSpan[i]))
                {
                    AddGroup(commandsSpan.Slice(groupStartIndex, i - groupStartIndex));
                    groupStartIndex = i;
                }
            }
            AddGroup(commandsSpan[groupStartIndex..]);

            var batch = _context.SubmitContext.CreateBatch();
            batch.CommandBuffer.LabelObject("IndirectDrawManager cmd");
            _indirectBuffer.StageCommands(batch, CollectionsMarshal.AsSpan(_indirectCommandsList));

            // Добавлен барьер после обновления буфера
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


            _commands.Clear();
            _isDirty = false;
            return ValueTask.CompletedTask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsNewGroup(in MeshRenderCommand a, in MeshRenderCommand b)
        {
            return a.Layer != b.Layer ||
                   !ReferenceEquals(a.Mesh.Material.MaterialInstance.Pipeline, b.Mesh.Material.MaterialInstance.Pipeline) ||
                   !ReferenceEquals(a.Mesh.Material.MaterialInstance, b.Mesh.Material.MaterialInstance) ||
                   !ReferenceEquals(a.Mesh, b.Mesh);
        }

        /// <summary>
        /// Добавляет группу команд в буфер и регистрирует DrawGroup
        /// </summary>
        /// <param name="groupSpan">Срез команд для группы</param>
        /// <param name="indirectCommands">Целевой список команд</param>
        private void AddGroup(Span<MeshRenderCommand> groupSpan)
        {
            groupSpan.Sort((a, b) => a.TransformIndex.CompareTo(b.TransformIndex));
            ref readonly MeshRenderCommand firstCmd = ref groupSpan[0];

            var pipeline = firstCmd.Mesh.Material.MaterialInstance.Pipeline;
            var key = (firstCmd.Layer, pipeline.SubPass);

            if (!_drawGroupsByLayerAndSubpass.TryGetValue(key, out var groups))
            {
                groups = new List<DrawGroup>();
                _drawGroupsByLayerAndSubpass[key] = groups;
            }

            groups.Add(new DrawGroup(
                pipeline,
                firstCmd.Mesh.Material.MaterialInstance,
                firstCmd.Mesh,
                (uint)groupSpan.Length,
                (ulong)(_indirectCommandsList.Count) * _commandStride
            ));

            _indirectCommandsList.Add(new DrawIndexedIndirectCommand
            {
                IndexCount = firstCmd.Mesh.IndicesCount.Value,
                InstanceCount = (uint)groupSpan.Length,
                FirstIndex = 0,
                VertexOffset = 0,
                FirstInstance = firstCmd.TransformIndex
            });
        }

        /// <summary>
        /// Возвращает группы отрисовки для указанного слоя
        /// </summary>
        public List<DrawGroup> GetDrawGroups(RenderLayerType layer, uint subpass)
        {
            var key = (layer, subpass);
            return _drawGroupsByLayerAndSubpass.TryGetValue(key, out var groups)
                ? groups
                : [];
           
        }


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
            public readonly MeshRenderer Mesh;             // Ссылка на меш
            public readonly uint TransformIndex;   // Индекс в трансформ-буфере
            public readonly RenderLayerType Layer; // Слой рендеринга

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public MeshRenderCommand(MeshRenderer mesh, uint transformIndex, RenderLayerType layer)
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
                int subPassCompare = x.Mesh.Material.MaterialInstance.Pipeline.SubPass
                    .CompareTo(y.Mesh.Material.MaterialInstance.Pipeline.SubPass);
                if (subPassCompare != 0) return subPassCompare;

                // Сравнение пайплайнов по ID
                int pipelineCompare = x.Mesh.Material.MaterialInstance.Pipeline.VkObjectNative.Handle
                    .CompareTo(y.Mesh.Material.MaterialInstance.Pipeline.VkObjectNative.Handle);
                if (pipelineCompare != 0) return pipelineCompare;

                // Сравнение материалов по ID
                int materialCompare = x.Mesh.Material.ID
                    .CompareTo(y.Mesh.Material.ID);
                if (materialCompare != 0) return materialCompare;

                // Сравнение мешей по ID
                return x.Mesh.Mesh.ID.CompareTo(y.Mesh.Mesh.ID);
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
            MeshRenderer Mesh,
            uint Count,
            ulong ByteOffset
        );
        #endregion
    }
}