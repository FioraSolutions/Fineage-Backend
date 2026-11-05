namespace Fineage.Net.Contract
{
    /// <summary>Authoritative entity reference. Generation guards stale IDs after reuse.</summary>
    public readonly struct EntityId : System.IEquatable<EntityId>
    {
        public EntityId(ulong value, uint generation)
        {
            Value = value;
            Generation = generation;
        }

        public ulong Value { get; }
        public uint Generation { get; }

        public static EntityId FromObjectId(int objectId, uint generation = 1) =>
            new((ulong)(uint)objectId, generation);

        public int ToObjectId() => (int)Value;

        public bool Equals(EntityId other) => Value == other.Value && Generation == other.Generation;
        public override bool Equals(object? obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => System.HashCode.Combine(Value, Generation);
        public override string ToString() => $"{Value}g{Generation}";
    }
}
