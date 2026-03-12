using ImGuiNET;
using System.Collections.Concurrent;

namespace RockEngine.Editor.Helpers
{
    /// <summary>
    /// Helper for ImGui drag-and-drop that supports any data type.
    /// Payloads are stored in a dictionary with a unique token, and the token is passed as the ImGui payload.
    /// This ensures type safety and avoids manual memory management.
    /// </summary>
    public static class DragDropHelper
    {
        private class DragData
        {
            public object Data;
            public string Type;
            public DateTime Timestamp;
        }

        private static int _nextToken = 1;
        private static readonly ConcurrentDictionary<int, DragData> _payloads = new ConcurrentDictionary<int, DragData>();
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5); // Clean up stale payloads after 5 seconds

        /// <summary>
        /// Begins a drag source for the specified data.
        /// Must be called within a ImGui item (e.g., after a Selectable or TreeNode).
        /// </summary>
        /// <typeparam name="T">Type of data being dragged.</typeparam>
        /// <param name="data">The data to drag.</param>
        /// <param name="payloadType">Optional custom payload type string. If null, uses typeof(T).FullName.</param>
        /// <returns>True if a drag source was started, false otherwise.</returns>
        public static unsafe bool BeginDragDropSource<T>(T data, string payloadType = null)
        {
            payloadType ??= typeof(T).FullName;

            if (ImGui.BeginDragDropSource())
            {
                int token = Interlocked.Increment(ref _nextToken);
                _payloads[token] = new DragData { Data = data, Type = payloadType, Timestamp = DateTime.UtcNow };

                // Pass the address of the token, not its value
                IntPtr ptr = new IntPtr(&token);
                ImGui.SetDragDropPayload(payloadType, ptr, (uint)sizeof(int));

                ImGui.Text($"Dragging {data?.ToString() ?? "null"}");
                ImGui.EndDragDropSource();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Accepts a drag drop payload of the specified type.
        /// Must be called inside a ImGui.BeginDragDropTarget() / EndDragDropTarget() block.
        /// </summary>
        /// <typeparam name="T">Expected type of the dropped data.</typeparam>
        /// <param name="result">The dropped data, if accepted.</param>
        /// <param name="payloadType">Optional custom payload type string. If null, uses typeof(T).FullName.</param>
        /// <returns>True if a payload of matching type was dropped and successfully retrieved.</returns>
        public static unsafe bool AcceptDragDropPayload<T>(out T result, string payloadType = null)
        {
            result = default;
            if (payloadType == null)
                payloadType = typeof(T).FullName;

            ImGuiPayload* payload = ImGui.AcceptDragDropPayload(payloadType);
            if (payload != null && payload->Data != null)
            {
                int token = *(int*)payload->Data;
                if (_payloads.TryRemove(token, out var dragData))
                {
                    // Double-check type match (in case of token collision, though unlikely)
                    if (dragData.Type == payloadType)
                    {
                        result = (T)dragData.Data;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Cleans up stale payloads that were never dropped (e.g., drag cancelled).
        /// Call this once per frame in your main loop.
        /// </summary>
        public static void CleanupStalePayloads()
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _payloads)
            {
                if (now - kv.Value.Timestamp > Timeout)
                {
                    _payloads.TryRemove(kv.Key, out _);
                }
            }
        }
    }
}