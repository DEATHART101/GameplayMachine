using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GMCore
{
    internal sealed class GameplayNetworkMessageChannel
    {
        private readonly struct FragmentKey : IEquatable<FragmentKey>
        {
            public readonly GameplayConnectionID Connection;
            public readonly long MessageID;

            public FragmentKey(GameplayConnectionID connection, long messageID)
            {
                Connection = connection;
                MessageID = messageID;
            }

            public bool Equals(FragmentKey other) =>
                Connection.Equals(other.Connection) && MessageID == other.MessageID;
            public override bool Equals(object obj) => obj is FragmentKey other && Equals(other);
            public override int GetHashCode() => (Connection.GetHashCode() * 397) ^ MessageID.GetHashCode();
        }

        private sealed class FragmentAccumulator
        {
            public byte[][] Parts;
            public int TotalLength;
            public int ReceivedCount;
            public int ReceivedBytes;
            public DateTime LastUpdatedUtc;
        }

        private readonly IGameplayNetworkTransport m_transport;
        private readonly GameplayNetworkOptions m_options;
        private readonly Dictionary<FragmentKey, FragmentAccumulator> m_fragments =
            new Dictionary<FragmentKey, FragmentAccumulator>();
        private long m_nextMessageID;

        public GameplayNetworkMessageChannel(
            IGameplayNetworkTransport transport,
            GameplayNetworkOptions options)
        {
            m_transport = transport ?? throw new ArgumentNullException(nameof(transport));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public void Send(
            GameplayConnectionID connection,
            byte[] message,
            GameplayNetworkDelivery delivery = GameplayNetworkDelivery.ReliableOrdered)
        {
            if (message == null || message.Length > m_options.MaxMessageBytes)
                throw new InvalidOperationException(
                    $"Gameplay network message exceeds {m_options.MaxMessageBytes} bytes");
            if (message.Length <= m_options.FragmentPayloadBytes)
            {
                m_transport.Send(connection, message, delivery);
                return;
            }
            if (delivery != GameplayNetworkDelivery.ReliableOrdered)
                throw new InvalidOperationException(
                    "Fragmented gameplay messages must use reliable ordered delivery");

            long messageID = ++m_nextMessageID;
            int count = (message.Length + m_options.FragmentPayloadBytes - 1) /
                        m_options.FragmentPayloadBytes;
            if (count > m_options.MaxFragmentsPerMessage)
                throw new InvalidOperationException(
                    $"Gameplay network message requires {count} fragments; maximum is {m_options.MaxFragmentsPerMessage}");
            for (int index = 0; index < count; index++)
            {
                int offset = index * m_options.FragmentPayloadBytes;
                int length = Math.Min(m_options.FragmentPayloadBytes, message.Length - offset);
                m_transport.Send(connection, GameplayNetworkFragmentCodec.Write(
                    messageID, index, count, message.Length, message, offset, length), delivery);
            }
        }

        public bool TryReceive(GameplayConnectionID connection, byte[] data, out byte[] message)
        {
            Cleanup();
            if (!GameplayNetworkFragmentCodec.TryRead(data, m_options.MaxMessageBytes,
                    out GameplayNetworkFragment fragment))
            {
                message = data;
                return true;
            }
            if (fragment.Count > m_options.MaxFragmentsPerMessage)
                throw new InvalidDataException(
                    $"Gameplay network message declares {fragment.Count} fragments; maximum is {m_options.MaxFragmentsPerMessage}");

            var key = new FragmentKey(connection, fragment.MessageID);
            if (!m_fragments.TryGetValue(key, out FragmentAccumulator accumulator))
            {
                if (m_fragments.Count >= m_options.MaxPendingFragmentedMessages)
                    throw new InvalidDataException("Too many fragmented gameplay messages are pending");
                accumulator = new FragmentAccumulator
                {
                    Parts = new byte[fragment.Count][],
                    TotalLength = fragment.TotalLength,
                    LastUpdatedUtc = DateTime.UtcNow,
                };
                m_fragments.Add(key, accumulator);
            }
            else if (accumulator.Parts.Length != fragment.Count ||
                     accumulator.TotalLength != fragment.TotalLength)
            {
                throw new InvalidDataException(
                    "Gameplay network fragment metadata changed during reassembly");
            }

            accumulator.LastUpdatedUtc = DateTime.UtcNow;
            if (accumulator.Parts[fragment.Index] == null)
            {
                accumulator.Parts[fragment.Index] = fragment.Payload;
                accumulator.ReceivedCount++;
                accumulator.ReceivedBytes += fragment.Payload.Length;
            }
            if (accumulator.ReceivedBytes > accumulator.TotalLength)
            {
                m_fragments.Remove(key);
                throw new InvalidDataException(
                    "Gameplay network fragments exceed the declared message size");
            }
            if (accumulator.ReceivedCount != accumulator.Parts.Length)
            {
                message = null;
                return false;
            }
            if (accumulator.ReceivedBytes != accumulator.TotalLength)
            {
                m_fragments.Remove(key);
                throw new InvalidDataException(
                    "Gameplay network fragments do not match the declared message size");
            }

            message = new byte[accumulator.TotalLength];
            int destinationOffset = 0;
            foreach (byte[] part in accumulator.Parts)
            {
                Buffer.BlockCopy(part, 0, message, destinationOffset, part.Length);
                destinationOffset += part.Length;
            }
            m_fragments.Remove(key);
            return true;
        }

        public void Discard(GameplayConnectionID connection)
        {
            foreach (FragmentKey key in m_fragments.Keys
                         .Where(item => item.Connection.Equals(connection)).ToArray())
                m_fragments.Remove(key);
        }

        private void Cleanup()
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(-m_options.FragmentTimeoutMilliseconds);
            foreach (FragmentKey key in m_fragments
                         .Where(item => item.Value.LastUpdatedUtc < deadline)
                         .Select(item => item.Key).ToArray())
                m_fragments.Remove(key);
        }
    }
}
