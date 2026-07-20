using MemoryPack;
using RockEngine.Core.ECS.Components.UI;
using RockEngine.Core.Rendering;

namespace RockEngine.Core.ECS.Components
{
    [MemoryPackUnion(1, typeof(Transform))]
    [MemoryPackUnion(2, typeof(Light))]
    [MemoryPackUnion(3, typeof(CapsuleColliderComponent))]
    [MemoryPackUnion(4, typeof(BoxColliderComponent))]
    [MemoryPackUnion(5, typeof(Camera))]
    [MemoryPackUnion(6, typeof(RigidbodyComponent))]
    [MemoryPackUnion(7, typeof(Skybox))]
    [MemoryPackUnion(8, typeof(SphereColliderComponent))]
    [MemoryPackUnion(9, typeof(TextRenderer))]
    [MemoryPackUnion(11, typeof(MeshRenderer))]
    [MemoryPackUnion(12, typeof(RectTransform))]
    [MemoryPackable]
    public partial interface IComponent 
    {
        public bool IsActive { get; }
        public Entity Entity { get; }
        public ValueTask Update(WorldRenderer renderer);
        public ValueTask OnStart(WorldRenderer renderer);
        public void SetEntity(Entity entity);
        void Destroy();
        void SetActive(bool isActive = true);
    }
}
