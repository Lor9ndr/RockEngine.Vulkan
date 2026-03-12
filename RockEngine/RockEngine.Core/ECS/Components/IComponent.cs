
using MessagePack;

using RockEngine.Assets;
using RockEngine.Core.ECS.Components.Physics;
using RockEngine.Core.Rendering;
    
namespace RockEngine.Core.ECS.Components
{
    [Union(0, typeof(MeshRenderer))]
    [Union(1, typeof(Light))]
    [Union(2, typeof(Camera))]
    [Union(3, typeof(RigidbodyComponent))]
    [Union(4, typeof(Transform))]
    [Union(5, typeof(SphereColliderComponent))]
    [Union(6, typeof(BoxColliderComponent))]
    [Union(7, typeof(CapsuleColliderComponent))]
    [Union(8, typeof(Skybox))]
    public interface IComponent: IPolymorphicSerializable
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
