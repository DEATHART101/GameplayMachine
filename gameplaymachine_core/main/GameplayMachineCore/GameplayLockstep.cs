using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XLockstep;
using LockstepPlayerController = GMCore.Lockstep.PlayerController;

namespace GMCore
{
    public enum GameplayLockstepTickMode
    {
        FixedRate,
        InputDriven,
    }

    public sealed class GameplayLockstepOptions : GameplayNetworkOptions
    {
        public int ExpectedPlayerCount = 1;
        public int RequestedSlot;
        public GameplayLockstepTickMode TickMode = GameplayLockstepTickMode.FixedRate;
        public int TickIntervalMilliseconds = 50;
        public int InputDelayTicks = 2;
        public int StateHashIntervalTicks = 30;
        public int FullStateHashIntervalTicks = 300;
        public int MaximumCatchUpTicksPerUpdate = 8;
        public int EmptyFrameFlushMilliseconds = 250;
        public int MaximumEmptyTicksPerMessage = 4096;
        public int HeartbeatMilliseconds = 1000;
        public string RecordingDirectory;
    }

    internal static class GameplayLockstepConnectionRequestCodec
    {
        private const uint Magic = 0x534C4D47; // GMLS
        private const byte Version = 1;

        public static byte[] Write(int requestedSlot, byte[] connectionData)
        {
            connectionData ??= Array.Empty<byte>();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(requestedSlot);
            writer.Write(connectionData.Length);
            writer.Write(connectionData);
            return stream.ToArray();
        }

        public static int ReadRequestedSlot(byte[] data)
        {
            data ??= Array.Empty<byte>();
            using var stream = new MemoryStream(data, writable: false);
            using var reader = new BinaryReader(stream);
            if (stream.Length < sizeof(uint) + sizeof(byte) + sizeof(int) * 2 ||
                reader.ReadUInt32() != Magic || reader.ReadByte() != Version)
                throw new InvalidDataException("Lockstep connection request is invalid");

            int requestedSlot = reader.ReadInt32();
            int payloadLength = reader.ReadInt32();
            if (payloadLength < 0 || payloadLength != stream.Length - stream.Position)
                throw new InvalidDataException("Lockstep connection request payload is invalid");
            return requestedSlot;
        }
    }

    internal sealed class GameplayLockstepRecordingWriter : IDisposable
    {
        private readonly BinaryWriter m_writer;

        public GameplayLockstepRecordingWriter(string directory, string role)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Recording directory is required", nameof(directory));
            Directory.CreateDirectory(directory);
            string fileName = $"{role}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.gmlockstep";
            m_writer = new BinaryWriter(new FileStream(Path.Combine(directory, fileName),
                FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            m_writer.Write(new byte[] { (byte)'G', (byte)'M', (byte)'L', (byte)'R', 1 });
            m_writer.Write(DateTime.UtcNow.ToBinary());
        }

        public void Write(byte[] packet)
        {
            m_writer.Write(DateTime.UtcNow.Ticks);
            m_writer.Write(packet.Length);
            m_writer.Write(packet);
            m_writer.Flush();
        }

        public void Dispose() => m_writer.Dispose();
    }

    public sealed class GameplayLockstepInputAcceptedParam
    {
        public long CommandID;
        public LockstepTick RequestedTick;
        public LockstepTick AcceptedTick;
        public bool Retargeted { get { return RequestedTick != AcceptedTick; } }
    }

    public sealed class GameplayLockstepInputRejectedParam
    {
        public LockstepTick Tick;
        public int PlayerSlot;
        public string InterfaceName;
        public string ErrorMessage;
    }

    public sealed class GameplayLockstepFrameAppliedParam
    {
        public LockstepTick Tick;
        public int CommandCount;
        public ulong StateHash;
    }

    public sealed class GameplayLockstepDesyncParam
    {
        public LockstepTick Tick;
        public int RemotePlayerSlot;
        public ulong ExpectedHash;
        public ulong ActualHash;
        public LockstepStateHashKind HashKind;
    }

    public sealed class GameplayLockstepHashAuditFailedParam
    {
        public LockstepTick Tick;
        public ulong IncrementalHash;
        public ulong RebuiltHash;
    }

    internal sealed class LockstepPeerGameplayNetworkSession : GameplayNetworkSession
    {
        private readonly struct ExpectedStateHash
        {
            public readonly int PlayerSlot;
            public readonly ulong Hash;
            public readonly LockstepStateHashKind Kind;

            public ExpectedStateHash(int playerSlot, ulong hash, LockstepStateHashKind kind)
            {
                PlayerSlot = playerSlot;
                Hash = hash;
                Kind = kind;
            }
        }

        private readonly GameplayLockstepOptions m_lockstepOptions;
        private readonly LockstepStateHashTracker m_stateHash;
        private readonly GameplayLockstepRecordingWriter m_recording;
        private GameplayConnectionID m_server;
        private bool m_connected;
        private int m_inputDelay;
        private int m_maximumEmptyTicksPerMessage;
        private int m_localPlayerSlot;
        private int m_stateHashIntervalTicks;
        private int m_fullStateHashIntervalTicks;
        private long m_nextCommandID;
        private long m_nextSequence;
        private readonly SortedDictionary<long, List<ExpectedStateHash>> m_expectedHashes =
            new SortedDictionary<long, List<ExpectedStateHash>>();
        private readonly SortedDictionary<long, ulong> m_localHashes =
            new SortedDictionary<long, ulong>();

        public LockstepPeerGameplayNetworkSession(
            GameplayMachine machine,
            IGameplayNetworkTransport transport,
            GameplayLockstepOptions options)
            : base(machine, transport, options)
        {
            m_lockstepOptions = options;
            m_stateHash = new LockstepStateHashTracker(machine);
            if (!string.IsNullOrWhiteSpace(options.RecordingDirectory))
                m_recording = new GameplayLockstepRecordingWriter(options.RecordingDirectory, "client");
        }

        internal ulong CurrentStateHash { get { return m_stateHash.CurrentHash; } }

        public override void OnObjectLifeChanged(GObjectID objectID, bool created) =>
            m_stateHash.MarkObjectLife(objectID);

        public override void OnObjectNetworkMetadataChanged(GObjectID objectID, GObjectID oldOwnerID) =>
            m_stateHash.MarkObject(objectID);

        public override void OnFieldChanged(
            GObjectID objectID, ODFieldName fieldName, ObjectEventTypes eventType,
            object context, object newValue, object oldValue) => m_stateHash.MarkObject(objectID);

        public override void OnRootChanged() => m_stateHash.MarkMetadata();

        public override void OnPartitionsChanged(GObjectID objectID)
        {
            if (objectID.IsValid)
                m_stateHash.MarkObject(objectID);
            else
                m_stateHash.MarkAll();
        }

        public override void Start()
        {
            SetState(GameplayNetworkState.Connecting);
            Transport.Connect();
        }

        public override void Update()
        {
            GameplayNetworkTransportEvent networkEvent;
            while (Transport.TryPollEvent(out networkEvent))
            {
                try
                {
                    if (networkEvent.Type == GameplayNetworkTransportEventType.Connected)
                        Connected(networkEvent.Connection);
                    else if (networkEvent.Type == GameplayNetworkTransportEventType.Disconnected)
                        Disconnected(networkEvent.Connection, networkEvent.Reason);
                    else if (networkEvent.Type == GameplayNetworkTransportEventType.Data &&
                             TryReceiveMessage(networkEvent.Connection, networkEvent.Data, out byte[] message))
                    {
                        m_recording?.Write(message);
                        ProcessMessage(LockstepProtocol.Read(message, Options.MaxMessageBytes));
                    }
                }
                catch (Exception exception)
                {
                    Transport.Disconnect(networkEvent.Connection, exception.Message);
                    Disconnected(networkEvent.Connection, exception.Message);
                }
            }
        }

        public ODCore.EventError CanSubmit(IReplicatedInterface param)
        {
            if (!m_connected || State != GameplayNetworkState.Ready)
                return "Lockstep peer is not ready";
            if (!RpcsByType.ContainsKey(param.GetType()))
                return $"No generated Lockstep codec is registered for {param.GetType()}";
            return true;
        }

        public ODCore.EventError Submit(IReplicatedInterface param)
        {
            ODCore.EventError error = CanSubmit(param);
            if (!error)
                return error;
            ODRpcMeta rpc = GetRpc(param.GetType());
            var command = new LockstepCommand
            {
                CommandId = ++m_nextCommandID,
                Sequence = ++m_nextSequence,
                RequestedTick = new LockstepTick(Machine.CurrentLockstepTick.Value + m_inputDelay + 1),
                TypeId = rpc.StableID,
                Payload = GameplayRpcCodec.Write(Machine, Machine.Serializers, rpc.ParamWriter, param),
            };
            SendMessage(m_server, LockstepProtocol.Write(new LockstepMessage
            {
                Kind = LockstepMessageKind.SubmitInput,
                Command = command,
            }));
            return true;
        }

        private void Connected(GameplayConnectionID connection)
        {
            m_server = connection;
            m_connected = true;
            SendMessage(connection, LockstepProtocol.Write(new LockstepMessage
            {
                Kind = LockstepMessageKind.Hello,
                SchemaId = Machine.NetworkSchemaID,
                ResourceCatalogId = Machine.ResourceCatalogID,
                InterfaceTypeIds = RpcsByID.Keys.OrderBy(item => item).ToArray(),
                Data = GameplayLockstepConnectionRequestCodec.Write(
                    m_lockstepOptions.RequestedSlot, m_lockstepOptions.ConnectionData),
            }));
        }

        private void Disconnected(GameplayConnectionID connection, string reason)
        {
            if (!connection.Equals(m_server))
                return;
            m_connected = false;
            DiscardFragments(connection);
            SetState(GameplayNetworkState.Disabled, reason);
        }

        private void ProcessMessage(LockstepMessage message)
        {
            switch (message.Kind)
            {
                case LockstepMessageKind.Welcome:
                    if (message.Tick != LockstepTick.Zero || message.PlayerId < 0 ||
                        message.PlayerCount <= 0 || message.PlayerId >= message.PlayerCount ||
                        message.InputDelay < 0 || message.MaximumEmptyTicks <= 0 ||
                        message.StateHashIntervalTicks <= 0 || message.FullStateHashIntervalTicks <= 0)
                        throw new InvalidDataException("Lockstep relay supplied invalid peer configuration");
                    Machine.InitializeLockstepPlayerControllers(message.PlayerCount, message.PlayerId);
                    m_stateHash.Initialize();
                    m_inputDelay = message.InputDelay;
                    m_localPlayerSlot = message.PlayerId;
                    m_maximumEmptyTicksPerMessage = message.MaximumEmptyTicks;
                    m_stateHashIntervalTicks = message.StateHashIntervalTicks;
                    m_fullStateHashIntervalTicks = message.FullStateHashIntervalTicks;
                    Revision = message.Tick.Value;
                    m_localHashes[message.Tick.Value] = m_stateHash.CurrentHash;
                    SetState(GameplayNetworkState.Synchronizing);
                    SendStateHash(message.Tick, m_stateHash.CurrentHash,
                        LockstepStateHashKind.FullAudit);
                    break;
                case LockstepMessageKind.Ready:
                    if (State != GameplayNetworkState.Synchronizing)
                        throw new InvalidDataException("Lockstep Ready arrived before Welcome");
                    SetState(GameplayNetworkState.Ready);
                    break;
                case LockstepMessageKind.InputAccepted:
                    Machine.BroadCastMachineEvent(new GameplayLockstepInputAcceptedParam
                    {
                        CommandID = message.CommandId,
                        RequestedTick = message.RequestedTick,
                        AcceptedTick = message.Tick,
                    });
                    break;
                case LockstepMessageKind.Frame:
                    ApplyFrame(message.Frame ?? throw new InvalidDataException("Frame is missing"));
                    break;
                case LockstepMessageKind.AdvanceTicks:
                    ApplyEmptyTickRange(message.FromTick, message.Tick);
                    break;
                case LockstepMessageKind.StateHash:
                    if (message.PlayerId != m_localPlayerSlot)
                        CheckStateHash(message.PlayerId, message.Tick,
                            message.StateHash, message.StateHashKind);
                    break;
                case LockstepMessageKind.Reject:
                    throw new InvalidDataException(message.Error);
                case LockstepMessageKind.Ping:
                    SendMessage(m_server, LockstepProtocol.Write(new LockstepMessage
                    {
                        Kind = LockstepMessageKind.Pong,
                        Sequence = message.Sequence,
                    }), GameplayNetworkDelivery.UnreliableSequenced);
                    break;
                case LockstepMessageKind.Pong:
                    break;
                default:
                    throw new InvalidDataException($"Server sent invalid Lockstep message {message.Kind}");
            }
        }

        private void ApplyFrame(LockstepFrame frame)
        {
            if (frame.Tick.Value != Revision + 1)
                throw new InvalidDataException($"Lockstep frame gap: expected {Revision + 1}, received {frame.Tick.Value}");
            Machine.ApplyLockstepFrame(frame, RpcsByID);
            Revision = frame.Tick.Value;
            bool fullAudit = frame.Tick.Value % m_fullStateHashIntervalTicks == 0;
            ulong stateHash;
            LockstepStateHashKind hashKind;
            if (fullAudit)
            {
                LockstepStateHashAudit audit = m_stateHash.AuditAndRebuild(item =>
                    Machine.BroadCastMachineEvent(new GameplayLockstepHashAuditFailedParam
                    {
                        Tick = frame.Tick,
                        IncrementalHash = item.IncrementalHash,
                        RebuiltHash = item.RebuiltHash,
                    }));
                stateHash = audit.RebuiltHash;
                hashKind = LockstepStateHashKind.FullAudit;
            }
            else
            {
                stateHash = m_stateHash.CommitDirty();
                hashKind = LockstepStateHashKind.Incremental;
            }
            Machine.NotifyLockstepFrameApplied(frame, stateHash);
            if (fullAudit || frame.Tick.Value % m_stateHashIntervalTicks == 0)
                SendStateHash(frame.Tick, stateHash, hashKind);
            if (m_expectedHashes.TryGetValue(Revision, out List<ExpectedStateHash> expected))
            {
                m_expectedHashes.Remove(Revision);
                foreach (ExpectedStateHash item in expected)
                    CompareStateHash(item.PlayerSlot, frame.Tick, item.Hash, stateHash, item.Kind);
            }
        }

        private void ApplyEmptyTickRange(LockstepTick fromTick, LockstepTick throughTick)
        {
            if (fromTick.Value != Revision)
                throw new InvalidDataException(
                    $"Lockstep empty range gap: expected start {Revision}, received {fromTick.Value}");
            long count = throughTick.Value - fromTick.Value;
            if (count <= 0 || count > m_maximumEmptyTicksPerMessage)
                throw new InvalidDataException("Lockstep empty range is invalid");
            for (long tick = fromTick.Value + 1; tick <= throughTick.Value; tick++)
            {
                ApplyFrame(new LockstepFrame
                {
                    Tick = new LockstepTick(tick),
                    Commands = Array.Empty<LockstepCommand>(),
                });
            }
        }

        private void SendStateHash(
            LockstepTick tick,
            ulong stateHash,
            LockstepStateHashKind hashKind)
        {
            m_localHashes[tick.Value] = stateHash;
            PruneLocalHashes(tick.Value - 1024);
            SendMessage(m_server, LockstepProtocol.Write(new LockstepMessage
            {
                Kind = LockstepMessageKind.StateHash,
                PlayerId = m_localPlayerSlot,
                Tick = tick,
                StateHash = stateHash,
                StateHashKind = hashKind,
            }));
        }

        private void CheckStateHash(
            int playerSlot,
            LockstepTick tick,
            ulong expected,
            LockstepStateHashKind hashKind)
        {
            if (tick.Value > Revision)
            {
                if (!m_expectedHashes.TryGetValue(tick.Value, out List<ExpectedStateHash> values))
                {
                    values = new List<ExpectedStateHash>();
                    m_expectedHashes.Add(tick.Value, values);
                }
                values.Add(new ExpectedStateHash(playerSlot, expected, hashKind));
                return;
            }
            if (!m_localHashes.TryGetValue(tick.Value, out ulong actual))
                return;
            CompareStateHash(playerSlot, tick, expected, actual, hashKind);
        }

        private void CompareStateHash(
            int playerSlot,
            LockstepTick tick,
            ulong expected,
            ulong actual,
            LockstepStateHashKind hashKind)
        {
            if (actual != expected)
                Machine.BroadCastMachineEvent(new GameplayLockstepDesyncParam
                {
                    Tick = tick,
                    RemotePlayerSlot = playerSlot,
                    ExpectedHash = expected,
                    ActualHash = actual,
                    HashKind = hashKind,
                });
        }

        private void PruneLocalHashes(long beforeTick)
        {
            while (m_localHashes.Count != 0 && m_localHashes.First().Key < beforeTick)
                m_localHashes.Remove(m_localHashes.First().Key);
        }

        public override void Dispose()
        {
            m_recording?.Dispose();
            base.Dispose();
        }
    }

    public partial class GameplayMachine
    {
        [NonSerialized] private LockstepTick m_currentLockstepTick;
        [NonSerialized] private List<GObjectID> m_lockstepPlayerControllers;

        public LockstepTick CurrentLockstepTick => m_currentLockstepTick;

        public void ConnectLockstepNetworking(
            IGameplayNetworkTransport transport,
            GameplayLockstepOptions options = null)
        {
            if (m_synchronizationMode != GameplaySynchronizationMode.Lockstep)
                throw new InvalidOperationException("ConnectLockstepNetworking requires Lockstep project mode");
            if (m_role != GameplayMachineRole.Authority)
                throw new InvalidOperationException("Only an Authority peer can connect to a Lockstep relay");
            if (m_networkSession != null)
                throw new InvalidOperationException("GameplayMachine networking is already enabled");
            StartNetworkSession(new LockstepPeerGameplayNetworkSession(
                this, transport, options ?? new GameplayLockstepOptions()));
        }

        private ODCore.EventError CanSubmitLockstepInterface(IReplicatedInterface param)
        {
            if (m_networkSession is LockstepPeerGameplayNetworkSession peer)
                return peer.CanSubmit(param);
            return "GameplayMachine does not have an active Lockstep session";
        }

        private ODCore.EventError SubmitLockstepInterface(IReplicatedInterface param)
        {
            if (m_networkSession is LockstepPeerGameplayNetworkSession peer)
                return peer.Submit(param);
            return "GameplayMachine does not have an active Lockstep session";
        }

        internal byte[] CreateLockstepSnapshot() => GameplayMachineSaveFormat.Save(
            this, m_objectManager, m_serializers,
            purpose: GameplaySerializationPurpose.Network,
            includePartitions: false);

        internal void ApplyLockstepFrame(
            LockstepFrame frame,
            IReadOnlyDictionary<ulong, ODRpcMeta> rpcs)
        {
            if (frame.Tick.Value != m_currentLockstepTick.Value + 1)
                throw new InvalidDataException(
                    $"Lockstep frame gap: expected {m_currentLockstepTick.Value + 1}, received {frame.Tick.Value}");
            m_currentLockstepTick = frame.Tick;
            foreach (LockstepCommand command in frame.Commands)
            {
                if (!rpcs.TryGetValue(command.TypeId, out ODRpcMeta rpc) || rpc.Mode != GameplayRpcMode.Lockstep)
                    throw new InvalidDataException($"Unknown Lockstep interface {command.TypeId}");
                object boxed = GameplayRpcCodec.Read(this, Serializers, rpc.ParamReader, command.Payload);
                if (!(boxed is IReplicatedInterface input) || !(input is IPlayerInputInterface))
                    throw new InvalidDataException($"Lockstep interface {rpc.StableName} has an invalid contract");
                GameplayRpcContext? previous = PushPlayerControllerContext(
                    ResolveLockstepPlayerController(command.PlayerId));
                try
                {
                    CheckableInterfaceResult execution =
                        ExecuteReplicatedFromNetwork(input, GameplayRpcMode.Lockstep);
                    if (!execution.Sussceeded)
                        BroadCastMachineEvent(new GameplayLockstepInputRejectedParam
                        {
                            Tick = frame.Tick,
                            PlayerSlot = command.PlayerId,
                            InterfaceName = rpc.StableName,
                            ErrorMessage = execution.ErrorMessage.ToString(),
                        });
                }
                finally
                {
                    RestoreRpcContext(previous);
                }
            }
            // Every peer executes game simulation only after the complete ordered input frame.
            RunGameplayTick();
        }

        internal void InitializeLockstepPlayerControllers(int playerCount, int localPlayerSlot)
        {
            if (m_role != GameplayMachineRole.Authority)
                throw new InvalidOperationException("Lockstep peers must be Authority GameplayMachines");
            if (playerCount <= 0 || localPlayerSlot < 0 || localPlayerSlot >= playerCount)
                throw new ArgumentOutOfRangeException(nameof(localPlayerSlot));
            if (m_lockstepPlayerControllers != null)
                throw new InvalidOperationException("Lockstep player slots are already initialized");

            m_engineMutationDepth++;
            try
            {
                LockstepPlayerController originalLocal = GetLocalLockstepPlayerController();
                m_lockstepPlayerControllers = new List<GObjectID>(playerCount) { originalLocal.ObjectID };
                while (m_lockstepPlayerControllers.Count < playerCount)
                {
                    LockstepPlayerController created = CreateAutomaticLockstepPlayerController();
                    SetNetworkOwnerCore(created.ObjectID, created.ObjectID, false);
                    m_lockstepPlayerControllers.Add(created.ObjectID);
                }

                for (int slot = 0; slot < m_lockstepPlayerControllers.Count; slot++)
                {
                    LockstepPlayerController controller = CreateLockstepPlayerControllerReference(
                        m_lockstepPlayerControllers[slot]);
                    controller.SlotID = slot;
                }

                SetLocalPlayerControllerForReplication(m_lockstepPlayerControllers[localPlayerSlot]);
                foreach (GObjectID objectID in m_lockstepPlayerControllers)
                    NotifyPlayerJoin(CreateLockstepPlayerControllerReference(objectID));
                OnGameStart();
            }
            finally
            {
                m_engineMutationDepth--;
            }
        }

        private LockstepPlayerController ResolveLockstepPlayerController(int playerSlot)
        {
            if (m_lockstepPlayerControllers == null || playerSlot < 0 ||
                playerSlot >= m_lockstepPlayerControllers.Count)
                throw new InvalidDataException($"Unknown Lockstep player slot {playerSlot}");
            return CreateLockstepPlayerControllerReference(m_lockstepPlayerControllers[playerSlot]);
        }

        public LockstepPlayerController GetPlayerControllerBySlot(int slotID)
        {
            if (m_synchronizationMode != GameplaySynchronizationMode.Lockstep)
                throw new InvalidOperationException(
                    "Player slots are only available in Lockstep GameplayMachines");
            if (m_lockstepPlayerControllers == null)
                throw new InvalidOperationException("Lockstep player slots have not been initialized");
            if (slotID < 0 || slotID >= m_lockstepPlayerControllers.Count)
                throw new ArgumentOutOfRangeException(nameof(slotID));
            return CreateLockstepPlayerControllerReference(m_lockstepPlayerControllers[slotID]);
        }

        public LockstepPlayerController? LocalLockstepPlayerController =>
            m_synchronizationMode == GameplaySynchronizationMode.Lockstep &&
            m_localPlayerControllerID.IsValid && ContainsGameplayObject(m_localPlayerControllerID)
                ? CreateLockstepPlayerControllerReference(m_localPlayerControllerID)
                : (LockstepPlayerController?)null;

        public IEnumerable<LockstepPlayerController> GetLockstepPlayerControllers()
        {
            if (m_synchronizationMode != GameplaySynchronizationMode.Lockstep)
                throw new InvalidOperationException("Lockstep PlayerControllers are only available in Lockstep mode");
            return (m_lockstepPlayerControllers ?? Enumerable.Empty<GObjectID>())
                .Where(id => id.IsValid && ContainsGameplayObject(id))
                .Distinct()
                .Select(CreateLockstepPlayerControllerReference)
                .ToArray();
        }

        public LockstepPlayerController GetLocalLockstepPlayerController()
        {
            LockstepPlayerController? result = LocalLockstepPlayerController;
            if (!result.HasValue)
                throw new InvalidOperationException("The local LockstepPlayerController is not available");
            return result.Value;
        }

        private LockstepPlayerController CreateLockstepPlayerControllerReference(GObjectID objectID)
        {
            return new LockstepPlayerController { Machine = this, ObjectID = objectID };
        }

        internal void NotifyLockstepFrameApplied(LockstepFrame frame, ulong stateHash)
        {
            BroadCastMachineEvent(new GameplayLockstepFrameAppliedParam
            {
                Tick = frame.Tick,
                CommandCount = frame.Commands.Length,
                StateHash = stateHash,
            });
        }

        public ulong ComputeLockstepStateHash()
        {
            if (m_networkSession is LockstepPeerGameplayNetworkSession peer)
                return peer.CurrentStateHash;
            return LockstepStateHashTree.Build(this).RootHash;
        }

        public ulong ComputeLockstepSnapshotHash()
        {
            byte[] bytes = CreateLockstepSnapshot();
            ulong hash = 14695981039346656037UL;
            unchecked
            {
                foreach (byte value in bytes)
                    hash = (hash ^ value) * 1099511628211UL;
            }
            return hash;
        }
    }
}
