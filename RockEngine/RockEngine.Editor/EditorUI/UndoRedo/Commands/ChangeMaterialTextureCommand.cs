using RockEngine.Core.Assets;


namespace RockEngine.Editor.EditorUI.UndoRedo.Commands
{
    public class ChangeMaterialTextureCommand : IUndoRedoCommand
    {
        private readonly MaterialAsset _material;
        private readonly string _slot;
        private readonly AssetReference<TextureAsset> _oldRef;
        private readonly AssetReference<TextureAsset> _newRef;

        public ChangeMaterialTextureCommand(MaterialAsset material, string slot, AssetReference<TextureAsset> oldRef, AssetReference<TextureAsset> newRef)
        {
            _material = material;
            _slot = slot;
            _oldRef = oldRef;
            _newRef = newRef;
        }

        public void Execute()
        {
            if (_newRef == null)
            {
                _material.RemoveTexture(_slot);
            }

            _material.AddTexture(_newRef, _slot);
        }

        public void Undo()
        {
            if (_oldRef == null)
            {
                _material.RemoveTexture(_slot);
            }

            _material.AddTexture(_oldRef, _slot);
        }
    }
}
