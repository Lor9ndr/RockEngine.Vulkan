using JoltPhysicsSharp;

using MessagePack;

using RockEngine.Core.ECS.Components.Physics;
using RockEngine.Core.Rendering;

using System.Numerics;

namespace RockEngine.Core.ECS.Components
{
    [Union(0, typeof(BoxColliderComponent))]
    [Union(1, typeof(SphereColliderComponent))]
    [Union(2, typeof(CapsuleColliderComponent))]
    public interface IColliderComponent : IComponent
    {
        bool IsTrigger { get; set; }
        Vector3 Center { get; set; }
        event Action<IColliderComponent> OnChanged;
        void NotifyChanged();
    }

    public abstract class ColliderComponent : Component, IColliderComponent
    {
        [IgnoreMember]

        private Vector3 _center = Vector3.Zero;
        [IgnoreMember]

        private bool _isTrigger = false;

        [MessagePack.Key(3)]
        public Vector3 Center
        {
            get => _center;
            set
            {
                if (_center != value)
                {
                    _center = value;
                    NotifyChanged();
                }
            }
        }

        [MessagePack.Key(4)]
        public bool IsTrigger
        {
            get => _isTrigger;
            set
            {
                if (_isTrigger != value)
                {
                    _isTrigger = value;
                    NotifyChanged();
                }
            }
        }

        public event Action<IColliderComponent> OnChanged;

        public virtual void NotifyChanged()
        {
            OnChanged?.Invoke(this);
        }

        public abstract Shape CreateShape();

        public override ValueTask OnStart(WorldRenderer renderer) => default;
        public override ValueTask Update(WorldRenderer renderer) => default;

        public override void Destroy()
        {
            OnChanged = null;
            base.Destroy();
        }
    }
}