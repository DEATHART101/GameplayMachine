using System;
using System.Collections.Generic;
using System.Linq;

namespace XLockstep
{
    /// <summary>Assigns authenticated inputs to open ticks and seals them in canonical order.</summary>
    public sealed class LockstepFrameCoordinator
    {
        private readonly SortedDictionary<long, List<LockstepCommand>> _pending =
            new SortedDictionary<long, List<LockstepCommand>>();
        private readonly Dictionary<int, long> _lastSequenceByPlayer = new Dictionary<int, long>();

        public LockstepTick NextOpenTick { get; private set; } = new LockstepTick(1);
        public int MaximumFutureTicks { get; }
        public int MaximumPayloadBytes { get; }
        public int MaximumCommandsPerFrame { get; }
        public bool HasCommandsForNextFrame =>
            _pending.TryGetValue(NextOpenTick.Value, out List<LockstepCommand>? commands) && commands.Count != 0;

        public LockstepFrameCoordinator(
            int maximumFutureTicks = 256,
            int maximumPayloadBytes = 64 * 1024,
            int maximumCommandsPerFrame = 4096)
        {
            if (maximumFutureTicks <= 0 || maximumPayloadBytes <= 0 || maximumCommandsPerFrame <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumFutureTicks));
            MaximumFutureTicks = maximumFutureTicks;
            MaximumPayloadBytes = maximumPayloadBytes;
            MaximumCommandsPerFrame = maximumCommandsPerFrame;
        }

        public LockstepInputAcceptance Accept(int authenticatedPlayerId, LockstepCommand submitted,
            bool forceNextOpenTick = false)
        {
            if (authenticatedPlayerId < 0)
                throw new ArgumentOutOfRangeException(nameof(authenticatedPlayerId));
            if (submitted == null)
                throw new ArgumentNullException(nameof(submitted));
            if (submitted.CommandId <= 0 || submitted.Sequence <= 0 || submitted.TypeId == 0)
                throw new InvalidOperationException("Lockstep command identity is invalid");
            if (submitted.Payload == null || submitted.Payload.Length > MaximumPayloadBytes)
                throw new InvalidOperationException("Lockstep command payload is invalid");
            if (_lastSequenceByPlayer.TryGetValue(authenticatedPlayerId, out long lastSequence) &&
                submitted.Sequence <= lastSequence)
                throw new InvalidOperationException("Duplicate or out-of-order lockstep input sequence");

            long requested = submitted.RequestedTick.Value;
            long accepted = forceNextOpenTick ? NextOpenTick.Value : Math.Max(requested, NextOpenTick.Value);
            if (accepted > checked(NextOpenTick.Value + MaximumFutureTicks))
            {
                throw new InvalidOperationException("Lockstep command is too far in the future");
            }

            if (!_pending.TryGetValue(accepted, out List<LockstepCommand>? commands))
            {
                commands = new List<LockstepCommand>();
                _pending.Add(accepted, commands);
            }
            if (commands.Count >= MaximumCommandsPerFrame)
            {
                throw new InvalidOperationException("Lockstep frame command limit exceeded");
            }

            LockstepTick acceptedTick = new LockstepTick(accepted);
            commands.Add(submitted.CloneForPlayer(authenticatedPlayerId, acceptedTick));
            _lastSequenceByPlayer[authenticatedPlayerId] = submitted.Sequence;
            return new LockstepInputAcceptance(submitted.CommandId, submitted.RequestedTick, acceptedTick);
        }

        public LockstepFrame SealNextFrame()
        {
            long tick = NextOpenTick.Value;
            _pending.TryGetValue(tick, out List<LockstepCommand>? commands);
            _pending.Remove(tick);
            LockstepCommand[] ordered = (commands ?? new List<LockstepCommand>())
                .OrderBy(item => item.PlayerId)
                .ThenBy(item => item.Sequence)
                .ThenBy(item => item.TypeId)
                .ToArray();
            NextOpenTick = NextOpenTick + 1;
            return new LockstepFrame { Tick = new LockstepTick(tick), Commands = ordered };
        }
    }
}
