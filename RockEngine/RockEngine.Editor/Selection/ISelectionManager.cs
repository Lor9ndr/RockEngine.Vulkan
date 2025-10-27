using RockEngine.Core.ECS;


namespace RockEngine.Editor.Selection
{
    public interface ISelectionManager
    {
        SelectionContext CurrentSelection { get; }
        IReadOnlyList<SelectionContext> SelectionHistory { get; }

        event Action<SelectionContext> SelectionChanging;
        event Action<SelectionContext> SelectionChanged;
        event Action<SelectionContext> SelectionContextChanged;

        void Select(SelectionContext context);
        void SelectEntity(Entity entity, SelectionSource source = SelectionSource.Script, object additionalData = null);
        void SelectEntities(IEnumerable<Entity> entities, SelectionSource source = SelectionSource.Script, object additionalData = null);
        void AddToSelection(Entity entity, SelectionSource source = SelectionSource.Script);
        void RemoveFromSelection(Entity entity, SelectionSource source = SelectionSource.Script);
        void ClearSelection(SelectionSource source = SelectionSource.Script);
        bool CanSelectEntity(Entity entity);
        bool IsEntitySelected(Entity entity); // Renamed from IsSelected
        void UndoSelection();
        void RedoSelection();
    }
}
