using RockEngine.Core.Helpers;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    public interface IUIPropertyAccessorProvider
    {
        IReadOnlyList<UIPropertyAccessor> GetUIPropertyAccessors();
    }
}