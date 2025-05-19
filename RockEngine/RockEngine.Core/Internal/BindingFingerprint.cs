namespace RockEngine.Core.Rendering.Managers
{
    public partial class BindingManager
    {
        private struct BindingFingerprint : IEquatable<BindingFingerprint>
        {
            public uint Set { get; init; }
            private readonly int _hashCode;

            public BindingFingerprint(uint set, int hashCode)
            {
                Set = set;
                _hashCode = hashCode;
            }

            public bool Equals(BindingFingerprint other) =>
                Set == other.Set && _hashCode == other._hashCode;

            public override bool Equals(object obj) =>
                obj is BindingFingerprint other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(Set, _hashCode);
        }

    }
}
