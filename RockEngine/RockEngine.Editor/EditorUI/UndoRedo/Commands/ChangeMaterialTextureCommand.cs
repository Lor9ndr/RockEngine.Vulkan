using RockEngine.Core.Assets;


namespace RockEngine.Editor.EditorUI.UndoRedo.Commands
{
    public class ChangeMaterialTextureCommand : IUndoRedoCommand
    {
        private readonly MaterialAsset _material;
        private readonly int _slot;
        private readonly AssetReference<TextureAsset> _oldRef;
        private readonly AssetReference<TextureAsset> _newRef;

        public ChangeMaterialTextureCommand(MaterialAsset material, int slot, AssetReference<TextureAsset> oldRef, AssetReference<TextureAsset> newRef)
        {
            _material = material;
            _slot = slot;
            _oldRef = oldRef;
            _newRef = newRef;
        }

        public void Execute()
        {
            if (_newRef == null)
                _material.RemoveTexture(_oldRef);
            else if (_slot < _material.Textures.Count)
                _material.Textures[_slot] = _newRef;
            else
                _material.AddTexture(_newRef);
        }

        public void Undo()
        {
            if (_oldRef == null)
                _material.RemoveTexture(_newRef);
            else if (_slot < _material.Textures.Count)
                _material.Textures[_slot] = _oldRef;
            else
                _material.AddTexture(_oldRef);
        }
    }
}
