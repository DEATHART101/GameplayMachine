using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using LiteNetLib;

namespace GMCore
{
    public sealed class LiteNetLibGameplayNetworkTransport : IGameplayNetworkTransport
    {
        private const byte ReliableChannel = 0;
        private const byte HeartbeatChannel = 1;
        private const string ConnectionKey = "GameplayMachine.UDP.1";
        private const int MaxDisconnectReasonBytes = 512;
        private static long s_nextConnectionID;

        private readonly bool m_isServer;
        private readonly IPEndPoint m_serverEndpoint;
        private readonly string m_remoteHost;
        private readonly int m_remotePort;
        private readonly EventBasedNetListener m_listener;
        private readonly NetManager m_manager;
        private readonly ConcurrentQueue<GameplayNetworkTransportEvent> m_events =
            new ConcurrentQueue<GameplayNetworkTransportEvent>();
        private readonly Dictionary<GameplayConnectionID, NetPeer> m_peers =
            new Dictionary<GameplayConnectionID, NetPeer>();
        private readonly Dictionary<int, GameplayConnectionID> m_connectionsByPeerID =
            new Dictionary<int, GameplayConnectionID>();
        private int m_started;

        private LiteNetLibGameplayNetworkTransport(IPEndPoint endpoint)
        {
            m_isServer = true;
            m_serverEndpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            m_listener = CreateListener();
            m_manager = CreateManager(m_listener);
        }

        private LiteNetLibGameplayNetworkTransport(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("Remote host cannot be empty", nameof(host));
            if (port <= 0 || port > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(port));
            m_remoteHost = host;
            m_remotePort = port;
            m_listener = CreateListener();
            m_manager = CreateManager(m_listener);
        }

        public static LiteNetLibGameplayNetworkTransport CreateServer(IPEndPoint endpoint) =>
            new LiteNetLibGameplayNetworkTransport(endpoint);

        public static LiteNetLibGameplayNetworkTransport CreateClient(string host, int port) =>
            new LiteNetLibGameplayNetworkTransport(host, port);

        public void StartServer()
        {
            if (!m_isServer)
                throw new InvalidOperationException("This UDP transport was configured as a client");
            StartOnce();
            if (!m_manager.Start(m_serverEndpoint.Port))
            {
                Interlocked.Exchange(ref m_started, 0);
                throw new InvalidOperationException($"Cannot start UDP server on port {m_serverEndpoint.Port}");
            }
        }

        public void Connect()
        {
            if (m_isServer)
                throw new InvalidOperationException("This UDP transport was configured as a server");
            StartOnce();
            if (!m_manager.Start())
            {
                Interlocked.Exchange(ref m_started, 0);
                throw new InvalidOperationException("Cannot start UDP client");
            }
            m_manager.Connect(m_remoteHost, m_remotePort, ConnectionKey);
        }

        public bool TryPollEvent(out GameplayNetworkTransportEvent networkEvent)
        {
            if (Volatile.Read(ref m_started) != 0)
                m_manager.PollEvents();
            return m_events.TryDequeue(out networkEvent);
        }

        public void Send(
            GameplayConnectionID connection,
            byte[] data,
            GameplayNetworkDelivery delivery = GameplayNetworkDelivery.ReliableOrdered)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            NetPeer peer;
            if (!m_peers.TryGetValue(connection, out peer))
                throw new InvalidOperationException($"UDP gameplay connection {connection} is not active");
            peer.Send(data,
                delivery == GameplayNetworkDelivery.ReliableOrdered ? ReliableChannel : HeartbeatChannel,
                ToLiteNetDelivery(delivery));
        }

        public void Disconnect(GameplayConnectionID connection, string reason = null)
        {
            NetPeer peer;
            if (m_peers.TryGetValue(connection, out peer))
            {
                if (string.IsNullOrEmpty(reason))
                {
                    m_manager.DisconnectPeer(peer);
                    return;
                }

                byte[] reasonBytes = Encoding.UTF8.GetBytes(reason);
                if (reasonBytes.Length > MaxDisconnectReasonBytes)
                    Array.Resize(ref reasonBytes, MaxDisconnectReasonBytes);
                m_manager.DisconnectPeer(peer, reasonBytes);
            }
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref m_started, 0) == 0)
                return;
            m_manager.Stop();
            m_peers.Clear();
            m_connectionsByPeerID.Clear();
        }

        public void Dispose() => Stop();

        private EventBasedNetListener CreateListener()
        {
            var listener = new EventBasedNetListener();
            listener.ConnectionRequestEvent += request =>
            {
                if (m_isServer)
                    request.AcceptIfKey(ConnectionKey);
                else
                    request.Reject();
            };
            listener.PeerConnectedEvent += peer =>
            {
                var connection = new GameplayConnectionID
                {
                    Value = Interlocked.Increment(ref s_nextConnectionID),
                };
                m_peers.Add(connection, peer);
                m_connectionsByPeerID.Add(peer.Id, connection);
                m_events.Enqueue(new GameplayNetworkTransportEvent
                {
                    Type = GameplayNetworkTransportEventType.Connected,
                    Connection = connection,
                });
            };
            listener.PeerDisconnectedEvent += (peer, info) =>
            {
                GameplayConnectionID connection;
                if (!m_connectionsByPeerID.TryGetValue(peer.Id, out connection))
                    return;
                m_connectionsByPeerID.Remove(peer.Id);
                m_peers.Remove(connection);
                string reason = info.Reason.ToString();
                if (info.AdditionalData != null)
                {
                    byte[] reasonBytes = info.AdditionalData.GetRemainingBytes();
                    if (reasonBytes.Length > 0)
                        reason = Encoding.UTF8.GetString(reasonBytes);
                }
                m_events.Enqueue(new GameplayNetworkTransportEvent
                {
                    Type = GameplayNetworkTransportEventType.Disconnected,
                    Connection = connection,
                    Reason = reason,
                });
            };
            listener.NetworkReceiveEvent += (peer, reader, channel, delivery) =>
            {
                GameplayConnectionID connection;
                if (m_connectionsByPeerID.TryGetValue(peer.Id, out connection))
                {
                    m_events.Enqueue(new GameplayNetworkTransportEvent
                    {
                        Type = GameplayNetworkTransportEventType.Data,
                        Connection = connection,
                        Data = reader.GetRemainingBytes(),
                    });
                }
                reader.Recycle();
            };
            return listener;
        }

        private static NetManager CreateManager(EventBasedNetListener listener) => new NetManager(listener)
        {
            AutoRecycle = false,
            UnsyncedEvents = false,
            UnsyncedReceiveEvent = false,
            IPv6Enabled = true,
        };

        private static DeliveryMethod ToLiteNetDelivery(GameplayNetworkDelivery delivery)
        {
            switch (delivery)
            {
                case GameplayNetworkDelivery.ReliableOrdered:
                    return DeliveryMethod.ReliableOrdered;
                case GameplayNetworkDelivery.UnreliableSequenced:
                    return DeliveryMethod.Sequenced;
                default:
                    throw new ArgumentOutOfRangeException(nameof(delivery));
            }
        }

        private void StartOnce()
        {
            if (Interlocked.CompareExchange(ref m_started, 1, 0) != 0)
                throw new InvalidOperationException("UDP gameplay transport is already started");
        }
    }
}
