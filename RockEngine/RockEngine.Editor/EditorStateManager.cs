namespace RockEngine.Editor
{
    public enum EditorState
    {
        Edit,   // Editor mode - minimal updates
        Play,   // Full simulation mode
        Paused  // Game is running but updates are paused
    }

    public sealed class EditorStateManager
    {
        public EditorState State { get; private set; } = EditorState.Edit;
        public event Action<EditorState> StateChanged;

        public void SetState(EditorState newState)
        {
            if (State == newState) return;

            EditorState previousState = State;
            State = newState;
            StateChanged?.Invoke(newState);
        }

        public bool ShouldUpdateEntities => State == EditorState.Play;
        public bool ShouldRunEditorUpdates => State != EditorState.Play;
    }
}
