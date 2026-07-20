using System.Numerics;
using MemoryPack;
using RockEngine.Core.Attributes;
using RockEngine.Core.Extensions;
using RockEngine.Core.Rendering;

namespace RockEngine.Core.ECS.Components
{
    [MemoryPackable]
    public partial class Transform : Component
    {
        
        private Vector3 _position = Vector3.Zero;
        
        private Quaternion _rotation = Quaternion.Identity;
        
        private Vector3 _scale = Vector3.One;
        

        private Transform? _parent;
        

        private Matrix4x4 _worldMatrix;
        
        private bool _isDirty = true;
        public event Action<Transform>? TransformChanged;

        [UIEditable]
        public Vector3 Position
        {
            get => _position;
            set
            {
                _position = value;
                SetDirty();
            }
        }


        [UIEditable("Rotation")]
        public Vector3 EulerAngles
        {
            get => _rotation.QuaternionToEuler();
            set
            {
                _rotation = value.EulerToQuaternion();
                SetDirty();
            }
        }

        [SerializeIgnore]
        public Quaternion Rotation
        {
            get => _rotation;
            set
            {
                _rotation = value;
                SetDirty();
            }
        }

        [SerializeIgnore]
        public Quaternion LocalRotation
        {
            get => _rotation;
            set
            {
                _rotation = value;
                SetDirty();
            }
        }

        [UIEditable]
        public Vector3 Scale
        {
            get => _scale;
            set
            {
                _scale = value;
                SetDirty();
            }
        }

        [ SerializeIgnore]
        public Vector3 LocalScale
        {
            get => _scale;
            set
            {
                _scale = value;
                SetDirty();
            }
        }

        [SerializeIgnore]
        public Transform? Parent
        {
            get => _parent;
            set => SetParent(value);
        }

        [SerializeIgnore]
        public Matrix4x4 LocalMatrix
        {
            get
            {
                return Matrix4x4.CreateScale(_scale)
                    * Matrix4x4.CreateFromQuaternion(_rotation)
                    * Matrix4x4.CreateTranslation(_position);
            }
        }

        [SerializeIgnore]

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

        [SerializeIgnore]

        public Vector3 WorldPosition => WorldMatrix.Translation;

        [SerializeIgnore]
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

        [ SerializeIgnore]
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
        [MemoryPackConstructor]
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

            // Remove from old parent
            _parent?.TransformChanged -= OnParentTransformChanged;

            _parent = parent;

            // Add to new parent
            _parent?.TransformChanged += OnParentTransformChanged;

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