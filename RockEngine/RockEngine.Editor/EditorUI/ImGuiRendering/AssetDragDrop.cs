using ImGuiNET;

using System.Runtime.InteropServices;

namespace RockEngine.Editor.EditorUI.ImGuiRendering
{
    public static class AssetDragDrop
    {
        public const string ASSET_PAYLOAD = "ASSET_PAYLOAD";
        public const string FOLDER_PAYLOAD = "FOLDER_PAYLOAD";

        public static bool BeginDragDropSource(Guid assetID, string displayName)
        {
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
            {
                nint ptr = Marshal.AllocHGlobal(Marshal.SizeOf(assetID));
                Marshal.StructureToPtr(assetID, ptr, false);

                ImGui.SetDragDropPayload(ASSET_PAYLOAD, ptr, (uint)Marshal.SizeOf(assetID));
                Marshal.FreeHGlobal(ptr);

                ImGui.Text($"Dragging {displayName}");
                ImGui.EndDragDropSource();
                return true;
            }
            return false;
        }

        public static bool BeginDragDropSource(string path, string displayName)
        {
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
            {
                nint ptr = Marshal.StringToHGlobalAuto(path);
                ImGui.SetDragDropPayload(FOLDER_PAYLOAD, ptr, (uint)(path.Length * 2)); // Unicode chars are 2 bytes
                Marshal.FreeHGlobal(ptr);

                ImGui.Text($"Dragging {displayName}");
                ImGui.EndDragDropSource();
                return true;
            }
            return false;
        }

        public static unsafe bool AcceptAssetDrop(out Guid assetId)
        {
            assetId = Guid.Empty;
            if (ImGui.BeginDragDropTarget())
            {
                ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload(ASSET_PAYLOAD);
                if (payload.NativePtr != null)
                {
                    nint dataPtr = payload.Data;
                    assetId = Marshal.PtrToStructure<Guid>(dataPtr);
                    ImGui.EndDragDropTarget();
                    return true;
                }
                ImGui.EndDragDropTarget();
            }
            return false;
        }

        public static unsafe bool AcceptFolderDrop(out string path)
        {
            path = string.Empty;
            if (ImGui.BeginDragDropTarget())
            {
                ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload(FOLDER_PAYLOAD);
                if (payload.NativePtr != null)
                {
                    nint dataPtr = payload.Data;
                    path = Marshal.PtrToStringAuto(dataPtr);
                    ImGui.EndDragDropTarget();
                    return true;
                }
                ImGui.EndDragDropTarget();
            }
            return false;
        }
        public static unsafe bool IsAnyPayloadActive()
        {
            return ImGui.IsMouseDragging(ImGuiMouseButton.Left) ||
                   ImGui.GetDragDropPayload().NativePtr != default;
        }
    }
}