using System;
using System.Collections.Concurrent;

namespace GMCore
{
    public sealed class InMemoryGameplayNetworkTransport : IGameplayNetworkTransport
    {
        private static long s_nextConnectionID;

        private readonly ConcurrentQueue<GameplayNetworkTransportEvent> m_events =
            new ConcurrentQueue<GameplayNetworkTransportEvent>();
        private InMemoryGameplayNetworkTransport m_peer;
        private GameplayConnectionID m_connection;
        private bool m_serverStarted;
        private bool m_connected;
        private bool m_stopped;

        private InMemoryGameplayNetworkTransport() { }

        public static void CreatePair(
            out InMemoryGameplayNetworkTransport server,
            out InMemoryGameplayNetworkTransport client)
        {
            server = new InMemoryGameplayNetworkTransport();
            client = new InMemoryGameplayNetworkTransport();
            server.m_peer = client;
            client.m_peer = server;
        }

        public void StartServer()
        {
            ThrowIfStopped();
            m_serverStarted = true;
        }

        public void Connect()
        {
            ThrowIfStopped();
            if (m_connected)
            {
                throw new InvalidOperationException("In-memory transport is already connected");
            }
            if (m_peer == null || !m_peer.m_serverStarted || m_peer.m_stopped)
            {
                throw new InvalidOperationException("In-memory authority transport is not listening");
            }

            GameplayConnectionID connection = new GameplayConnectionID
            {
                Value = System.Threading.Interlocked.Increment(ref s_nextConnectionID),
            };
            m_connection = connection;
            m_peer.m_connection = connection;
            m_connected = true;
            m_peer.m_connected = true;
            m_events.Enqueue(new GameplayNetworkTransportEvent
            {
                Type = GameplayNetworkTransportEventType.Connected,
                Connection = connection,
            });
            m_peer.m_events.Enqueue(new GameplayNetworkTransportEvent
            {
                Type = GameplayNetworkTransportEventType.Connected,
                Connection = connection,
            });
        }

        public bool TryPollEvent(out GameplayNetworkTransportEvent networkEvent)
        {
            return m_events.TryDequeue(out networkEvent);
        }

        public void Send(GameplayConnectionID connection, byte[] data, GameplayNetworkDelivery delivery = GameplayNetworkDelivery.ReliableOrdered)
        {
            ThrowIfStopped();
            if (!m_connected || m_peer == null || !m_peer.m_connected || !connection.Equals(m_connection))
            {
                throw new InvalidOperationException($"In-memory connection {connection} is not active");
            }
            m_peer.m_events.Enqueue(new GameplayNetworkTransportEvent
            {
                Type = GameplayNetworkTransportEventType.Data,
                Connection = connection,
                Data = data == null ? null : (byte[])data.Clone(),
            });
        }

        public void Disconnect(GameplayConnectionID connection, string reason = null)
        {
            if (!m_connected || !connection.Equals(m_connection))
            {
                return;
            }
            m_connected = false;
            m_events.Enqueue(new GameplayNetworkTransportEvent
            {
                Type = GameplayNetworkTransportEventType.Disconnected,
                Connection = connection,
                Reason = reason,
            });
            if (m_peer != null && m_peer.m_connected)
            {
                m_peer.m_connected = false;
                m_peer.m_events.Enqueue(new GameplayNetworkTransportEvent
                {
                    Type = GameplayNetworkTransportEventType.Disconnected,
                    Connection = connection,
                    Reason = reason,
                });
            }
        }

        public void Stop()
        {
            if (m_stopped)
            {
                return;
            }
            if (m_connected)
            {
                Disconnect(m_connection, "Transport stopped");
            }
            m_serverStarted = false;
            m_stopped = true;
        }

        public void Dispose()
        {
            Stop();
        }

        private void ThrowIfStopped()
        {
            if (m_stopped)
            {
                throw new ObjectDisposedException(nameof(InMemoryGameplayNetworkTransport));
            }
        }
    }
}
