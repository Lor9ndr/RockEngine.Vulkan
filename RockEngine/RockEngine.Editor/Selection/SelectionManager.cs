using RockEngine.Core.ECS;
using RockEngine.Assets;

namespace RockEngine.Editor.Selection
{
    public class SelectionManager : ISelectionManager
    {
        private SelectionContext _currentSelection = new();
        private readonly Stack<SelectionContext> _undoStack = new();
        private readonly Stack<SelectionContext> _redoStack = new();
        private readonly List<SelectionContext> _history = new();

        public SelectionContext CurrentSelection => _currentSelection;
        public IReadOnlyList<SelectionContext> SelectionHistory => _history;

        public event Action<SelectionContext>? SelectionChanging;
        public event Action<SelectionContext>? SelectionChanged;
        public event Action<SelectionContext>? SelectionContextChanged;

        public void Select(SelectionContext context)
        {
            SelectionChanging?.Invoke(context);
            _undoStack.Push(_currentSelection);
            _redoStack.Clear();
            _currentSelection = context;
            _history.Add(context);
            SelectionChanged?.Invoke(context);
            SelectionContextChanged?.Invoke(context);
        }

        public void SelectEntity(Entity entity, SelectionSource source = SelectionSource.Script, object? additionalData = null)
        {
            var ctx = new SelectionContext(entity, source) { AdditionalData = additionalData };
            Select(ctx);
        }

        public void SelectEntities(IEnumerable<Entity> entities, SelectionSource source = SelectionSource.Script, object? additionalData = null)
        {
            var ctx = new SelectionContext(entities, source) { AdditionalData = additionalData };
            Select(ctx);
        }

        public void AddToSelection(Entity entity, SelectionSource source = SelectionSource.Script)
        {
            var list = _currentSelection.SelectedEntities.ToList();
            if (!list.Contains(entity))
            {
                list.Add(entity);
                Select(new SelectionContext(list, source));
            }
        }

        public void RemoveFromSelection(Entity entity, SelectionSource source = SelectionSource.Script)
        {
            var list = _currentSelection.SelectedEntities.ToList();
            if (list.Remove(entity))
            {
                Select(new SelectionContext(list, source));
            }
        }

        public void ClearSelection(SelectionSource source = SelectionSource.Script)
        {
            Select(new SelectionContext());
        }

        public bool CanSelectEntity(Entity entity) => true;
        public bool IsEntitySelected(Entity entity) => _currentSelection.ContainsEntity(entity);

        // New asset selection methods
        public void SelectAsset(IAsset asset, SelectionSource source = SelectionSource.Script)
        {
            var ctx = new SelectionContext(asset, source);
            Select(ctx);
        }

        public void ClearAssetSelection(SelectionSource source = SelectionSource.Script)
        {
            if (_currentSelection.HasAssetSelection)
            {
                var ctx = new SelectionContext
                {
                    Source = source
                }; // clear everything
                Select(ctx);
            }
        }

        public void UndoSelection()
        {
            if (_undoStack.Count > 0)
            {
                _redoStack.Push(_currentSelection);
                _currentSelection = _undoStack.Pop();
                SelectionContextChanged?.Invoke(_currentSelection);
            }
        }

        public void RedoSelection()
        {
            if (_redoStack.Count > 0)
            {
                _undoStack.Push(_currentSelection);
                _currentSelection = _redoStack.Pop();
                SelectionContextChanged?.Invoke(_currentSelection);
            }
        }
    }
}
