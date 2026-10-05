using System;

namespace XLockstep
{
    [Serializable]
    public readonly struct LockstepTick : IEquatable<LockstepTick>, IComparable<LockstepTick>
    {
        public long Value { get; }
        public LockstepTick(long value) { Value = value; }
        public static LockstepTick Zero => new LockstepTick(0);
        public int CompareTo(LockstepTick other) => Value.CompareTo(other.Value);
        public bool Equals(LockstepTick other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is LockstepTick other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
        public static LockstepTick operator +(LockstepTick value, long offset) => new LockstepTick(checked(value.Value + offset));
        public static LockstepTick operator -(LockstepTick value, long offset) => new LockstepTick(checked(value.Value - offset));
        public static bool operator ==(LockstepTick left, LockstepTick right) => left.Equals(right);
        public static bool operator !=(LockstepTick left, LockstepTick right) => !left.Equals(right);
        public static bool operator <(LockstepTick left, LockstepTick right) => left.Value < right.Value;
        public static bool operator >(LockstepTick left, LockstepTick right) => left.Value > right.Value;
        public static bool operator <=(LockstepTick left, LockstepTick right) => left.Value <= right.Value;
        public static bool operator >=(LockstepTick left, LockstepTick right) => left.Value >= right.Value;
    }

    [Serializable]
    public sealed class LockstepCommand
    {
        public long CommandId { get; set; }
        public int PlayerId { get; set; }
        public long Sequence { get; set; }
        public LockstepTick RequestedTick { get; set; }
        public LockstepTick AcceptedTick { get; set; }
        public ulong TypeId { get; set; }
        public byte[] Payload { get; set; } = Array.Empty<byte>();

        public LockstepCommand CloneForPlayer(int playerId, LockstepTick acceptedTick) => new LockstepCommand
        {
            CommandId = CommandId,
            PlayerId = playerId,
            Sequence = Sequence,
            RequestedTick = RequestedTick,
            AcceptedTick = acceptedTick,
            TypeId = TypeId,
            Payload = (byte[])Payload.Clone(),
        };
    }

    [Serializable]
    public sealed class LockstepFrame
    {
        public LockstepTick Tick { get; set; }
        public LockstepCommand[] Commands { get; set; } = Array.Empty<LockstepCommand>();
    }

    public readonly struct LockstepInputAcceptance
    {
        public long CommandId { get; }
        public LockstepTick RequestedTick { get; }
        public LockstepTick AcceptedTick { get; }
        public bool Retargeted => RequestedTick != AcceptedTick;

        public LockstepInputAcceptance(long commandId, LockstepTick requestedTick, LockstepTick acceptedTick)
        {
            CommandId = commandId;
            RequestedTick = requestedTick;
            AcceptedTick = acceptedTick;
        }
    }
}
