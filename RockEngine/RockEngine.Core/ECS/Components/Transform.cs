using RockEngine.Core.Extensions;
using RockEngine.Core.Rendering;

using System.Numerics;

namespace RockEngine.Core.ECS.Components
{
    public partial class Transform : Component
    {
        private Vector3 _position = Vector3.Zero;
        private Quaternion _rotation = Quaternion.Identity;
        private Vector3 _scale = Vector3.One;
        private Transform? _parent;
        private Matrix4x4 _worldMatrix;
        private bool _isDirty = true;
        public event Action<Transform>? TransformChanged;

        public Vector3 Position
        {
            get => _position;
            set { _position = value; SetDirty(); }
        }

        public Vector3 LocalPosition
        {
            get => _position;
            set { _position = value; SetDirty(); }
        }

        public Vector3 EulerAngles
        {
            get => _rotation.QuaternionToEuler();
            set
            {
                _rotation = value.EulerToQuaternion();
                SetDirty();
            }
        }

        public Quaternion Rotation
        {
            get => _rotation;
            set { _rotation = value; SetDirty(); }
        }

        public Quaternion LocalRotation
        {
            get => _rotation;
            set { _rotation = value; SetDirty(); }
        }

        public Vector3 Scale
        {
            get => _scale;
            set { _scale = value; SetDirty(); }
        }

        public Vector3 LocalScale
        {
            get => _scale;
            set { _scale = value; SetDirty(); }
        }

        public Transform? Parent
        {
            get => _parent;
            set => SetParent(value);
        }

        public Matrix4x4 LocalMatrix
        {
            get
            {
                return Matrix4x4.CreateScale(_scale)
                    * Matrix4x4.CreateFromQuaternion(_rotation)
                    * Matrix4x4.CreateTranslation(_position);
            }
        }

        public Matrix4x4 WorldMatrix
        {
            get
            {
                if (_isDirty)
                {
                    UpdateWorldMatrix();
                }
                return _worldMatrix;
            }
        }

        public Vector3 WorldPosition => WorldMatrix.Translation;

        public Quaternion WorldRotation
        {
            get
            {
                if (Parent == null)
                {
                    return _rotation;
                }

                return Quaternion.Normalize(Parent.WorldRotation * _rotation);
            }
        }

        public Vector3 WorldScale
        {
            get
            {
                if (Parent == null)
                {
                    return _scale;
                }

                var parentScale = Parent.WorldScale;
                return new Vector3(
                    parentScale.X * _scale.X,
                    parentScale.Y * _scale.Y,
                    parentScale.Z * _scale.Z
                );
            }
        }

        public Vector3 Right => Vector3.Transform(Vector3.UnitX, WorldRotation);
        public Vector3 Up => Vector3.Transform(Vector3.UnitY, WorldRotation);
        public Vector3 Forward => Vector3.Transform(Vector3.UnitZ, WorldRotation);

        public Vector3 LocalRight => Vector3.Transform(Vector3.UnitX, _rotation);
        public Vector3 LocalUp => Vector3.Transform(Vector3.UnitY, _rotation);
        public Vector3 LocalForward => Vector3.Transform(Vector3.UnitZ, _rotation);

        public Transform(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            _position = position;
            _rotation = rotation;
            _scale = scale;
            UpdateWorldMatrix();
        }

        public Transform() : this(Vector3.Zero, Quaternion.Identity, Vector3.One) { }

        public override void SetEntity(Entity entity)
        {
            base.SetEntity(entity);
            SetDirty();
        }

        private void UpdateWorldMatrix()
        {
            if (Parent == null)
            {
                _worldMatrix = Matrix4x4.CreateScale(_scale)
                    * Matrix4x4.CreateFromQuaternion(_rotation)
                    * Matrix4x4.CreateTranslation(_position);
            }
            else
            {
                // Correct order: ParentWorldMatrix * LocalMatrix
                _worldMatrix = LocalMatrix * Parent.WorldMatrix;
            }
            _isDirty = false;
        }

        private void SetDirty()
        {
            if (!_isDirty)
            {
                _isDirty = true;
                TransformChanged?.Invoke(this);

                // Propagate to children
                if (Entity != null)
                {
                    foreach (var child in Entity.Children)
                    {
                        child.Transform.SetDirty();
                    }
                }
            }
        }

        public void SetParent(Transform? parent)
        {
            if (_parent == parent)
            {
                return;
            }

            // If we're already attached to a parent, we need to adjust our local
            // position/rotation/scale to maintain the same world transform
            if (parent != null && _parent != null)
            {
                // Convert current world transform to local space relative to new parent
                if (Matrix4x4.Invert(parent.WorldMatrix, out var invParentWorld))
                {
                    var localMat = _worldMatrix * invParentWorld;

                    // Extract new local values
                    Matrix4x4.Decompose(localMat, out var newScale, out var newRotation, out var newPosition);
                    _position = newPosition;
                    _rotation = newRotation;
                    _scale = newScale;
                }
            }
            // If we're detaching from parent (setting parent to null)
            else if (parent == null && _parent != null)
            {
                // Convert current world transform to local (which becomes world now)
                _position = WorldPosition;
                _rotation = WorldRotation;
                _scale = WorldScale;
            }

            // Remove from old parent
            if (_parent != null)
            {
                _parent.TransformChanged -= OnParentTransformChanged;
            }

            _parent = parent;

            // Add to new parent
            if (_parent != null)
            {
                _parent.TransformChanged += OnParentTransformChanged;
            }

            SetDirty();
        }

        private void OnParentTransformChanged(Transform parent)
        {
            SetDirty();
        }

        public override ValueTask OnStart(WorldRenderer renderer) => default;
        public override ValueTask Update(WorldRenderer renderer) => default;
    }

    public enum Space
    {
        Local,
        World
    }
}