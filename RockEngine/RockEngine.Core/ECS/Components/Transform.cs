using RockEngine.Core.Rendering;

using System.Numerics;

namespace RockEngine.Core.ECS.Components
{
    public class Transform : Component
    {
        private Vector3 _position = Vector3.Zero;
        private Quaternion _rotation = Quaternion.Identity;
        private Vector3 _scale = Vector3.One;
        private Transform? _parent;
        private Matrix4x4 _worldMatrix;
        private bool _isDirty = true;
        public event Action TransformChanged;

        public Vector3 Position
        {
            get => _position;
            set { _position = value; SetDirty(); }
        }

        public Quaternion Rotation
        {
            get => _rotation;
            set { _rotation = value; SetDirty(); }
        }

        public Vector3 Scale
        {
            get => _scale;
            set { _scale = value; SetDirty(); }
        }

        public Transform? Parent
        {
            get => _parent;
            set
            {
                SetParent(value);
            }
        }

        public Matrix4x4 LocalMatrix => Matrix4x4.CreateScale(Scale)
            * Matrix4x4.CreateFromQuaternion(Rotation)
            * Matrix4x4.CreateTranslation(Position);

        public Matrix4x4 WorldMatrix
        {
            get
            {
                if (_isDirty)
                {
                    _worldMatrix = Parent == null
                        ? LocalMatrix
                        : LocalMatrix * Parent.WorldMatrix;
                    _isDirty = false;
                }
                return _worldMatrix;
            }
        }
        public Vector3 WorldPosition => WorldMatrix.Translation;
        public Quaternion WorldRotation => Parent?.WorldRotation * Rotation ?? Rotation;
        public Vector3 WorldScale => Parent?.WorldScale * Scale ?? Scale;


        public Transform(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            _position = position;
            _rotation = rotation;
            _scale = scale;
        }

        public override void SetEntity(Entity entity)
        {
            base.SetEntity(entity);
            SetDirty();
        }

        public Transform() : this(Vector3.Zero, Quaternion.Identity, Vector3.One) { }

        public Matrix4x4 GetModelMatrix()
        {
            var scaleMatrix = Matrix4x4.CreateScale(Scale);
            var rotationMatrix = Matrix4x4.CreateFromQuaternion(Rotation);
            var translationMatrix = Matrix4x4.CreateTranslation(Position);
            return scaleMatrix * rotationMatrix * translationMatrix;
        }

        public override ValueTask OnStart(Renderer renderer)
        {
            return default;
        }

        public override ValueTask Update(Renderer renderer)
        {
            return default;
        }

        public void SetParent(Transform? parent)
        {
            if (_parent != null)
            {
                _parent.TransformChanged -= OnParentTransformChanged;
            }
            _parent = parent;
            if (_parent != null)
            {
                _parent.TransformChanged += OnParentTransformChanged;
            }
            SetDirty();
        }
        private void SetDirty()
        {
            _isDirty = true;
            TransformChanged?.Invoke();
            foreach (var child in Entity.Children)
            {
                child.Transform.SetDirty();
            }
        }

        private void OnParentTransformChanged()
        {
            SetDirty();
        }
     
    }
}
