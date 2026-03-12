using RockEngine.Core.ECS;


namespace RockEngine.Editor.Selection
{
    public class EntitySelectionManager : ISelectionManager
    {
        private readonly Stack<SelectionContext> _undoStack = new();
        private readonly Stack<SelectionContext> _redoStack = new();
        private readonly int _maxHistorySize = 20;

        public SelectionContext CurrentSelection { get; private set; } = new SelectionContext();
        public IReadOnlyList<SelectionContext> SelectionHistory => _undoStack.ToList();

        public event Action<SelectionContext> SelectionChanging;
        public event Action<SelectionContext> SelectionChanged;
        public event Action<SelectionContext> SelectionContextChanged;

        public void Select(SelectionContext context)
        {
            if (context == null)
            {
                return;
            }

            // Validate selection
            if (!ValidateSelectionContext(context))
            {
                return;
            }

            var previousSelection = CurrentSelection;

            // Notify about changing selection
            SelectionChanging?.Invoke(context);

            // Push to history
            PushToHistory(previousSelection);

            // Update current selection
            CurrentSelection = context;

            // Clear redo stack when new selection is made
            _redoStack.Clear();

            // Notify about changed selection
            SelectionChanged?.Invoke(context);
            SelectionContextChanged?.Invoke(context);
        }

        public void SelectEntity(Entity entity, SelectionSource source = SelectionSource.Script, object additionalData = null)
        {
            var context = new SelectionContext(entity, source)
            {
                AdditionalData = additionalData
            };
            Select(context);
        }

        public void SelectEntities(IEnumerable<Entity> entities, SelectionSource source = SelectionSource.Script, object additionalData = null)
        {
            var context = new SelectionContext(entities, source)
            {
                AdditionalData = additionalData
            };
            Select(context);
        }

        public void AddToSelection(Entity entity, SelectionSource source = SelectionSource.Script)
        {
            if (entity == null || IsEntitySelected(entity))
            {
                return;
            }

            var newSelection = CurrentSelection.SelectedEntities.ToList();
            newSelection.Add(entity);

            var context = new SelectionContext(newSelection, source)
            {
                PrimaryEntity = entity, // New entity becomes primary
                AdditionalData = CurrentSelection.AdditionalData
            };
            Select(context);
        }

        public void RemoveFromSelection(Entity entity, SelectionSource source = SelectionSource.Script)
        {
            if (entity == null || !IsEntitySelected(entity))
            {
                return;
            }

            var newSelection = CurrentSelection.SelectedEntities.Where(e => e != entity).ToList();
            var newPrimary = newSelection.FirstOrDefault() ?? CurrentSelection.PrimaryEntity;

            var context = new SelectionContext(newSelection, source)
            {
                PrimaryEntity = newPrimary != entity ? newPrimary : newSelection.FirstOrDefault(),
                AdditionalData = CurrentSelection.AdditionalData
            };
            Select(context);
        }

        public void ClearSelection(SelectionSource source = SelectionSource.Script)
        {
            var context = new SelectionContext
            {
                Source = source
            };
            Select(context);
        }

        public bool CanSelectEntity(Entity entity)
        {
            return entity != null &&
                   entity.IsActive &&
                   IsSelectableEntity(entity);
        }

        public bool IsEntitySelected(Entity entity)
        {
            return CurrentSelection.SelectedEntities.Contains(entity);
        }

        public void UndoSelection()
        {
            if (_undoStack.Count > 0)
            {
                var previous = _undoStack.Pop();
                _redoStack.Push(CurrentSelection);

                SelectionChanging?.Invoke(previous);
                CurrentSelection = previous;
                SelectionChanged?.Invoke(previous);
                SelectionContextChanged?.Invoke(previous);
            }
        }

        public void RedoSelection()
        {
            if (_redoStack.Count > 0)
            {
                var next = _redoStack.Pop();
                _undoStack.Push(CurrentSelection);

                SelectionChanging?.Invoke(next);
                CurrentSelection = next;
                SelectionChanged?.Invoke(next);
                SelectionContextChanged?.Invoke(next);
            }
        }

        private bool ValidateSelectionContext(SelectionContext context)
        {
            // Filter out invalid entities
            var validEntities = context.SelectedEntities.Where(CanSelectEntity).ToList();

            if (validEntities.Count != context.SelectedEntities.Count)
            {
                context = new SelectionContext(validEntities, context.Source)
                {
                    AdditionalData = context.AdditionalData,
                    ScreenPosition = context.ScreenPosition,
                    WorldPosition = context.WorldPosition
                };
            }

            // Check if selection actually changed
            return !SelectionEquals(CurrentSelection, context);
        }

        private bool SelectionEquals(SelectionContext a, SelectionContext b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            if (a.SelectedEntities.Count != b.SelectedEntities.Count)
            {
                return false;
            }

            return a.SelectedEntities.All(b.ContainsEntity) &&
                   b.SelectedEntities.All(a.ContainsEntity) &&
                   a.PrimaryEntity == b.PrimaryEntity;
        }

        private bool IsSelectableEntity(Entity entity)
        {
            // Add any entity-specific selection rules here
            // For example, exclude helper entities, invisible entities, etc.
            return true;
        }

        private void PushToHistory(SelectionContext context)
        {
            _undoStack.Push(context);

            // Limit history size
            while (_undoStack.Count > _maxHistorySize)
            {
                _undoStack.Pop();
            }
        }
    }
}
