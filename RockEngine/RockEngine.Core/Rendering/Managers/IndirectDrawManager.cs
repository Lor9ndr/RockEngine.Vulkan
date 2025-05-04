using RockEngine.Core.ECS.Components;
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
        // Основные зависимости
        private readonly VulkanContext _context;        // Контекст Vulkan API
        private readonly IndirectBuffer _indirectBuffer; // GPU буфер для хранения команд

        // Коллекции команд
        private readonly List<MeshRenderCommand> _commands = new List<MeshRenderCommand>(); // Основные команды рендеринга
        private readonly ConcurrentQueue<IRenderCommand> _otherCommands = new ConcurrentQueue<IRenderCommand>(); // Другие команды (потокобезопасные)
        private readonly Dictionary<RenderLayerType, List<DrawGroup>> _drawGroupsByLayer = new Dictionary<RenderLayerType, List<DrawGroup>>(); // Группировка по слоям

        // Состояние
        private readonly uint _transformBufferCapacity; // Макс. количество трансформ в буфере
        private bool _isDirty; // Флаг необходимости обновления данных
        private static readonly ulong _commandStride = (ulong)Marshal.SizeOf<DrawIndexedIndirectCommand>(); // Размер одной команды в байтах

        // Свойства
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

            // Выделяем буфер на 10 000 команд (можно масштабировать)
            _indirectBuffer = new IndirectBuffer(
                context,
                10_000 * _commandStride
            );
        }

        /// <summary>
        /// Добавляет меш для отрисовки с указанным индексом трансформации
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">При невалидном индексе трансформации</exception>
        public void AddMesh(Mesh mesh, int transformIndex)
        {
            if (transformIndex < 0 || transformIndex >= _transformBufferCapacity)
                throw new ArgumentOutOfRangeException(nameof(transformIndex), "Transform index exceeds buffer capacity");

            // Добавляем команду и помечаем данные как "грязные"
            _commands.Add(new MeshRenderCommand(mesh, (uint)transformIndex, mesh.Entity.Layer));
            _isDirty = true;
        }

        /// <summary>
        /// Обновляет команды отрисовки. Должен вызываться перед рендерингом.
        /// </summary>
        public void Update()
        {
            if (!_isDirty) return;

            // 1. Сортируем команды для оптимальной группировки
            _commands.Sort(MeshRenderCommandComparer.Default);

            // 2. Работаем через Span для прямого доступа к памяти списка
            Span<MeshRenderCommand> commandsSpan = CollectionsMarshal.AsSpan(_commands);

            List<DrawIndexedIndirectCommand> indirectCommands = new List<DrawIndexedIndirectCommand>(_commands.Count);
            _drawGroupsByLayer.Clear();

            // 3. Группируем команды по критериям
            RenderLayerType currentLayer = default;
            VkPipeline? currentPipeline = null;
            Material? currentMaterial = null;
            Mesh? currentMesh = null;
            int groupStartIndex = 0;

            for (int i = 0; i < commandsSpan.Length; i++)
            {
                ref readonly MeshRenderCommand cmd = ref commandsSpan[i];

                // Определяем начало новой группы
                bool isNewGroup = i == 0 ||
                    cmd.Layer != currentLayer ||
                    !ReferenceEquals(cmd.Mesh.Material.Pipeline, currentPipeline) ||
                    !ReferenceEquals(cmd.Mesh.Material, currentMaterial) ||
                    !ReferenceEquals(cmd.Mesh, currentMesh);

                if (isNewGroup && i > 0)
                {
                    // Добавляем найденную группу
                    AddGroup(commandsSpan.Slice(groupStartIndex, i - groupStartIndex), indirectCommands);
                    groupStartIndex = i;
                }

                // Обновляем текущие параметры группы
                currentLayer = cmd.Layer;
                currentPipeline = cmd.Mesh.Material.Pipeline;
                currentMaterial = cmd.Mesh.Material;
                currentMesh = cmd.Mesh;
            }

            // 4. Добавляем последнюю группу
            if (commandsSpan.Length > 0)
                AddGroup(commandsSpan.Slice(groupStartIndex), indirectCommands);

            // 5. Отправляем команды в GPU
            var batch = _context.SubmitContext.CreateBatch();
            _indirectBuffer.StageCommands(batch, CollectionsMarshal.AsSpan(indirectCommands));
            batch.Submit();

            // 6. Сбрасываем состояние
            _commands.Clear();
            _isDirty = false;
        }

        /// <summary>
        /// Добавляет группу команд в буфер и регистрирует DrawGroup
        /// </summary>
        /// <param name="groupSpan">Срез команд для группы</param>
        /// <param name="indirectCommands">Целевой список команд</param>
        private void AddGroup(Span<MeshRenderCommand> groupSpan, List<DrawIndexedIndirectCommand> indirectCommands)
        {
            if (groupSpan.IsEmpty) return;

            // Извлекаем данные из первой команды группы
            ref readonly MeshRenderCommand firstCmd = ref groupSpan[0];
            Mesh mesh = firstCmd.Mesh;
            uint firstInstance = firstCmd.TransformIndex;
            ulong byteOffset = (ulong)indirectCommands.Count * _commandStride;

            // Создаем команду инстансированного рендеринга
            indirectCommands.Add(new DrawIndexedIndirectCommand
            {
                IndexCount = (uint)mesh.Indices.Length,
                InstanceCount = (uint)groupSpan.Length,
                FirstIndex = 0,
                VertexOffset = 0,
                FirstInstance = firstInstance
            });

            // Регистрируем группу для рендера
            if (!_drawGroupsByLayer.TryGetValue(firstCmd.Layer, out var drawGroups))
            {
                drawGroups = new List<DrawGroup>();
                _drawGroupsByLayer[firstCmd.Layer] = drawGroups;
            }

            drawGroups.Add(new DrawGroup(
                firstCmd.Mesh.Material.Pipeline,
                firstCmd.Mesh.Material,
                mesh,
                (uint)groupSpan.Length,
                byteOffset
            ));
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