using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XLockstep;

namespace GMCore
{
    /// <summary>
    /// Orders opaque player inputs into deterministic frames. The relay never creates a
    /// GameplayMachine, loads an OD module, deserializes an interface, or simulates game state.
    /// </summary>
    public sealed class GameplayLockstepRelayServer : IDisposable
    {
        private sealed class Peer
        {
            public int PlayerSlot;
            public ulong InitialStateHash;
            public bool InitialStateHashReceived;
        }

        private readonly IGameplayNetworkTransport m_transport;
        private readonly GameplayLockstepOptions m_options;
        private readonly GameplayNetworkMessageChannel m_messages;
        private readonly XLogger.Logger m_logger;
        private readonly HashSet<GameplayConnectionID> m_connected =
            new HashSet<GameplayConnectionID>();
        private readonly Dictionary<GameplayConnectionID, Peer> m_peers =
            new Dictionary<GameplayConnectionID, Peer>();
        private readonly LockstepFrameCoordinator m_coordinator = new LockstepFrameCoordinator();
        private HashSet<ulong> m_allowedInterfaceTypes;
        private ulong m_schemaID;
        private ulong m_resourceCatalogID;
        private long m_lastBroadcastTick;
        private long m_heartbeatSequence;
        private DateTime m_nextTickUtc;
        private DateTime m_nextEmptyFrameFlushUtc;
        private DateTime m_nextHeartbeatUtc;
        private bool m_welcomeSent;
        private bool m_matchStarted;
        private bool m_disposed;
        private GameplayLockstepRecordingWriter m_recording;

        public GameplayNetworkState State { get; private set; } = GameplayNetworkState.Disabled;
        public long CurrentTick => m_lastBroadcastTick;
        public int ConnectedPlayerCount => m_peers.Count;
        public event Action<GameplayNetworkState, string> StateChanged;

        public GameplayLockstepRelayServer(
            IGameplayNetworkTransport transport,
            GameplayLockstepOptions options = null)
        {
            m_transport = transport ?? throw new ArgumentNullException(nameof(transport));
            m_options = options ?? new GameplayLockstepOptions();
            ValidateOptions(m_options);
            m_logger = m_options.Logger;
            m_messages = new GameplayNetworkMessageChannel(m_transport, m_options);
        }

        public void Start()
        {
            ThrowIfDisposed();
            if (State != GameplayNetworkState.Disabled)
                throw new InvalidOperationException("Lockstep relay is already started");
            m_transport.StartServer();
            if (!string.IsNullOrWhiteSpace(m_options.RecordingDirectory))
                m_recording = new GameplayLockstepRecordingWriter(m_options.RecordingDirectory, "relay");
            m_logger?.Log(XLogger.LogVerbosity.Common, LogCatagories.LockstepRelay,
                $"Relay started. Expected players={m_options.ExpectedPlayerCount}, tick mode={m_options.TickMode}, tick interval={m_options.TickIntervalMilliseconds}ms");
            SetState(GameplayNetworkState.Connecting, null);
        }

        public void Update()
        {
            ThrowIfDisposed();
            GameplayNetworkTransportEvent networkEvent;
            while (m_transport.TryPollEvent(out networkEvent))
            {
                try
                {
                    switch (networkEvent.Type)
                    {
                        case GameplayNetworkTransportEventType.Connected:
                            if (m_welcomeSent)
                            {
                                m_logger?.LogWarning(XLogger.LogVerbosity.Common,
                                    LogCatagories.LockstepRelay,
                                    $"Rejected connection {networkEvent.Connection}: match already started");
                                m_transport.Disconnect(networkEvent.Connection,
                                    "Lockstep match is full or already started");
                            }
                            else
                            {
                                m_connected.Add(networkEvent.Connection);
                                m_logger?.Log(XLogger.LogVerbosity.Common,
                                    LogCatagories.LockstepRelay,
                                    $"Transport connected: {networkEvent.Connection}. Pending handshakes={m_connected.Count - m_peers.Count}");
                            }
                            break;
                        case GameplayNetworkTransportEventType.Disconnected:
                            Disconnect(networkEvent.Connection, networkEvent.Reason);
                            break;
                        case GameplayNetworkTransportEventType.Data:
                            if (m_messages.TryReceive(networkEvent.Connection, networkEvent.Data,
                                    out byte[] message))
                                ProcessMessage(networkEvent.Connection,
                                    LockstepProtocol.Read(message, m_options.MaxMessageBytes));
                            break;
                    }
                }
                catch (Exception exception)
                {
                    m_logger?.LogError(XLogger.LogVerbosity.Common,
                        LogCatagories.LockstepRelay,
                        $"Peer {networkEvent.Connection} protocol failure: {exception}");
                    m_transport.Disconnect(networkEvent.Connection, exception.Message);
                    Disconnect(networkEvent.Connection, exception.Message);
                }
            }

            if (!m_matchStarted)
                return;
            DateTime now = DateTime.UtcNow;
            if (m_options.TickMode == GameplayLockstepTickMode.InputDriven)
            {
                if (m_coordinator.HasCommandsForNextFrame)
                    SealAndBroadcastInputDrivenFrame();
            }
            else
                AdvanceFixedRate(now);
            SendHeartbeatIfDue(now);
        }

        private void ProcessMessage(GameplayConnectionID connection, LockstepMessage message)
        {
            switch (message.Kind)
            {
                case LockstepMessageKind.Hello:
                    ProcessHello(connection, message);
                    break;
                case LockstepMessageKind.SubmitInput:
                    ProcessInput(connection,
                        message.Command ?? throw new InvalidDataException("Input is missing"));
                    break;
                case LockstepMessageKind.StateHash:
                    ProcessStateHash(connection, message);
                    break;
                case LockstepMessageKind.Pong:
                    break;
                case LockstepMessageKind.Ping:
                    m_messages.Send(connection, LockstepProtocol.Write(new LockstepMessage
                    {
                        Kind = LockstepMessageKind.Pong,
                        Sequence = message.Sequence,
                    }), GameplayNetworkDelivery.UnreliableSequenced);
                    break;
                default:
                    throw new InvalidDataException(
                        $"Peer sent invalid Lockstep message {message.Kind}");
            }
        }

        private void ProcessHello(GameplayConnectionID connection, LockstepMessage message)
        {
            if (!m_connected.Contains(connection) || m_peers.ContainsKey(connection))
                throw new InvalidOperationException("Duplicate or unknown Lockstep connection");
            if (m_welcomeSent || m_peers.Count >= m_options.ExpectedPlayerCount)
                throw new InvalidOperationException("Lockstep match is full or already started");

            int requestedSlot = GameplayLockstepConnectionRequestCodec.ReadRequestedSlot(message.Data);
            if (requestedSlot < 0 || requestedSlot >= m_options.ExpectedPlayerCount)
            {
                RejectConnection(connection,
                    $"Requested slot {requestedSlot} does not exist; valid slots are 0 through {m_options.ExpectedPlayerCount - 1}");
                return;
            }
            if (m_peers.Values.Any(item => item.PlayerSlot == requestedSlot))
            {
                RejectConnection(connection, $"Requested slot {requestedSlot} is already occupied");
                return;
            }

            ulong[] interfaceTypes = (message.InterfaceTypeIds ?? Array.Empty<ulong>())
                .OrderBy(item => item).ToArray();
            if (interfaceTypes.Any(item => item == 0) ||
                interfaceTypes.Distinct().Count() != interfaceTypes.Length)
                throw new InvalidDataException("Lockstep interface manifest is invalid");
            if (m_peers.Count == 0)
            {
                m_schemaID = message.SchemaId;
                m_resourceCatalogID = message.ResourceCatalogId;
                m_allowedInterfaceTypes = new HashSet<ulong>(interfaceTypes);
            }
            else
            {
                if (message.SchemaId != m_schemaID)
                    throw new InvalidDataException("Lockstep network schema does not match the other peers");
                if (message.ResourceCatalogId != m_resourceCatalogID)
                    throw new InvalidDataException("Lockstep resource catalog does not match the other peers");
                if (!m_allowedInterfaceTypes.SetEquals(interfaceTypes))
                    throw new InvalidDataException("Lockstep interface manifest does not match the other peers");
            }

            m_peers.Add(connection, new Peer { PlayerSlot = requestedSlot });
            m_logger?.Log(XLogger.LogVerbosity.Common, LogCatagories.LockstepRelay,
                $"Peer handshake accepted: {connection}. Slot={requestedSlot}, players={m_peers.Count}/{m_options.ExpectedPlayerCount}, schema={m_schemaID}, resources={m_resourceCatalogID}");
            if (m_peers.Count == m_options.ExpectedPlayerCount)
                SendWelcome();
        }

        private void SendWelcome()
        {
            foreach (KeyValuePair<GameplayConnectionID, Peer> pair in
                     m_peers.OrderBy(item => item.Value.PlayerSlot))
            {
                m_messages.Send(pair.Key, LockstepProtocol.Write(new LockstepMessage
                {
                    Kind = LockstepMessageKind.Welcome,
                    PlayerId = pair.Value.PlayerSlot,
                    PlayerCount = m_options.ExpectedPlayerCount,
                    Tick = LockstepTick.Zero,
                    InputDelay = m_options.InputDelayTicks,
                    MaximumEmptyTicks = m_options.MaximumEmptyTicksPerMessage,
                    StateHashIntervalTicks = m_options.StateHashIntervalTicks,
                    FullStateHashIntervalTicks = m_options.FullStateHashIntervalTicks,
                }));
            }
            m_welcomeSent = true;
            m_logger?.Log(XLogger.LogVerbosity.Common, LogCatagories.LockstepRelay,
                $"Assigned {m_peers.Count} player slots. Waiting for initial state hashes");
            SetState(GameplayNetworkState.Synchronizing, null);
        }

        private void ProcessInitialStateHash(Peer peer, LockstepMessage message)
        {
            if (message.Tick != LockstepTick.Zero ||
                message.StateHashKind != LockstepStateHashKind.FullAudit)
                throw new InvalidDataException("Peer supplied an invalid initial Lockstep state hash");
            peer.InitialStateHash = message.StateHash;
            peer.InitialStateHashReceived = true;
            if (m_peers.Values.Any(item => !item.InitialStateHashReceived))
                return;
            if (m_peers.Values.Select(item => item.InitialStateHash).Distinct().Count() != 1)
            {
                StopMatch("Initial Lockstep state differs between peers");
                return;
            }

            m_matchStarted = true;
            m_nextTickUtc = DateTime.UtcNow.AddMilliseconds(m_options.TickIntervalMilliseconds);
            m_nextEmptyFrameFlushUtc = DateTime.UtcNow.AddMilliseconds(
                m_options.EmptyFrameFlushMilliseconds);
            m_nextHeartbeatUtc = DateTime.UtcNow.AddMilliseconds(m_options.HeartbeatMilliseconds);
            m_lastBroadcastTick = 0;
            Broadcast(new LockstepMessage { Kind = LockstepMessageKind.Ready });
            m_logger?.Log(XLogger.LogVerbosity.Common, LogCatagories.LockstepRelay,
                $"Match ready. Players={m_peers.Count}, initial state hash={m_peers.Values.First().InitialStateHash}");
            SetState(GameplayNetworkState.Ready, null);
        }

        private void ProcessStateHash(GameplayConnectionID connection, LockstepMessage message)
        {
            if (!m_peers.TryGetValue(connection, out Peer peer) || !m_welcomeSent)
                throw new InvalidOperationException("Lockstep state hash arrived before Welcome");
            if (!m_matchStarted)
            {
                ProcessInitialStateHash(peer, message);
                return;
            }
            if (message.Tick.Value <= 0 || message.Tick.Value > m_lastBroadcastTick)
                throw new InvalidDataException("Peer supplied a Lockstep state hash for an invalid tick");
            message.PlayerId = peer.PlayerSlot;
            Broadcast(message);
        }

        private void ProcessInput(GameplayConnectionID connection, LockstepCommand command)
        {
            if (!m_matchStarted || !m_peers.TryGetValue(connection, out Peer peer))
                throw new InvalidOperationException("Lockstep input was submitted before Ready");
            if (!m_allowedInterfaceTypes.Contains(command.TypeId))
                throw new InvalidDataException($"Unknown Lockstep interface {command.TypeId}");
            LockstepInputAcceptance accepted = m_coordinator.Accept(
                peer.PlayerSlot, command,
                m_options.TickMode == GameplayLockstepTickMode.InputDriven);
            m_logger?.Log(XLogger.LogVerbosity.VeryVerbose, LogCatagories.LockstepRelay,
                $"Accepted input {accepted.CommandId} from player {peer.PlayerSlot}: requested tick={accepted.RequestedTick}, accepted tick={accepted.AcceptedTick}, type={command.TypeId}");
            m_messages.Send(connection, LockstepProtocol.Write(new LockstepMessage
            {
                Kind = LockstepMessageKind.InputAccepted,
                CommandId = accepted.CommandId,
                RequestedTick = accepted.RequestedTick,
                Tick = accepted.AcceptedTick,
            }));
        }

        private void AdvanceFixedRate(DateTime now)
        {
            int catchUp = 0;
            while (now >= m_nextTickUtc && catchUp++ < m_options.MaximumCatchUpTicksPerUpdate)
            {
                LockstepFrame frame = m_coordinator.SealNextFrame();
                if (frame.Commands.Length != 0)
                {
                    FlushEmptyTicksThrough(frame.Tick.Value - 1, now);
                    BroadcastFrame(frame);
                }
                else if (now >= m_nextEmptyFrameFlushUtc)
                    FlushEmptyTicksThrough(frame.Tick.Value, now);
                m_nextTickUtc = m_nextTickUtc.AddMilliseconds(m_options.TickIntervalMilliseconds);
            }
        }

        private void SealAndBroadcastInputDrivenFrame()
        {
            LockstepFrame frame = m_coordinator.SealNextFrame();
            if (frame.Commands.Length == 0)
                throw new InvalidOperationException(
                    "Input-driven Lockstep cannot seal an empty frame");
            BroadcastFrame(frame);
        }

        private void BroadcastFrame(LockstepFrame frame)
        {
            if (frame.Tick.Value != m_lastBroadcastTick + 1)
                throw new InvalidOperationException(
                    $"Lockstep broadcast gap: expected {m_lastBroadcastTick + 1}, received {frame.Tick.Value}");
            Broadcast(new LockstepMessage
            {
                Kind = LockstepMessageKind.Frame,
                Frame = frame,
            });
            m_lastBroadcastTick = frame.Tick.Value;
            m_logger?.Log(XLogger.LogVerbosity.VeryVerbose, LogCatagories.LockstepRelay,
                $"Broadcast frame {frame.Tick.Value} with {frame.Commands.Length} command(s)");
        }

        private void FlushEmptyTicksThrough(long throughTick, DateTime now)
        {
            while (m_lastBroadcastTick < throughTick)
            {
                long next = Math.Min(throughTick,
                    checked(m_lastBroadcastTick + m_options.MaximumEmptyTicksPerMessage));
                Broadcast(new LockstepMessage
                {
                    Kind = LockstepMessageKind.AdvanceTicks,
                    FromTick = new LockstepTick(m_lastBroadcastTick),
                    Tick = new LockstepTick(next),
                });
                m_lastBroadcastTick = next;
                m_logger?.Log(XLogger.LogVerbosity.VeryVerbose, LogCatagories.LockstepRelay,
                    $"Advanced empty ticks through {next}");
            }
            m_nextEmptyFrameFlushUtc = now.AddMilliseconds(
                m_options.EmptyFrameFlushMilliseconds);
        }

        private void SendHeartbeatIfDue(DateTime now)
        {
            if (now < m_nextHeartbeatUtc)
                return;
            byte[] packet = LockstepProtocol.Write(new LockstepMessage
            {
                Kind = LockstepMessageKind.Ping,
                Sequence = ++m_heartbeatSequence,
            });
            foreach (GameplayConnectionID connection in m_peers.Keys)
                m_messages.Send(connection, packet, GameplayNetworkDelivery.UnreliableSequenced);
            m_logger?.Log(XLogger.LogVerbosity.VeryVerbose, LogCatagories.LockstepRelay,
                $"Heartbeat {m_heartbeatSequence} sent to {m_peers.Count} peer(s)");
            m_nextHeartbeatUtc = now.AddMilliseconds(m_options.HeartbeatMilliseconds);
        }

        private void Broadcast(LockstepMessage message)
        {
            byte[] packet = LockstepProtocol.Write(message);
            m_recording?.Write(packet);
            foreach (GameplayConnectionID connection in m_peers.Keys)
                m_messages.Send(connection, packet);
        }

        private void Disconnect(GameplayConnectionID connection, string reason)
        {
            m_connected.Remove(connection);
            m_messages.Discard(connection);
            bool wasPlayer = m_peers.Remove(connection);
            m_logger?.LogWarning(XLogger.LogVerbosity.Common, LogCatagories.LockstepRelay,
                $"Peer disconnected: {connection}. Player={wasPlayer}, reason={reason ?? "none"}, remaining={m_peers.Count}");
            if (!wasPlayer)
                return;
            if (m_welcomeSent)
                StopMatch(string.IsNullOrWhiteSpace(reason)
                    ? "A Lockstep peer disconnected"
                    : $"A Lockstep peer disconnected: {reason}");
        }

        private void RejectConnection(GameplayConnectionID connection, string reason)
        {
            m_logger?.LogWarning(XLogger.LogVerbosity.Common, LogCatagories.LockstepRelay,
                $"Rejected connection {connection}: {reason}");
            m_messages.Send(connection, LockstepProtocol.Write(new LockstepMessage
            {
                Kind = LockstepMessageKind.Reject,
                Error = reason,
            }));
            m_transport.Disconnect(connection, reason);
            Disconnect(connection, reason);
        }

        private void StopMatch(string reason)
        {
            m_matchStarted = false;
            m_logger?.LogError(XLogger.LogVerbosity.Common, LogCatagories.LockstepRelay,
                $"Stopping match: {reason}");
            foreach (GameplayConnectionID connection in m_peers.Keys.ToArray())
                m_transport.Disconnect(connection, reason);
            SetState(GameplayNetworkState.Disabled, reason);
        }

        private void SetState(GameplayNetworkState state, string reason)
        {
            if (State == state)
                return;
            GameplayNetworkState oldState = State;
            State = state;
            m_logger?.Log(XLogger.LogVerbosity.Common, LogCatagories.LockstepRelay,
                $"State {oldState} -> {state}{(string.IsNullOrWhiteSpace(reason) ? string.Empty : $". Reason: {reason}")}");
            StateChanged?.Invoke(state, reason);
        }

        private static void ValidateOptions(GameplayLockstepOptions options)
        {
            if (options.ExpectedPlayerCount <= 0 ||
                !Enum.IsDefined(typeof(GameplayLockstepTickMode), options.TickMode) ||
                options.TickMode == GameplayLockstepTickMode.FixedRate &&
                options.TickIntervalMilliseconds <= 0 ||
                options.TickMode == GameplayLockstepTickMode.InputDriven &&
                options.InputDelayTicks != 0 ||
                options.InputDelayTicks < 0 || options.StateHashIntervalTicks <= 0 ||
                options.FullStateHashIntervalTicks <= 0 ||
                options.MaximumCatchUpTicksPerUpdate <= 0 ||
                options.EmptyFrameFlushMilliseconds <= 0 ||
                options.MaximumEmptyTicksPerMessage <= 0 ||
                options.HeartbeatMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(options),
                    "Lockstep relay options are invalid");
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
                throw new ObjectDisposedException(nameof(GameplayLockstepRelayServer));
        }

        public void Dispose()
        {
            if (m_disposed)
                return;
            m_disposed = true;
            m_recording?.Dispose();
            m_transport.Stop();
            m_transport.Dispose();
            m_logger?.Log(XLogger.LogVerbosity.Common, LogCatagories.LockstepRelay,
                $"Relay disposed at tick {m_lastBroadcastTick}");
            State = GameplayNetworkState.Disabled;
        }
    }
}
