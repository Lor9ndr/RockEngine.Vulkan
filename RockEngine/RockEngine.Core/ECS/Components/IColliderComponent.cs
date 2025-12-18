using JoltPhysicsSharp;

using RockEngine.Core.Rendering;

using System.Numerics;

namespace RockEngine.Core.ECS.Components
{
    public interface IColliderComponent : IComponent
    {
        bool IsTrigger { get; set; }
        Vector3 Center { get; set; }
        event Action<IColliderComponent> OnChanged;
        void NotifyChanged();
    }

    public abstract class ColliderComponent : Component, IColliderComponent
    {
        private Vector3 _center = Vector3.Zero;
        private bool _isTrigger = false;

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