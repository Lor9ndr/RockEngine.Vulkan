using RockEngine.Core.Info;
using RockEngine.Core.Rendering.ResourceBindings;
using RockEngine.Vulkan;

namespace RockEngine.Core.Rendering.Managers
{
    public class GlobalTextureArray
    {
        private readonly VulkanContext _context;
        private readonly GlobalTextureArrayBinding _binding;

        public GlobalTextureArray(VulkanContext context)
        {
            _context = context;
            _binding = new GlobalTextureArrayBinding(MaterialInfo.TEXTURE_SET, 0, _context);
        }

        public uint AllocateIndex(Texturing.Texture texture)
        {
            return _binding.Allocate(texture);
        }

        public void FreeIndex(uint index)
        {
            _binding.Free(index);
        }

        public GlobalTextureArrayBinding GetBinding() => _binding;
    }
}
