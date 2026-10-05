using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace GMCore
{
    public sealed class TcpGameplayNetworkTransport : IGameplayNetworkTransport
    {
        private sealed class ConnectionState : IDisposable
        {
            public GameplayConnectionID ID;
            public TcpClient Client;
            public NetworkStream Stream;
            public readonly ConcurrentQueue<byte[]> PendingSends = new ConcurrentQueue<byte[]>();
            public int SendLoopRunning;
            public int Disconnected;

            public void Dispose()
            {
                Stream?.Dispose();
                Client?.Dispose();
            }
        }

        private static long s_nextConnectionID;

        private readonly bool m_isServer;
        private readonly IPEndPoint m_serverEndpoint;
        private readonly string m_remoteHost;
        private readonly int m_remotePort;
        private readonly int m_maxMessageBytes;
        private readonly ConcurrentQueue<GameplayNetworkTransportEvent> m_events =
            new ConcurrentQueue<GameplayNetworkTransportEvent>();
        private readonly ConcurrentDictionary<GameplayConnectionID, ConnectionState> m_connections =
            new ConcurrentDictionary<GameplayConnectionID, ConnectionState>();
        private CancellationTokenSource m_cancellation;
        private TcpListener m_listener;
        private int m_started;

        private TcpGameplayNetworkTransport(IPEndPoint endpoint, int maxMessageBytes)
        {
            m_isServer = true;
            m_serverEndpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            m_maxMessageBytes = ValidateMaxMessageBytes(maxMessageBytes);
        }

        private TcpGameplayNetworkTransport(string host, int port, int maxMessageBytes)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("Remote host cannot be empty", nameof(host));
            }
            if (port <= 0 || port > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(port));
            }
            m_remoteHost = host;
            m_remotePort = port;
            m_maxMessageBytes = ValidateMaxMessageBytes(maxMessageBytes);
        }

        public static TcpGameplayNetworkTransport CreateServer(IPEndPoint endpoint, int maxMessageBytes = 16 * 1024 * 1024)
        {
            return new TcpGameplayNetworkTransport(endpoint, maxMessageBytes);
        }

        public static TcpGameplayNetworkTransport CreateClient(string host, int port, int maxMessageBytes = 16 * 1024 * 1024)
        {
            return new TcpGameplayNetworkTransport(host, port, maxMessageBytes);
        }

        public void StartServer()
        {
            if (!m_isServer)
            {
                throw new InvalidOperationException("This TCP transport was configured as a client");
            }
            StartOnce();
            m_listener = new TcpListener(m_serverEndpoint);
            m_listener.Start();
            _ = AcceptLoopAsync(m_cancellation.Token);
        }

        public void Connect()
        {
            if (m_isServer)
            {
                throw new InvalidOperationException("This TCP transport was configured as a server");
            }
            StartOnce();
            _ = ConnectAsync(m_cancellation.Token);
        }

        public bool TryPollEvent(out GameplayNetworkTransportEvent networkEvent)
        {
            return m_events.TryDequeue(out networkEvent);
        }

        public void Send(GameplayConnectionID connection, byte[] data, GameplayNetworkDelivery delivery = GameplayNetworkDelivery.ReliableOrdered)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            if (data.Length > m_maxMessageBytes)
            {
                throw new InvalidOperationException($"TCP gameplay message exceeds {m_maxMessageBytes} bytes");
            }
            ConnectionState state;
            if (!m_connections.TryGetValue(connection, out state))
            {
                throw new InvalidOperationException($"TCP gameplay connection {connection} is not active");
            }
            state.PendingSends.Enqueue((byte[])data.Clone());
            StartSendLoop(state);
        }

        public void Disconnect(GameplayConnectionID connection, string reason = null)
        {
            ConnectionState state;
            if (m_connections.TryRemove(connection, out state))
            {
                CloseConnection(state, reason ?? "Disconnected");
            }
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref m_started, 0) == 0)
            {
                return;
            }
            m_cancellation.Cancel();
            m_listener?.Stop();
            foreach (KeyValuePair<GameplayConnectionID, ConnectionState> pair in m_connections.ToArray())
            {
                if (m_connections.TryRemove(pair.Key, out ConnectionState state))
                {
                    CloseConnection(state, "Transport stopped");
                }
            }
            m_cancellation.Dispose();
            m_cancellation = null;
            m_listener = null;
        }

        public void Dispose()
        {
            Stop();
        }

        private async Task AcceptLoopAsync(CancellationToken cancellation)
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    TcpClient client = await m_listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    RegisterConnection(client);
                }
            }
            catch (ObjectDisposedException) when (cancellation.IsCancellationRequested) { }
            catch (SocketException) when (cancellation.IsCancellationRequested) { }
            catch (Exception exception)
            {
                m_events.Enqueue(new GameplayNetworkTransportEvent
                {
                    Type = GameplayNetworkTransportEventType.Disconnected,
                    Reason = exception.Message,
                });
            }
        }

        private async Task ConnectAsync(CancellationToken cancellation)
        {
            TcpClient client = new TcpClient();
            try
            {
                await client.ConnectAsync(m_remoteHost, m_remotePort).ConfigureAwait(false);
                if (cancellation.IsCancellationRequested)
                {
                    client.Dispose();
                    return;
                }
                RegisterConnection(client);
            }
            catch (Exception exception)
            {
                client.Dispose();
                if (!cancellation.IsCancellationRequested)
                {
                    m_events.Enqueue(new GameplayNetworkTransportEvent
                    {
                        Type = GameplayNetworkTransportEventType.Disconnected,
                        Reason = exception.Message,
                    });
                }
            }
        }

        private void RegisterConnection(TcpClient client)
        {
            client.NoDelay = true;
            GameplayConnectionID id = new GameplayConnectionID
            {
                Value = Interlocked.Increment(ref s_nextConnectionID),
            };
            ConnectionState state = new ConnectionState
            {
                ID = id,
                Client = client,
                Stream = client.GetStream(),
            };
            if (!m_connections.TryAdd(id, state))
            {
                state.Dispose();
                throw new InvalidOperationException($"TCP gameplay connection ID {id} is already active");
            }
            m_events.Enqueue(new GameplayNetworkTransportEvent
            {
                Type = GameplayNetworkTransportEventType.Connected,
                Connection = id,
            });
            _ = ReceiveLoopAsync(state, m_cancellation.Token);
        }

        private async Task ReceiveLoopAsync(ConnectionState state, CancellationToken cancellation)
        {
            byte[] header = new byte[sizeof(int)];
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    await ReadExactlyAsync(state.Stream, header, cancellation).ConfigureAwait(false);
                    int length = header[0]
                        | (header[1] << 8)
                        | (header[2] << 16)
                        | (header[3] << 24);
                    if (length < 0 || length > m_maxMessageBytes)
                    {
                        throw new InvalidOperationException($"TCP gameplay frame has invalid length {length}");
                    }
                    byte[] data = new byte[length];
                    await ReadExactlyAsync(state.Stream, data, cancellation).ConfigureAwait(false);
                    m_events.Enqueue(new GameplayNetworkTransportEvent
                    {
                        Type = GameplayNetworkTransportEventType.Data,
                        Connection = state.ID,
                        Data = data,
                    });
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception exception)
            {
                if (m_connections.TryRemove(state.ID, out ConnectionState removed))
                {
                    CloseConnection(removed, exception.Message);
                }
            }
        }

        private void StartSendLoop(ConnectionState state)
        {
            if (Interlocked.CompareExchange(ref state.SendLoopRunning, 1, 0) == 0)
            {
                _ = SendLoopAsync(state, m_cancellation.Token);
            }
        }

        private async Task SendLoopAsync(ConnectionState state, CancellationToken cancellation)
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    while (state.PendingSends.TryDequeue(out byte[] data))
                    {
                        int length = data.Length;
                        byte[] header =
                        {
                            (byte)length,
                            (byte)(length >> 8),
                            (byte)(length >> 16),
                            (byte)(length >> 24),
                        };
                        await state.Stream.WriteAsync(header, 0, header.Length, cancellation).ConfigureAwait(false);
                        await state.Stream.WriteAsync(data, 0, data.Length, cancellation).ConfigureAwait(false);
                    }

                    Interlocked.Exchange(ref state.SendLoopRunning, 0);
                    if (state.PendingSends.IsEmpty ||
                        Interlocked.CompareExchange(ref state.SendLoopRunning, 1, 0) != 0)
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception exception)
            {
                if (m_connections.TryRemove(state.ID, out ConnectionState removed))
                {
                    CloseConnection(removed, exception.Message);
                }
            }
        }

        private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellation)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, cancellation).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new InvalidOperationException("Remote TCP gameplay connection closed");
                }
                offset += read;
            }
        }

        private void CloseConnection(ConnectionState state, string reason)
        {
            if (Interlocked.Exchange(ref state.Disconnected, 1) != 0)
            {
                return;
            }
            state.Dispose();
            m_events.Enqueue(new GameplayNetworkTransportEvent
            {
                Type = GameplayNetworkTransportEventType.Disconnected,
                Connection = state.ID,
                Reason = reason,
            });
        }

        private void StartOnce()
        {
            if (Interlocked.CompareExchange(ref m_started, 1, 0) != 0)
            {
                throw new InvalidOperationException("TCP gameplay transport is already started");
            }
            m_cancellation = new CancellationTokenSource();
        }

        private static int ValidateMaxMessageBytes(int value)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            return value;
        }
    }
}
