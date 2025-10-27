using ImGuiNET;

using RockEngine.Core.Assets;

using System.Runtime.InteropServices;

namespace RockEngine.Editor.EditorUI.ImGuiRendering
{
    public static class AssetDragDrop
    {
        public const string ASSET_PAYLOAD = "ASSET_PAYLOAD";
        public const string FOLDER_PAYLOAD = "FOLDER_PAYLOAD";
        public const string TEXTURE_PAYLOAD = "TEXTURE_PAYLOAD";
        public const string MATERIAL_PAYLOAD = "MATERIAL_PAYLOAD";
        public const string MESH_PAYLOAD = "MESH_PAYLOAD";

        public static bool BeginAssetDragDropSource<T>(Guid assetID, string displayName) where T : IAsset
        {
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
            {
                string payloadType = GetPayloadType<T>();
                nint ptr = Marshal.AllocHGlobal(Marshal.SizeOf(assetID));
                Marshal.StructureToPtr(assetID, ptr, false);

                ImGui.SetDragDropPayload(payloadType, ptr, (uint)Marshal.SizeOf(assetID));
                Marshal.FreeHGlobal(ptr);

                ImGui.Text($"Dragging {displayName} ({typeof(T).Name})");
                ImGui.EndDragDropSource();
                return true;
            }
            return false;
        }

        public static bool BeginAssetDragDropSource(Guid assetID, string displayName, Type assetType)
        {
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
            {
                string payloadType = GetPayloadType(assetType);
                nint ptr = Marshal.AllocHGlobal(Marshal.SizeOf(assetID));
                Marshal.StructureToPtr(assetID, ptr, false);

                ImGui.SetDragDropPayload(payloadType, ptr, (uint)Marshal.SizeOf(assetID));
                Marshal.FreeHGlobal(ptr);

                ImGui.Text($"Dragging {displayName} ({assetType.Name})");
                ImGui.EndDragDropSource();
                return true;
            }
            return false;
        }

        public static unsafe bool AcceptAssetDrop<T>(out Guid assetId) where T : IAsset
        {
            assetId = Guid.Empty;
            if (ImGui.BeginDragDropTarget())
            {
                string payloadType = GetPayloadType<T>();
                ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload(payloadType);
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

        public static unsafe bool AcceptAssetDrop(Type assetType, out Guid assetId)
        {
            assetId = Guid.Empty;
            if (ImGui.BeginDragDropTarget())
            {
                string payloadType = GetPayloadType(assetType);
                ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload(payloadType);
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

        private static string GetPayloadType<T>() where T : IAsset
        {
            return typeof(T).Name switch
            {
                nameof(MaterialAsset) => MATERIAL_PAYLOAD,
                nameof(TextureAsset) => TEXTURE_PAYLOAD,
                nameof(MeshAsset) => MESH_PAYLOAD,
                _ => ASSET_PAYLOAD
            };
        }

        private static string GetPayloadType(Type assetType)
        {
            return assetType.Name switch
            {
                nameof(MaterialAsset) => MATERIAL_PAYLOAD,
                nameof(TextureAsset) => TEXTURE_PAYLOAD,
                nameof(MeshAsset) => MESH_PAYLOAD,
                _ => ASSET_PAYLOAD
            };
        }

        // Existing methods for backward compatibility
        public static bool BeginDragDropSource(Guid assetID, string displayName)
        {
            return BeginAssetDragDropSource<IAsset>(assetID, displayName);
        }

        public static bool AcceptAssetDrop(out Guid assetId)
        {
            return AcceptAssetDrop<IAsset>(out assetId);
        }


        public static bool BeginDragDropSource(string path, string displayName)
        {
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
            {
                nint ptr = Marshal.StringToHGlobalAuto(path);
                ImGui.SetDragDropPayload(FOLDER_PAYLOAD, ptr, (uint)(path.Length * 2));
                Marshal.FreeHGlobal(ptr);

                ImGui.Text($"Dragging {displayName}");
                ImGui.EndDragDropSource();
                return true;
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