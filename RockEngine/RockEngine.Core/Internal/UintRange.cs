namespace RockEngine.Core.Internal 
{

    /// <summary>
    /// Represents a range of uint values with a start and end index.
    /// Mimics the behavior of System.Range but for uint instead of int.
    /// </summary>
    public readonly struct UIntRange : IEquatable<UIntRange>, IComparable<UIntRange>
    {
        /// <summary>
        /// Gets the inclusive start index of the range.
        /// </summary>
        public uint Start { get; }

        /// <summary>
        /// Gets the exclusive end index of the range.
        /// </summary>
        public uint End { get; }

        /// <summary>
        /// Gets the length of the range (End - Start).
        /// </summary>
        public uint Length => End > Start ? End - Start : 0;

        /// <summary>
        /// Gets a value indicating whether the range is empty (Start == End).
        /// </summary>
        public bool IsEmpty => Start == End;

        /// <summary>
        /// Initializes a new instance of the <see cref="UIntRange"/> struct with the specified start and end indices.
        /// </summary>
        /// <param name="start">The inclusive start index of the range.</param>
        /// <param name="end">The exclusive end index of the range.</param>
        public UIntRange(uint start, uint end)
        {
            Start = start;
            End = end;
        }

        /// <summary>
        /// Creates a UIntRange that starts from the beginning to the specified end value.
        /// </summary>
        /// <param name="end">The exclusive end index of the range.</param>
        /// <returns>A UIntRange from the beginning to the specified end.</returns>
        public static UIntRange EndAt(uint end) => new UIntRange(0, end);

        /// <summary>
        /// Creates a UIntRange that starts from the specified value to the end.
        /// </summary>
        /// <param name="start">The inclusive start index of the range.</param>
        /// <returns>A UIntRange from the specified start to the end.</returns>
        public static UIntRange StartAt(uint start) => new UIntRange(start, uint.MaxValue);

        /// <summary>
        /// Gets a UIntRange that represents the entire range.
        /// </summary>
        public static UIntRange All => new UIntRange(0, uint.MaxValue);

        /// <summary>
        /// Deconstructs the range into its start and end components.
        /// </summary>
        /// <param name="start">The start index of the range.</param>
        /// <param name="end">The end index of the range.</param>
        public void Deconstruct(out uint start, out uint end)
        {
            start = Start;
            end = End;
        }

        /// <summary>
        /// Returns the offset and length of the range if used with a collection of the specified length.
        /// </summary>
        /// <param name="length">The length of the collection.</param>
        /// <returns>A tuple containing the offset and length.</returns>
        public (uint offset, uint length) GetOffsetAndLength(uint length)
        {
            if (End < Start || Start > length || End > length)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            return (Start, End - Start);
        }

        /// <summary>
        /// Determines whether the range contains the specified value.
        /// </summary>
        /// <param name="value">The value to check.</param>
        /// <returns>true if the range contains the value; otherwise, false.</returns>
        public bool Contains(uint value) => value >= Start && value <= End;

        /// <summary>
        /// Determines whether the range contains the specified range.
        /// </summary>
        /// <param name="other">The range to check.</param>
        /// <returns>true if this range contains the other range; otherwise, false.</returns>
        public bool Contains(UIntRange other) => Start <= other.Start && End >= other.End;

        /// <summary>
        /// Determines whether the range overlaps with the specified range.
        /// </summary>
        /// <param name="other">The range to check.</param>
        /// <returns>true if the ranges overlap; otherwise, false.</returns>
        public bool Overlaps(UIntRange other) => Start < other.End && other.Start < End;

        /// <summary>
        /// Returns a string that represents the current range.
        /// </summary>
        /// <returns>A string that represents the current range.</returns>
        public override string ToString() => $"{Start}..{End}";

        /// <summary>
        /// Determines whether the specified object is equal to the current range.
        /// </summary>
        /// <param name="obj">The object to compare with the current range.</param>
        /// <returns>true if the specified object is equal to the current range; otherwise, false.</returns>
        public override bool Equals(object? obj) => obj is UIntRange range && Equals(range);

        /// <summary>
        /// Determines whether the specified range is equal to the current range.
        /// </summary>
        /// <param name="other">The range to compare with the current range.</param>
        /// <returns>true if the specified range is equal to the current range; otherwise, false.</returns>
        public bool Equals(UIntRange other) => Start == other.Start && End == other.End;

        /// <summary>
        /// Returns the hash code for this range.
        /// </summary>
        /// <returns>A hash code for the current range.</returns>
        public override int GetHashCode() => HashCode.Combine(Start, End);

        /// <summary>
        /// Compares the current range with another range.
        /// Prioritizes ranges that cover more binding points (wider ranges come first),
        /// then compares by start position.
        /// </summary>
        public int CompareTo(UIntRange other)
        {
            // Prefer wider ranges (more binding points)
            int lengthComparison = other.Length.CompareTo(Length);
            if (lengthComparison != 0)
            {
                return lengthComparison;
            }

            // If same width, prefer lower start positions
            int startComparison = Start.CompareTo(other.Start);
            if (startComparison != 0)
            {
                return startComparison;
            }

            // If same start, compare by end
            return End.CompareTo(other.End);
        }

        public static bool operator ==(UIntRange left, UIntRange right) => left.Equals(right);
        public static bool operator !=(UIntRange left, UIntRange right) => !(left == right);
        public static bool operator <(UIntRange left, UIntRange right) => left.CompareTo(right) < 0;
        public static bool operator >(UIntRange left, UIntRange right) => left.CompareTo(right) > 0;
        public static bool operator <=(UIntRange left, UIntRange right) => left.CompareTo(right) <= 0;
        public static bool operator >=(UIntRange left, UIntRange right) => left.CompareTo(right) >= 0;
    }
}