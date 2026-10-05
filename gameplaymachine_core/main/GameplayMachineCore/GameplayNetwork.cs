using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PlayerController = GMCore.StateSync.PlayerController;
using LockstepPlayerController = GMCore.Lockstep.PlayerController;

namespace GMCore
{
    public enum GameplayMachineRole
    {
        Authority,
        ClientProxy,
    }

    public enum GameplayNetworkState
    {
        Disabled,
        Connecting,
        Synchronizing,
        Ready,
    }

    public struct GameplayConnectionID : IEquatable<GameplayConnectionID>
    {
        public long Value;

        public bool Equals(GameplayConnectionID other) { return Value == other.Value; }
        public override bool Equals(object obj) { return obj is GameplayConnectionID other && Equals(other); }
        public override int GetHashCode() { return Value.GetHashCode(); }
        public override string ToString() { return Value.ToString(); }
    }

    public enum GameplayNetworkTransportEventType
    {
        Connected,
        Disconnected,
        Data,
    }

    public struct GameplayNetworkTransportEvent
    {
        public GameplayNetworkTransportEventType Type;
        public GameplayConnectionID Connection;
        public byte[] Data;
        public string Reason;
    }

    public enum GameplayNetworkDelivery
    {
        ReliableOrdered,
        UnreliableSequenced,
    }

    public interface IGameplayNetworkTransport : IDisposable
    {
        void StartServer();
        void Connect();
        bool TryPollEvent(out GameplayNetworkTransportEvent networkEvent);
        void Send(GameplayConnectionID connection, byte[] data, GameplayNetworkDelivery delivery = GameplayNetworkDelivery.ReliableOrdered);
        void Disconnect(GameplayConnectionID connection, string reason = null);
        void Stop();
    }

    public class GameplayNetworkOptions
    {
        public XLogger.Logger Logger;
        public int MaxMessageBytes = 16 * 1024 * 1024;
        public int FragmentPayloadBytes = 32 * 1024;
        public int MaxFragmentsPerMessage = 1024;
        public int MaxPendingFragmentedMessages = 64;
        public int FragmentTimeoutMilliseconds = 30_000;
        public int PredictionTimeoutMilliseconds = 10_000;
        public byte[] ConnectionData = Array.Empty<byte>();
        public Func<GameplayMachine, GameplayConnectionID, byte[], CommonGameplayObject?> ResolvePlayerController;
        public Action<GameplayMachine, GameplayConnectionID, PlayerController, byte[]> InitializePlayerController;
        public Action<GameplayMachine, GameplayConnectionID, PlayerController?> PlayerDisconnected;
        public Func<GameplayConnectionID, ODRpcMeta, object, bool> AuthorizeServerRpc;
    }

    public readonly struct GameplayRpcContext
    {
        public GameplayConnectionID Connection { get; }
        public PlayerController? PlayerController { get; }
        public LockstepPlayerController? LockstepPlayerController { get; }

        internal GameplayRpcContext(
            GameplayConnectionID connection,
            PlayerController? playerController,
            LockstepPlayerController? lockstepPlayerController = null)
        {
            Connection = connection;
            PlayerController = playerController;
            LockstepPlayerController = lockstepPlayerController;
        }
    }

    public sealed class GameplayObjectNetworkOwnerChangedParam
    {
        public GObjectID ObjectID;
        public GObjectID OldOwnerID;
        public GObjectID NewOwnerID;
    }

    public sealed class GameplayPlayerControllerChangedParam
    {
        public GameplayConnectionID Connection;
        public GObjectID OldPlayerControllerID;
        public GObjectID NewPlayerControllerID;
    }

    public sealed class GameplayNetworkStateChangedParam
    {
        public GameplayNetworkState OldState;
        public GameplayNetworkState NewState;
        public string Reason;
    }

    public delegate void ODRpcValueWriter(GameplaySaveWriter writer, object value);
    public delegate object ODRpcValueReader(GameplaySaveReader reader);
    public delegate ODRpcExecutionResult ODRpcExecutor(GameplayMachine machine, object param);

    public struct ODRpcExecutionResult
    {
        public string Error;
        public object Result;

        public bool Succeeded { get { return string.IsNullOrEmpty(Error); } }
    }

    public struct ODRpcMeta
    {
        public ulong StableID;
        public string StableName;
        public GameplayRpcMode Mode;
        public GameplayInterfaceType InterfaceType;
        public Type ParamType;
        public Type ResultType;
        public ODRpcValueWriter ParamWriter;
        public ODRpcValueReader ParamReader;
        public ODRpcValueWriter ResultWriter;
        public ODRpcValueReader ResultReader;
        public ODRpcExecutor Execute;
    }

    public struct ODMulticastEventMeta
    {
        public ulong StableID;
        public string StableName;
        public Type EventType;
        public ODRpcValueWriter Writer;
        public ODRpcValueReader Reader;
    }

    public interface IODNetworkModule
    {
        ulong NetworkSchemaID { get; }
        IEnumerable<ODRpcMeta> GetAllODRpcMetas();
    }

    public interface IODMulticastEventNetworkModule
    {
        IEnumerable<ODMulticastEventMeta> GetAllODMulticastEventMetas();
    }

    public interface IODResourceCatalogNetworkModule
    {
        ulong ResourceCatalogID { get; }
    }

    internal enum GameplayNetworkMessageType : byte
    {
        Hello = 1,
        Welcome = 2,
        Reject = 3,
        ReplicationFrame = 4,
        AuthorityInterface = 5,
        MulticastInterface = 6,
        ResyncRequest = 7,
        PredictableInterface = 8,
        PredictionResponse = 9,
        MulticastEvent = 10,
    }

    internal static class GameplayNetworkMessageCodec
    {
        private const uint Magic = 0x314E4D47U;
        private const ushort Version = 7;

        public static byte[] Write(GameplayNetworkMessageType type, Action<BinaryWriter> body)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write((byte)type);
                body?.Invoke(writer);
                return stream.ToArray();
            }
        }

        public static GameplayNetworkMessageType Read(byte[] bytes, int maxBytes, Action<GameplayNetworkMessageType, BinaryReader> body)
        {
            if (bytes == null || bytes.Length > maxBytes)
            {
                throw new InvalidDataException("Gameplay network message has an invalid size");
            }
            using (MemoryStream stream = new MemoryStream(bytes, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != Version)
                {
                    throw new InvalidDataException("Gameplay network protocol header does not match");
                }
                GameplayNetworkMessageType type = (GameplayNetworkMessageType)reader.ReadByte();
                body(type, reader);
                if (stream.Position != stream.Length)
                {
                    throw new InvalidDataException("Gameplay network message contains trailing data");
                }
                return type;
            }
        }

        public static void WriteBytes(BinaryWriter writer, byte[] bytes)
        {
            bytes = bytes ?? Array.Empty<byte>();
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        public static byte[] ReadBytes(BinaryReader reader, int maxBytes)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > maxBytes)
            {
                throw new InvalidDataException("Gameplay network payload has an invalid size");
            }
            byte[] result = reader.ReadBytes(count);
            if (result.Length != count)
            {
                throw new EndOfStreamException();
            }
            return result;
        }

        public static void WriteNullableString(BinaryWriter writer, string value)
        {
            writer.Write(value != null);
            if (value != null)
            {
                writer.Write(value);
            }
        }

        public static string ReadNullableString(BinaryReader reader)
        {
            return reader.ReadBoolean() ? reader.ReadString() : null;
        }
    }

    internal struct GameplayNetworkFragment
    {
        public long MessageID;
        public int Index;
        public int Count;
        public int TotalLength;
        public byte[] Payload;
    }

    internal static class GameplayNetworkFragmentCodec
    {
        private const uint Magic = 0x31464D47U;
        private const ushort Version = 1;

        public static byte[] Write(long messageID, int index, int count, int totalLength, byte[] data, int offset, int length)
        {
            using (MemoryStream stream = new MemoryStream(length + 32))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write(messageID);
                writer.Write(index);
                writer.Write(count);
                writer.Write(totalLength);
                writer.Write(length);
                writer.Write(data, offset, length);
                return stream.ToArray();
            }
        }

        public static bool TryRead(byte[] bytes, int maxMessageBytes, out GameplayNetworkFragment fragment)
        {
            fragment = default;
            if (bytes == null || bytes.Length < 4 || BitConverter.ToUInt32(bytes, 0) != Magic)
            {
                return false;
            }
            using (MemoryStream stream = new MemoryStream(bytes, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != Version)
                {
                    throw new InvalidDataException("Gameplay network fragment header does not match");
                }
                fragment.MessageID = reader.ReadInt64();
                fragment.Index = reader.ReadInt32();
                fragment.Count = reader.ReadInt32();
                fragment.TotalLength = reader.ReadInt32();
                int payloadLength = reader.ReadInt32();
                if (fragment.MessageID <= 0 || fragment.Count <= 1 || fragment.Index < 0 ||
                    fragment.Index >= fragment.Count || fragment.TotalLength <= 0 ||
                    fragment.TotalLength > maxMessageBytes || payloadLength <= 0 ||
                    payloadLength > fragment.TotalLength || stream.Position + payloadLength != stream.Length)
                {
                    throw new InvalidDataException("Gameplay network fragment is invalid");
                }
                fragment.Payload = reader.ReadBytes(payloadLength);
                if (fragment.Payload.Length != payloadLength)
                {
                    throw new EndOfStreamException();
                }
                return true;
            }
        }
    }

    internal static class GameplayRpcCodec
    {
        public static byte[] Write(GameplayMachine machine, IEnumerable<IGMSerializer> serializers, ODRpcValueWriter codec, object value)
        {
            using (GameplaySaveWriter writer = new GameplaySaveWriter(serializers, GameplaySerializationPurpose.Network))
            {
                writer.Machine = machine;
                codec(writer, value);
                return writer.Finish();
            }
        }

        public static object Read(GameplayMachine machine, IEnumerable<IGMSerializer> serializers, ODRpcValueReader codec, byte[] bytes)
        {
            using (GameplaySaveReader reader = new GameplaySaveReader(bytes, serializers, GameplaySerializationPurpose.Network))
            {
                reader.Machine = machine;
                object result = codec(reader);
                reader.EnsureAt(reader.Length);
                return result;
            }
        }
    }

    internal sealed class GameplayReplicationObjectData
    {
        public GObjectID ObjectID;
        public bool Created;
        public bool MetadataChanged;
        public ODClassSaveMeta ClassMeta;
        public GObjectID OwnerID;
        public GameplayObjectReplicationMode ReplicationMode;
        public PartitionID PartitionID;
        public readonly List<GameplayReplicationFieldData> Fields = new List<GameplayReplicationFieldData>();
    }

    internal sealed class GameplayReplicationFieldData
    {
        public ODFieldSaveMeta Meta;
        public object Value;
        public bool IsDelta;
        public bool IsReset;
        public byte PatchMask;
        public readonly List<GameplayReplicationDelta> Deltas = new List<GameplayReplicationDelta>();
    }

    internal sealed class GameplayReplicationDelta
    {
        public ObjectEventTypes EventType;
        public object Key;
        public object Value;
    }

    internal sealed class GameplayReplicationDirtyField
    {
        public bool SendFullValue;
        public byte PatchMask;
        public readonly List<GameplayReplicationDelta> Deltas = new List<GameplayReplicationDelta>();
    }

    internal static class GameplayReplicationFormat
    {
        private const uint Magic = 0x31465052U;
        private const ushort Version = 8;
        private const byte CreatedFlag = 1;
        private const byte MetadataFlag = 2;
        private const uint FieldKindMask = 3;
        private const uint FullField = 0;
        private const uint DeltaField = 1;
        private const uint ResetField = 2;
        private const uint PatchField = 3;
        private const byte TransformPosition = 1;
        private const byte TransformRotation = 2;
        private const byte TransformScale = 4;

        public static byte[] WriteFrame(
            GameplayMachine machine,
            long revision,
            IEnumerable<GObjectID> deleted,
            IEnumerable<GObjectID> created,
            IReadOnlyDictionary<GObjectID, Dictionary<ODFieldName, GameplayReplicationDirtyField>> dirty,
            bool rootChanged)
        {
            return WriteFrame(
                machine,
                revision,
                deleted,
                created,
                dirty,
                Array.Empty<GObjectID>(),
                new Dictionary<GObjectID, HashSet<ODFieldName>>(),
                new Dictionary<GObjectID, HashSet<ODFieldName>>(),
                default,
                rootChanged,
                machine.RootObjectID);
        }

        public static byte[] WriteFrame(
            GameplayMachine machine,
            long revision,
            IEnumerable<GObjectID> deleted,
            IEnumerable<GObjectID> created,
            IReadOnlyDictionary<GObjectID, Dictionary<ODFieldName, GameplayReplicationDirtyField>> dirty,
            IEnumerable<GObjectID> metadataChanged,
            IReadOnlyDictionary<GObjectID, HashSet<ODFieldName>> resetFields,
            IReadOnlyDictionary<GObjectID, HashSet<ODFieldName>> forceFullFields,
            GObjectID playerControllerID,
            bool rootChanged,
            GObjectID rootObjectID,
            bool partitionsChanged = false,
            ISet<GObjectID> permanentlyDeleted = null)
        {
            IGameplayObjectSaveManager manager = machine.ObjectManager as IGameplayObjectSaveManager;
            if (manager == null)
            {
                throw new NotSupportedException($"{machine.ObjectManager.GetType()} does not support gameplay replication");
            }

            List<GObjectID> deletedItems = deleted.Distinct().OrderBy(id => id.ID).ToList();
            HashSet<GObjectID> createdItems = new HashSet<GObjectID>(created);
            HashSet<GObjectID> metadataItems = new HashSet<GObjectID>(metadataChanged);
            List<GObjectID> objectItems = createdItems.Concat(dirty.Keys).Concat(metadataItems)
                .Concat(resetFields.Keys).Concat(forceFullFields.Keys).Distinct()
                .Where(id => machine.ObjectManager.ContainsGameplayObject(id))
                .OrderBy(id => id.ID)
                .ToList();

            using (GameplaySaveWriter writer = new GameplaySaveWriter(machine.Serializers, GameplaySerializationPurpose.Network))
            {
                writer.Machine = machine;
                writer.WriteUInt32(Magic);
                writer.WriteUInt16(Version);
                writer.WriteInt64(revision);
                writer.WriteBoolean(rootChanged);
                if (rootChanged)
                {
                    writer.WriteObjectID(rootObjectID);
                }
                writer.WriteBoolean(partitionsChanged);
                if (partitionsChanged) machine.WritePartitionManifest(writer, false, recipient: playerControllerID);
                writer.WriteCount(deletedItems.Count);
                foreach (GObjectID objectID in deletedItems)
                {
                    writer.WriteObjectID(objectID);
                    writer.WriteBoolean(permanentlyDeleted == null || permanentlyDeleted.Contains(objectID));
                }

                writer.WriteCount(objectItems.Count);
                foreach (GObjectID objectID in objectItems)
                {
                    IGameplayObject gameplayObject = machine.ObjectManager.GetGameplayObject(objectID);
                    bool isCreated = createdItems.Contains(objectID);
                    bool hasMetadata = isCreated || metadataItems.Contains(objectID);
                    writer.WriteObjectID(objectID);
                    writer.WriteByte((byte)((isCreated ? CreatedFlag : 0) | (hasMetadata ? MetadataFlag : 0)));
                    if (isCreated)
                    {
                        writer.WriteUInt64(GameplayMachineBase.GetODClassSaveMeta(gameplayObject.ObjectClass).StableID);
                    }
                    if (hasMetadata)
                    {
                        writer.WriteObjectID(machine.GetNetworkOwnerID(objectID));
                        writer.WriteByte((byte)machine.GetGameplayObjectReplicationMode(objectID));
                        writer.WriteInt32(machine.GetGameplayObjectPartition(objectID).Value);
                    }

                    List<GameplayReplicationFieldData> fields;
                    if (isCreated)
                    {
                        fields = manager.GetStoredFieldsForSave(objectID)
                            .Where(field => machine.IsFieldVisibleTo(objectID, field.Key, playerControllerID))
                            .Select(field => new GameplayReplicationFieldData
                            {
                                Meta = GameplayMachineBase.GetODFieldSaveMeta(field.Key),
                                Value = field.Value,
                            })
                            .OrderBy(field => field.Meta.StableID)
                            .ToList();
                    }
                    else
                    {
                        Dictionary<ODFieldName, object> storedFields = manager.GetStoredFieldsForSave(objectID)
                            .ToDictionary(field => field.Key, field => field.Value);
                        Dictionary<ODFieldName, GameplayReplicationDirtyField> dirtyFields;
                        dirty.TryGetValue(objectID, out dirtyFields);
                        dirtyFields = dirtyFields ?? new Dictionary<ODFieldName, GameplayReplicationDirtyField>();
                        fields = dirtyFields
                            .Where(field => machine.IsFieldVisibleTo(objectID, field.Key, playerControllerID))
                            .Select(field => new GameplayReplicationFieldData
                            {
                                Meta = GameplayMachineBase.GetODFieldSaveMeta(field.Key),
                                Value = storedFields.TryGetValue(field.Key, out object value) ? value : field.Key.Meta.DefaultObject,
                                IsDelta = !field.Value.SendFullValue && field.Value.Deltas.Count > 0,
                                PatchMask = field.Value.SendFullValue ? (byte)0 : field.Value.PatchMask,
                            })
                            .OrderBy(field => field.Meta.StableID)
                            .ToList();
                        foreach (GameplayReplicationFieldData field in fields.Where(item => item.IsDelta))
                        {
                            GameplayReplicationDirtyField source = dirtyFields[field.Meta.FieldName];
                            field.Deltas.AddRange(source.Deltas);
                        }
                        HashSet<ODFieldName> forced;
                        if (forceFullFields.TryGetValue(objectID, out forced))
                        {
                            foreach (ODFieldName fieldName in forced.Where(field =>
                                         machine.IsFieldVisibleTo(objectID, field, playerControllerID)))
                            {
                                fields.RemoveAll(item => item.Meta.FieldName.Equals(fieldName));
                                fields.Add(new GameplayReplicationFieldData
                                {
                                    Meta = GameplayMachineBase.GetODFieldSaveMeta(fieldName),
                                    Value = storedFields.TryGetValue(fieldName, out object value)
                                        ? value
                                        : machine.CreateInitialGameplayFieldValue(fieldName),
                                });
                            }
                        }
                        HashSet<ODFieldName> resets;
                        if (resetFields.TryGetValue(objectID, out resets))
                        {
                            foreach (ODFieldName fieldName in resets)
                            {
                                fields.RemoveAll(item => item.Meta.FieldName.Equals(fieldName));
                                fields.Add(new GameplayReplicationFieldData
                                {
                                    Meta = GameplayMachineBase.GetODFieldSaveMeta(fieldName),
                                    IsReset = true,
                                });
                            }
                        }
                        fields = fields.OrderBy(field => field.Meta.StableID).ToList();
                    }

                    writer.WriteCount(fields.Count);
                    foreach (GameplayReplicationFieldData field in fields)
                    {
                        uint fieldIndex = machine.GetNetworkFieldIndex(field.Meta.FieldName);
                        if (fieldIndex > int.MaxValue)
                        {
                            throw new InvalidOperationException("Gameplay network field index space is exhausted");
                        }
                        uint fieldKind = field.IsReset ? ResetField : field.IsDelta ? DeltaField :
                            field.PatchMask != 0 ? PatchField : FullField;
                        uint fieldToken = (fieldIndex << 2) | fieldKind;
                        writer.WriteVarUInt32(fieldToken);
                        if (field.IsReset)
                        {
                            continue;
                        }
                        if (field.PatchMask != 0)
                        {
                            WritePatch(writer, field.Meta, field.Value, field.PatchMask);
                        }
                        else if (!field.IsDelta)
                        {
                            field.Meta.Writer(writer, field.Value);
                        }
                        else
                        {
                            writer.WriteCount(field.Deltas.Count);
                            foreach (GameplayReplicationDelta delta in field.Deltas)
                            {
                                writer.WriteByte((byte)delta.EventType);
                                field.Meta.DeltaKeyWriter(writer, delta.Key);
                                if (field.Meta.DeltaKind == ODCollectionDeltaKind.Map &&
                                    delta.EventType != ObjectEventTypes.Remove)
                                    field.Meta.DeltaValueWriter(writer, delta.Value);
                            }
                        }
                    }
                }
                return writer.Finish();
            }
        }

        public static long ApplyFrame(GameplayMachine machine, byte[] bytes)
        {
            IGameplayObjectReplicationManager manager = machine.ObjectManager as IGameplayObjectReplicationManager;
            if (manager == null)
            {
                throw new NotSupportedException($"{machine.ObjectManager.GetType()} does not support gameplay replication");
            }

            using (GameplaySaveReader reader = new GameplaySaveReader(bytes, machine.Serializers, GameplaySerializationPurpose.Network))
            {
                reader.Machine = machine;
                if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != Version)
                {
                    throw new InvalidDataException("Gameplay replication frame header does not match");
                }
                long revision = reader.ReadInt64();
                bool rootChanged = reader.ReadBoolean();
                GObjectID rootObjectID = rootChanged ? reader.ReadObjectID() : default;
                if (reader.ReadBoolean()) machine.ReadPartitionManifest(reader, false);
                int deletedCount = ReadCount(reader, "deleted object");
                List<GObjectID> deleted = new List<GObjectID>(deletedCount);
                var unavailable = new HashSet<GObjectID>();
                for (int i = 0; i < deletedCount; i++)
                {
                    var id = reader.ReadObjectID();
                    deleted.Add(id);
                    if (!reader.ReadBoolean()) unavailable.Add(id);
                }

                int objectCount = ReadCount(reader, "replicated object");
                List<GameplayReplicationObjectData> objects = new List<GameplayReplicationObjectData>(objectCount);
                for (int i = 0; i < objectCount; i++)
                {
                    GameplayReplicationObjectData item = new GameplayReplicationObjectData();
                    item.ObjectID = reader.ReadObjectID();
                    byte flags = reader.ReadByte();
                    if ((flags & ~(CreatedFlag | MetadataFlag)) != 0)
                        throw new InvalidDataException("Replicated GameplayObject has invalid flags");
                    item.Created = (flags & CreatedFlag) != 0;
                    item.MetadataChanged = (flags & MetadataFlag) != 0;
                    if (item.Created)
                    {
                        item.ClassMeta = GameplayMachineBase.GetODClassSaveMeta(reader.ReadUInt64());
                    }
                    if (item.MetadataChanged)
                    {
                        item.OwnerID = reader.ReadObjectID();
                        item.ReplicationMode = (GameplayObjectReplicationMode)reader.ReadByte();
                        item.PartitionID = new PartitionID(reader.ReadInt32());
                        if (!Enum.IsDefined(typeof(GameplayObjectReplicationMode), item.ReplicationMode))
                            throw new InvalidDataException("Replicated GameplayObject has an invalid replication mode");
                    }
                    int fieldCount = ReadCount(reader, "replicated field");
                    for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
                    {
                        uint fieldToken = reader.ReadVarUInt32();
                        uint networkFieldIndex = fieldToken >> 2;
                        uint fieldKind = fieldToken & FieldKindMask;
                        if (fieldKind > PatchField)
                            throw new InvalidDataException("Replicated field has an invalid encoding");
                        bool isDelta = fieldKind == DeltaField;
                        bool isReset = fieldKind == ResetField;
                        bool isPatch = fieldKind == PatchField;
                        ODFieldSaveMeta fieldMeta = machine.GetNetworkFieldMeta(networkFieldIndex);
                        if (fieldMeta.FieldName.Meta.ReplicationMode == ODFieldReplicationMode.None)
                        {
                            throw new InvalidDataException($"Field {fieldMeta.StableName} is not configured for replication");
                        }
                        var fieldData = new GameplayReplicationFieldData
                        {
                            Meta = fieldMeta,
                            IsDelta = isDelta,
                            IsReset = isReset,
                        };
                        if (isReset)
                        {
                            fieldData.Value = machine.CreateInitialGameplayFieldValue(fieldMeta.FieldName);
                        }
                        else if (isPatch)
                        {
                            fieldData.PatchMask = reader.ReadByte();
                            fieldData.Value = ReadPatch(reader, fieldMeta, fieldData.PatchMask);
                        }
                        else if (!isDelta)
                        {
                            fieldData.Value = fieldMeta.Reader(reader);
                        }
                        else
                        {
                            if (fieldMeta.DeltaKind == ODCollectionDeltaKind.None ||
                                fieldMeta.DeltaKeyReader == null)
                                throw new InvalidDataException($"Field {fieldMeta.StableName} does not support delta replication");
                            int deltaCount = ReadCount(reader, "collection delta");
                            for (int deltaIndex = 0; deltaIndex < deltaCount; deltaIndex++)
                            {
                                ObjectEventTypes eventType = (ObjectEventTypes)reader.ReadByte();
                                if (eventType != ObjectEventTypes.Add && eventType != ObjectEventTypes.Remove &&
                                    eventType != ObjectEventTypes.ItemChanged)
                                    throw new InvalidDataException("Collection delta has an invalid event type");
                                object key = fieldMeta.DeltaKeyReader(reader);
                                object value = null;
                                if (fieldMeta.DeltaKind == ODCollectionDeltaKind.Map && eventType != ObjectEventTypes.Remove)
                                {
                                    if (fieldMeta.DeltaValueReader == null)
                                        throw new InvalidDataException($"Map field {fieldMeta.StableName} has no delta value codec");
                                    value = fieldMeta.DeltaValueReader(reader);
                                }
                                fieldData.Deltas.Add(new GameplayReplicationDelta
                                {
                                    EventType = eventType,
                                    Key = key,
                                    Value = value,
                                });
                            }
                        }
                        item.Fields.Add(fieldData);
                    }
                    objects.Add(item);
                }
                reader.EnsureAt(reader.Length);
                machine.ApplyReplicationData(manager, deleted, objects, rootChanged, rootObjectID, unavailable);
                return revision;
            }
        }

        private static void WritePatch(
            GameplaySaveWriter writer,
            ODFieldSaveMeta field,
            object value,
            byte mask)
        {
            if (field.PatchKind != ODFieldPatchKind.Transform || !(value is Transform transform) ||
                mask == 0 || (mask & ~(TransformPosition | TransformRotation | TransformScale)) != 0)
            {
                throw new InvalidOperationException($"Field {field.StableName} has an invalid replication patch");
            }

            writer.WriteByte(mask);
            if ((mask & TransformPosition) != 0)
            {
                writer.WriteString(transform.Position.Space.Value);
                writer.WriteSingle(transform.Position.X);
                writer.WriteSingle(transform.Position.Y);
                writer.WriteSingle(transform.Position.Z);
            }
            if ((mask & TransformRotation) != 0)
            {
                writer.WriteSingle(transform.Rotation.X);
                writer.WriteSingle(transform.Rotation.Y);
                writer.WriteSingle(transform.Rotation.Z);
                writer.WriteSingle(transform.Rotation.W);
            }
            if ((mask & TransformScale) != 0)
            {
                writer.WriteSingle(transform.Scale.X);
                writer.WriteSingle(transform.Scale.Y);
                writer.WriteSingle(transform.Scale.Z);
            }
        }

        private static object ReadPatch(GameplaySaveReader reader, ODFieldSaveMeta field, byte mask)
        {
            if (field.PatchKind != ODFieldPatchKind.Transform || mask == 0 ||
                (mask & ~(TransformPosition | TransformRotation | TransformScale)) != 0)
            {
                throw new InvalidDataException($"Field {field.StableName} has an invalid replication patch");
            }

            Position position = default;
            Rotation rotation = default;
            Float3 scale = default;
            if ((mask & TransformPosition) != 0)
            {
                GameplaySpatialSpaceID space = new GameplaySpatialSpaceID(reader.ReadString());
                position = new Position(space, reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }
            if ((mask & TransformRotation) != 0)
            {
                rotation = new Rotation(
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }
            if ((mask & TransformScale) != 0)
            {
                scale = new Float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }
            return new Transform(position, rotation, scale);
        }

        internal static byte GetPatchMask(ODFieldSaveMeta field, object oldValue, object newValue)
        {
            if (field.PatchKind != ODFieldPatchKind.Transform ||
                !(oldValue is Transform oldTransform) || !(newValue is Transform newTransform))
            {
                return 0;
            }

            byte mask = 0;
            if (oldTransform.Position != newTransform.Position) mask |= TransformPosition;
            if (oldTransform.Rotation != newTransform.Rotation) mask |= TransformRotation;
            if (oldTransform.Scale != newTransform.Scale) mask |= TransformScale;
            return mask;
        }

        internal static object MergePatch(ODFieldSaveMeta field, object currentValue, object patchValue, byte mask)
        {
            if (field.PatchKind != ODFieldPatchKind.Transform ||
                !(currentValue is Transform current) || !(patchValue is Transform patch))
            {
                throw new InvalidDataException($"Field {field.StableName} cannot apply a replication patch");
            }

            if ((mask & TransformPosition) != 0) current.Position = patch.Position;
            if ((mask & TransformRotation) != 0) current.Rotation = patch.Rotation;
            if ((mask & TransformScale) != 0) current.Scale = patch.Scale;
            return current;
        }

        public static long ReadRevision(GameplayMachine machine, byte[] bytes)
        {
            using (GameplaySaveReader reader = new GameplaySaveReader(bytes, machine.Serializers, GameplaySerializationPurpose.Network))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != Version)
                {
                    throw new InvalidDataException("Gameplay replication frame header does not match");
                }
                return reader.ReadInt64();
            }
        }

        private static int ReadCount(GameplaySaveReader reader, string kind)
        {
            int value = reader.ReadCount();
            if (value < 0 || value > 1_000_000)
            {
                throw new InvalidDataException($"Gameplay replication {kind} count is invalid");
            }
            return value;
        }
    }

    internal abstract class GameplayNetworkSession : IDisposable
    {
        private struct FragmentKey : IEquatable<FragmentKey>
        {
            public GameplayConnectionID Connection;
            public long MessageID;

            public bool Equals(FragmentKey other) => Connection.Equals(other.Connection) && MessageID == other.MessageID;
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

        protected readonly GameplayMachine Machine;
        protected readonly IGameplayNetworkTransport Transport;
        protected readonly GameplayNetworkOptions Options;
        protected readonly Dictionary<ulong, ODRpcMeta> RpcsByID;
        protected readonly Dictionary<Type, ODRpcMeta> RpcsByType;
        protected readonly Dictionary<ulong, ODMulticastEventMeta> MulticastEventsByID;
        protected readonly Dictionary<Type, ODMulticastEventMeta> MulticastEventsByType;
        private readonly Dictionary<FragmentKey, FragmentAccumulator> m_fragments =
            new Dictionary<FragmentKey, FragmentAccumulator>();
        private long m_nextFragmentMessageID;

        public GameplayNetworkState State { get; protected set; }
        public long Revision { get; protected set; }

        protected GameplayNetworkSession(GameplayMachine machine, IGameplayNetworkTransport transport, GameplayNetworkOptions options)
        {
            Machine = machine;
            Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Options = options ?? new GameplayNetworkOptions();
            if (Options.MaxMessageBytes <= 0 || Options.FragmentPayloadBytes <= 0 ||
                Options.FragmentPayloadBytes >= Options.MaxMessageBytes ||
                Options.MaxFragmentsPerMessage <= 1 || Options.MaxPendingFragmentedMessages <= 0 ||
                Options.FragmentTimeoutMilliseconds <= 0 || Options.PredictionTimeoutMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Gameplay network fragmentation options are invalid");
            }
            RpcsByID = new Dictionary<ulong, ODRpcMeta>();
            RpcsByType = new Dictionary<Type, ODRpcMeta>();
            MulticastEventsByID = new Dictionary<ulong, ODMulticastEventMeta>();
            MulticastEventsByType = new Dictionary<Type, ODMulticastEventMeta>();
            foreach (IODNetworkModule module in machine.Modules.OfType<IODNetworkModule>())
            {
                foreach (ODRpcMeta rpc in module.GetAllODRpcMetas() ?? Enumerable.Empty<ODRpcMeta>())
                {
                    bool isPlayerInput = typeof(IPlayerInputInterface).IsAssignableFrom(rpc.ParamType);
                    if (!isPlayerInput || rpc.InterfaceType != GameplayInterfaceType.PlayerInput)
                        continue;
                    if (machine.SynchronizationMode == GameplaySynchronizationMode.Lockstep)
                    {
                        if (rpc.Mode != GameplayRpcMode.Lockstep)
                            continue;
                        RpcsByID.Add(rpc.StableID, rpc);
                        RpcsByType.Add(rpc.ParamType, rpc);
                        continue;
                    }
                    if (rpc.Mode == GameplayRpcMode.Lockstep)
                        continue;
                    if (rpc.Mode != GameplayRpcMode.Authority && rpc.Mode != GameplayRpcMode.Predictable)
                        throw new InvalidOperationException($"PlayerInput RPC {rpc.StableName} has an invalid transport mode");
                    if ((rpc.Mode == GameplayRpcMode.Predictable) !=
                        typeof(IPredictableInterface).IsAssignableFrom(rpc.ParamType))
                        throw new InvalidOperationException($"PlayerInput RPC {rpc.StableName} has inconsistent prediction metadata");
                    RpcsByID.Add(rpc.StableID, rpc);
                    RpcsByType.Add(rpc.ParamType, rpc);
                }
            }
            foreach (IODMulticastEventNetworkModule module in machine.Modules.OfType<IODMulticastEventNetworkModule>())
            {
                foreach (ODMulticastEventMeta eventMeta in module.GetAllODMulticastEventMetas() ??
                         Enumerable.Empty<ODMulticastEventMeta>())
                {
                    if (eventMeta.EventType == null ||
                        !typeof(IMulticastEvent).IsAssignableFrom(eventMeta.EventType))
                        throw new InvalidOperationException(
                            $"Multicast event {eventMeta.StableName} has an invalid event type");
                    if (eventMeta.Writer == null || eventMeta.Reader == null)
                        throw new InvalidOperationException(
                            $"Multicast event {eventMeta.StableName} does not have a generated codec");
                    MulticastEventsByID.Add(eventMeta.StableID, eventMeta);
                    MulticastEventsByType.Add(eventMeta.EventType, eventMeta);
                }
            }
        }

        public abstract void Start();
        public abstract void Update();
        public virtual void OnObjectLifeChanged(GObjectID objectID, bool created) { }
        public virtual void OnObjectNetworkMetadataChanged(
            GObjectID objectID,
            GObjectID oldOwnerID) { }
        public virtual void OnFieldChanged(
            GObjectID objectID,
            ODFieldName fieldName,
            ObjectEventTypes eventType,
            object context,
            object newValue,
            object oldValue) { }
        public virtual void OnRootChanged() { }
        public virtual void OnPartitionsChanged(GObjectID objectID) { }
        public virtual void FlushChanges() { }

        protected void SendMessage(
            GameplayConnectionID connection,
            byte[] message,
            GameplayNetworkDelivery delivery = GameplayNetworkDelivery.ReliableOrdered)
        {
            if (message == null || message.Length > Options.MaxMessageBytes)
            {
                throw new InvalidOperationException($"Gameplay network message exceeds {Options.MaxMessageBytes} bytes");
            }
            if (message.Length <= Options.FragmentPayloadBytes)
            {
                Transport.Send(connection, message, delivery);
                return;
            }
            if (delivery != GameplayNetworkDelivery.ReliableOrdered)
            {
                throw new InvalidOperationException("Fragmented gameplay messages must use reliable ordered delivery");
            }

            long messageID = ++m_nextFragmentMessageID;
            int count = (message.Length + Options.FragmentPayloadBytes - 1) / Options.FragmentPayloadBytes;
            if (count > Options.MaxFragmentsPerMessage)
            {
                throw new InvalidOperationException(
                    $"Gameplay network message requires {count} fragments; maximum is {Options.MaxFragmentsPerMessage}");
            }
            for (int index = 0; index < count; index++)
            {
                int offset = index * Options.FragmentPayloadBytes;
                int length = Math.Min(Options.FragmentPayloadBytes, message.Length - offset);
                Transport.Send(connection, GameplayNetworkFragmentCodec.Write(
                    messageID, index, count, message.Length, message, offset, length), delivery);
            }
        }

        protected bool TryReceiveMessage(GameplayConnectionID connection, byte[] data, out byte[] message)
        {
            CleanupFragments();
            GameplayNetworkFragment fragment;
            if (!GameplayNetworkFragmentCodec.TryRead(data, Options.MaxMessageBytes, out fragment))
            {
                message = data;
                return true;
            }
            if (fragment.Count > Options.MaxFragmentsPerMessage)
            {
                throw new InvalidDataException(
                    $"Gameplay network message declares {fragment.Count} fragments; maximum is {Options.MaxFragmentsPerMessage}");
            }

            FragmentKey key = new FragmentKey { Connection = connection, MessageID = fragment.MessageID };
            FragmentAccumulator accumulator;
            if (!m_fragments.TryGetValue(key, out accumulator))
            {
                if (m_fragments.Count >= Options.MaxPendingFragmentedMessages)
                {
                    throw new InvalidDataException("Too many fragmented gameplay messages are pending");
                }
                accumulator = new FragmentAccumulator
                {
                    Parts = new byte[fragment.Count][],
                    TotalLength = fragment.TotalLength,
                    LastUpdatedUtc = DateTime.UtcNow,
                };
                m_fragments.Add(key, accumulator);
            }
            else if (accumulator.Parts.Length != fragment.Count || accumulator.TotalLength != fragment.TotalLength)
            {
                throw new InvalidDataException("Gameplay network fragment metadata changed during reassembly");
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
                throw new InvalidDataException("Gameplay network fragments exceed the declared message size");
            }
            if (accumulator.ReceivedCount != accumulator.Parts.Length)
            {
                message = null;
                return false;
            }
            if (accumulator.ReceivedBytes != accumulator.TotalLength)
            {
                m_fragments.Remove(key);
                throw new InvalidDataException("Gameplay network fragments do not match the declared message size");
            }

            message = new byte[accumulator.TotalLength];
            int offset = 0;
            foreach (byte[] part in accumulator.Parts)
            {
                Buffer.BlockCopy(part, 0, message, offset, part.Length);
                offset += part.Length;
            }
            m_fragments.Remove(key);
            return true;
        }

        protected void DiscardFragments(GameplayConnectionID connection)
        {
            foreach (FragmentKey key in m_fragments.Keys.Where(key => key.Connection.Equals(connection)).ToArray())
            {
                m_fragments.Remove(key);
            }
        }

        private void CleanupFragments()
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(-Options.FragmentTimeoutMilliseconds);
            foreach (FragmentKey key in m_fragments
                .Where(pair => pair.Value.LastUpdatedUtc < deadline)
                .Select(pair => pair.Key)
                .ToArray())
            {
                m_fragments.Remove(key);
            }
        }

        public ODRpcMeta GetRpc(Type type)
        {
            ODRpcMeta result;
            if (!RpcsByType.TryGetValue(type, out result))
            {
                throw new InvalidOperationException($"No generated network RPC codec is registered for {type}");
            }
            return result;
        }

        protected ODMulticastEventMeta GetMulticastEvent(Type type)
        {
            if (!MulticastEventsByType.TryGetValue(type, out ODMulticastEventMeta result))
                throw new InvalidOperationException(
                    $"No generated multicast event codec is registered for {type}");
            return result;
        }

        protected static bool PredictionReports(
            GameplayPredictionMode mode,
            bool succeeded)
        {
            return mode == GameplayPredictionMode.Both ||
                   succeeded && mode == GameplayPredictionMode.OnlySuccess ||
                   !succeeded && mode == GameplayPredictionMode.OnlyFailure;
        }

        protected void SetState(GameplayNetworkState state, string reason = null)
        {
            GameplayNetworkState oldState = State;
            if (oldState == state)
            {
                return;
            }
            State = state;
            if (Machine.Logger is GameplayLogger)
            {
                Machine.Logger.Log(XLogger.LogVerbosity.Common, LogCatagories.Network,
                    $"{Machine.Role} network state {oldState} -> {state}" +
                    (string.IsNullOrWhiteSpace(reason) ? string.Empty : $". Reason: {reason}"));
            }
            Machine.BroadCastMachineEvent(new GameplayNetworkStateChangedParam
            {
                OldState = oldState,
                NewState = state,
                Reason = reason,
            });
        }

        public virtual void Dispose()
        {
            Transport.Stop();
            Transport.Dispose();
            SetState(GameplayNetworkState.Disabled);
        }
    }

    internal sealed class AuthorityGameplayNetworkSession : GameplayNetworkSession
    {
        private sealed class ConnectionState
        {
            public readonly HashSet<GObjectID> KnownObjects = new HashSet<GObjectID>();
            public GObjectID PlayerControllerID;
            public GObjectID RootObjectID;
        }

        private readonly struct NetworkMetadata
        {
            public readonly GObjectID OwnerID;

            public NetworkMetadata(GObjectID ownerID)
            {
                OwnerID = ownerID;
            }
        }

        private readonly Dictionary<GObjectID, Dictionary<ODFieldName, GameplayReplicationDirtyField>> m_dirty =
            new Dictionary<GObjectID, Dictionary<ODFieldName, GameplayReplicationDirtyField>>();
        private readonly HashSet<GObjectID> m_created = new HashSet<GObjectID>();
        private readonly HashSet<GObjectID> m_deleted = new HashSet<GObjectID>();
        private readonly HashSet<GameplayConnectionID> m_connected = new HashSet<GameplayConnectionID>();
        private readonly Dictionary<GameplayConnectionID, ConnectionState> m_connections =
            new Dictionary<GameplayConnectionID, ConnectionState>();
        private readonly Dictionary<GObjectID, NetworkMetadata> m_metadataChanged =
            new Dictionary<GObjectID, NetworkMetadata>();
        private readonly HashSet<GameplayConnectionID> m_playerControllerChanged =
            new HashSet<GameplayConnectionID>();
        private readonly HashSet<GObjectID> m_spatialObjectsChanged = new HashSet<GObjectID>();
        private readonly HashSet<GObjectID> m_spatialViewersChanged = new HashSet<GObjectID>();
        private bool m_rootChanged;
        private bool m_partitionsChanged;
        public override void OnPartitionsChanged(GObjectID objectID) { m_partitionsChanged = true; }
        public void OnSpatialObjectChanged(GObjectID objectID) { m_spatialObjectsChanged.Add(objectID); }
        public void OnSpatialViewerChanged(GObjectID playerControllerID) { m_spatialViewersChanged.Add(playerControllerID); }

        public IEnumerable<GameplayConnectionID> Connections { get { return m_connections.Keys; } }
        public bool ContainsConnection(GameplayConnectionID connection) { return m_connected.Contains(connection); }

        public AuthorityGameplayNetworkSession(GameplayMachine machine, IGameplayNetworkTransport transport, GameplayNetworkOptions options)
            : base(machine, transport, options) { }

        public override void Start()
        {
            Transport.StartServer();
            SetState(GameplayNetworkState.Ready);
        }

        public override void Update()
        {
            GameplayNetworkTransportEvent networkEvent;
            while (Transport.TryPollEvent(out networkEvent))
            {
                try
                {
                    if (networkEvent.Type == GameplayNetworkTransportEventType.Connected)
                    {
                        m_connected.Add(networkEvent.Connection);
                    }
                    else if (networkEvent.Type == GameplayNetworkTransportEventType.Disconnected)
                    {
                        HandleDisconnected(networkEvent.Connection);
                        DiscardFragments(networkEvent.Connection);
                    }
                    else if (networkEvent.Type == GameplayNetworkTransportEventType.Data)
                    {
                        byte[] message;
                        if (TryReceiveMessage(networkEvent.Connection, networkEvent.Data, out message))
                        {
                            ProcessMessage(networkEvent.Connection, message);
                        }
                    }
                }
                catch (Exception exception)
                {
                    Transport.Disconnect(networkEvent.Connection, exception.Message);
                    HandleDisconnected(networkEvent.Connection);
                }
            }
        }

        private void HandleDisconnected(GameplayConnectionID connection)
        {
            PlayerController? playerController = Machine.GetPlayerController(connection);
            m_connected.Remove(connection);
            m_connections.Remove(connection);
            m_playerControllerChanged.Remove(connection);
            if (playerController.HasValue)
                Machine.NotifyPlayerDisconnected(playerController.Value);
            Machine.UnbindPlayerControllerForNetwork(connection);
            Options.PlayerDisconnected?.Invoke(Machine, connection, playerController);
            if (playerController.HasValue && Machine.IsAutomaticPlayerController(playerController.Value.ObjectID))
            {
                Machine.ForgetAutomaticPlayerController(playerController.Value.ObjectID);
                Machine.DeleteEngineGameplayObject(playerController.Value.ObjectID);
            }
        }

        public override void OnObjectLifeChanged(GObjectID objectID, bool created)
        {
            if (created)
            {
                m_deleted.Remove(objectID);
                m_created.Add(objectID);
                m_dirty.Remove(objectID);
            }
            else
            {
                m_dirty.Remove(objectID);
                if (!m_created.Remove(objectID))
                {
                    m_deleted.Add(objectID);
                }
            }
        }

        public override void OnObjectNetworkMetadataChanged(
            GObjectID objectID,
            GObjectID oldOwnerID)
        {
            if (!m_metadataChanged.ContainsKey(objectID))
                m_metadataChanged.Add(objectID, new NetworkMetadata(oldOwnerID));
        }

        public void OnPlayerControllerChanged(
            GameplayConnectionID connection,
            GObjectID oldPlayerControllerID,
            GObjectID newPlayerControllerID)
        {
            if (m_connections.ContainsKey(connection))
                m_playerControllerChanged.Add(connection);
        }

        public override void OnFieldChanged(
            GObjectID objectID,
            ODFieldName fieldName,
            ObjectEventTypes eventType,
            object context,
            object newValue,
            object oldValue)
        {
            if (fieldName.Meta.ReplicationMode == ODFieldReplicationMode.None ||
                m_deleted.Contains(objectID) || m_created.Contains(objectID))
            {
                return;
            }
            Dictionary<ODFieldName, GameplayReplicationDirtyField> fields;
            if (!m_dirty.TryGetValue(objectID, out fields))
            {
                fields = new Dictionary<ODFieldName, GameplayReplicationDirtyField>();
                m_dirty.Add(objectID, fields);
            }
            GameplayReplicationDirtyField dirtyField;
            if (!fields.TryGetValue(fieldName, out dirtyField))
            {
                dirtyField = new GameplayReplicationDirtyField();
                fields.Add(fieldName, dirtyField);
            }

            ODFieldSaveMeta saveMeta = GameplayMachineBase.GetODFieldSaveMeta(fieldName);
            byte patchMask = GameplayReplicationFormat.GetPatchMask(saveMeta, oldValue, newValue);
            if (patchMask != 0 && eventType == ObjectEventTypes.Changed && !dirtyField.SendFullValue)
            {
                dirtyField.PatchMask |= patchMask;
                dirtyField.Deltas.Clear();
                return;
            }
            bool canUseDelta = saveMeta.DeltaKind != ODCollectionDeltaKind.None &&
                (eventType == ObjectEventTypes.Add || eventType == ObjectEventTypes.Remove ||
                 eventType == ObjectEventTypes.ItemChanged);
            if (!canUseDelta)
            {
                dirtyField.SendFullValue = true;
                dirtyField.PatchMask = 0;
                dirtyField.Deltas.Clear();
                return;
            }
            if (dirtyField.SendFullValue)
                return;
            if (saveMeta.DeltaKind == ODCollectionDeltaKind.Set && eventType == ObjectEventTypes.ItemChanged)
                throw new InvalidOperationException($"Set field {saveMeta.StableName} emitted ItemChanged");

            object deltaValue = null;
            if (saveMeta.DeltaKind == ODCollectionDeltaKind.Map && eventType != ObjectEventTypes.Remove)
            {
                IGameplayObjectSaveManager manager = Machine.ObjectManager as IGameplayObjectSaveManager;
                object collection = manager?.GetStoredFieldsForSave(objectID)
                    .FirstOrDefault(item => item.Key.Equals(fieldName)).Value;
                if (!(collection is IGMCollection gmCollection))
                    throw new InvalidOperationException($"Map field {saveMeta.StableName} has no collection storage");
                deltaValue = gmCollection.Get(context);
            }
            dirtyField.Deltas.Add(new GameplayReplicationDelta
            {
                EventType = eventType,
                Key = context,
                Value = deltaValue,
            });
        }

        public override void OnRootChanged()
        {
            m_rootChanged = true;
        }

        public override void FlushChanges()
        {
            if (m_dirty.Count == 0 && m_created.Count == 0 && m_deleted.Count == 0 &&
                m_metadataChanged.Count == 0 && m_playerControllerChanged.Count == 0 &&
                m_spatialObjectsChanged.Count == 0 && m_spatialViewersChanged.Count == 0 &&
                !m_rootChanged && !m_partitionsChanged)
            {
                return;
            }
            Revision++;
            foreach (KeyValuePair<GameplayConnectionID, ConnectionState> pair in m_connections.ToArray())
            {
                GameplayConnectionID connection = pair.Key;
                ConnectionState state = pair.Value;
                GObjectID oldPlayerControllerID = state.PlayerControllerID;
                GObjectID playerControllerID = Machine.GetPlayerControllerID(connection);
                var deleted = new HashSet<GObjectID>(m_deleted.Where(state.KnownObjects.Contains));
                var created = new HashSet<GObjectID>();
                var metadata = new HashSet<GObjectID>();
                var resets = new Dictionary<GObjectID, HashSet<ODFieldName>>();
                var forceFull = new Dictionary<GObjectID, HashSet<ODFieldName>>();

                IEnumerable<GObjectID> visibilityCandidates = m_created.Concat(m_metadataChanged.Keys)
                    .Concat(m_partitionsChanged ? Machine.GetAllGameplayObjectIDs().Concat(state.KnownObjects) : Enumerable.Empty<GObjectID>())
                    .Concat(m_spatialObjectsChanged)
                    .Concat(m_spatialViewersChanged.Contains(playerControllerID)
                        ? Machine.GetSpatialVisibilityCandidates(playerControllerID)
                            .Concat(state.KnownObjects.Where(Machine.IsSpatiallyManaged))
                        : Enumerable.Empty<GObjectID>())
                    .Concat(m_playerControllerChanged.Contains(connection)
                        ? Machine.GetAllGameplayObjectIDs()
                        : Enumerable.Empty<GObjectID>())
                    .Distinct();
                foreach (GObjectID objectID in visibilityCandidates)
                {
                    bool wasKnown = state.KnownObjects.Contains(objectID);
                    bool visible = Machine.ContainsGameplayObject(objectID) && Machine.IsObjectVisibleTo(objectID, playerControllerID);
                    if (!wasKnown && visible)
                        created.Add(objectID);
                    else if (wasKnown && !visible)
                        deleted.Add(objectID);
                    else if (wasKnown && visible && (m_metadataChanged.ContainsKey(objectID) || m_partitionsChanged))
                        metadata.Add(objectID);
                }

                foreach (KeyValuePair<GObjectID, NetworkMetadata> change in m_metadataChanged)
                {
                    GObjectID objectID = change.Key;
                    if (!state.KnownObjects.Contains(objectID) || deleted.Contains(objectID) ||
                        created.Contains(objectID) || !Machine.ObjectManager.ContainsGameplayObject(objectID))
                        continue;
                    GObjectID newOwnerID = Machine.GetNetworkOwnerID(objectID);
                    if (change.Value.OwnerID.Equals(newOwnerID))
                        continue;
                    if (change.Value.OwnerID.Equals(playerControllerID))
                        AddOwnerFields(resets, objectID);
                    if (newOwnerID.Equals(playerControllerID))
                        AddOwnerFields(forceFull, objectID);
                }

                if (!oldPlayerControllerID.Equals(playerControllerID))
                {
                    foreach (GObjectID objectID in state.KnownObjects.Where(id =>
                                 !deleted.Contains(id) && Machine.ObjectManager.ContainsGameplayObject(id)))
                    {
                        GObjectID ownerID = Machine.GetNetworkOwnerID(objectID);
                        if (ownerID.Equals(oldPlayerControllerID))
                            AddOwnerFields(resets, objectID);
                        if (ownerID.Equals(playerControllerID))
                            AddOwnerFields(forceFull, objectID);
                    }
                }

                GObjectID visibleRoot = Machine.RootObjectID.IsValid &&
                                        Machine.IsObjectVisibleTo(Machine.RootObjectID, playerControllerID)
                    ? Machine.RootObjectID
                    : default;
                bool rootChanged = m_rootChanged || !state.RootObjectID.Equals(visibleRoot);
                Dictionary<GObjectID, Dictionary<ODFieldName, GameplayReplicationDirtyField>> visibleDirty =
                    m_dirty.Where(item =>
                            !deleted.Contains(item.Key) &&
                            (state.KnownObjects.Contains(item.Key) || created.Contains(item.Key)) &&
                            Machine.IsObjectVisibleTo(item.Key, playerControllerID))
                        .ToDictionary(item => item.Key, item => item.Value);
                byte[] frame = GameplayReplicationFormat.WriteFrame(
                    Machine,
                    Revision,
                    deleted,
                    created,
                    visibleDirty,
                    metadata,
                    resets,
                    forceFull,
                    playerControllerID,
                    rootChanged,
                    visibleRoot,
                    m_partitionsChanged,
                    m_deleted);
                SendMessage(connection, GameplayNetworkMessageCodec.Write(
                    GameplayNetworkMessageType.ReplicationFrame,
                    writer => GameplayNetworkMessageCodec.WriteBytes(writer, frame)));

                state.KnownObjects.ExceptWith(deleted);
                state.KnownObjects.UnionWith(created);
                state.PlayerControllerID = playerControllerID;
                state.RootObjectID = visibleRoot;
            }
            m_dirty.Clear();
            m_created.Clear();
            m_deleted.Clear();
            m_metadataChanged.Clear();
            m_playerControllerChanged.Clear();
            m_spatialObjectsChanged.Clear();
            m_spatialViewersChanged.Clear();
            m_rootChanged = false;
            m_partitionsChanged = false;
        }

        private void AddOwnerFields(
            Dictionary<GObjectID, HashSet<ODFieldName>> target,
            GObjectID objectID)
        {
            HashSet<ODFieldName> fields;
            if (!target.TryGetValue(objectID, out fields))
            {
                fields = new HashSet<ODFieldName>();
                target.Add(objectID, fields);
            }
            var visited = new HashSet<ODClassName>();
            AddClassFields(Machine.GetGameplayObjectClass(objectID));

            void AddClassFields(ODClassName className)
            {
                if (!visited.Add(className))
                    return;
                ODClassMeta meta = GameplayMachineBase.GetODClassMeta(className);
                foreach (ODClassName baseClass in meta.DirectBaseClasses)
                    AddClassFields(baseClass);
                foreach (ODFieldMeta field in meta.Fields)
                    if (!field.IsDriven && field.ReplicationMode == ODFieldReplicationMode.OwnerOnly)
                        fields.Add(field.Name);
            }
        }

        public void BroadcastMulticastInterface(IReplicatedInterface param)
        {
            ODRpcMeta rpc = GetRpc(param.GetType());
            if (rpc.Mode != GameplayRpcMode.Multicast || param.RpcMode != GameplayRpcMode.Multicast)
                throw new InvalidOperationException($"Interface {rpc.StableName} is not configured for Multicast RPC");
            byte[] payload = GameplayRpcCodec.Write(Machine, Machine.Serializers, rpc.ParamWriter, param);
            byte[] message = GameplayNetworkMessageCodec.Write(GameplayNetworkMessageType.MulticastInterface, writer =>
            {
                writer.Write(rpc.StableID);
                GameplayNetworkMessageCodec.WriteBytes(writer, payload);
            });
            foreach (GameplayConnectionID connection in m_connections.Keys.ToArray())
                SendMessage(connection, message);
        }

        public void BroadcastMulticastEvent(IMulticastEvent multicastEvent)
        {
            ODMulticastEventMeta eventMeta = GetMulticastEvent(multicastEvent.GetType());
            byte[] payload = GameplayRpcCodec.Write(
                Machine, Machine.Serializers, eventMeta.Writer, multicastEvent);
            byte[] message = GameplayNetworkMessageCodec.Write(
                GameplayNetworkMessageType.MulticastEvent,
                writer =>
                {
                    writer.Write(eventMeta.StableID);
                    GameplayNetworkMessageCodec.WriteBytes(writer, payload);
                });
            foreach (GameplayConnectionID connection in m_connections.Keys.ToArray())
                SendMessage(connection, message);
        }

        private void ProcessMessage(GameplayConnectionID connection, byte[] bytes)
        {
            GameplayNetworkMessageCodec.Read(bytes, Options.MaxMessageBytes, (type, reader) =>
            {
                switch (type)
                {
                    case GameplayNetworkMessageType.Hello:
                        ProcessHello(
                            connection,
                            reader.ReadUInt64(),
                            reader.ReadUInt64(),
                            GameplayNetworkMessageCodec.ReadBytes(reader, Options.MaxMessageBytes));
                        break;
                    case GameplayNetworkMessageType.AuthorityInterface:
                        ProcessAuthorityInterface(connection, reader.ReadUInt64(), GameplayNetworkMessageCodec.ReadBytes(reader, Options.MaxMessageBytes));
                        break;
                    case GameplayNetworkMessageType.PredictableInterface:
                        ProcessPredictableInterface(
                            connection,
                            reader.ReadInt64(),
                            reader.ReadUInt64(),
                            GameplayNetworkMessageCodec.ReadBytes(reader, Options.MaxMessageBytes));
                        break;
                    case GameplayNetworkMessageType.ResyncRequest:
                        SendWelcome(connection);
                        break;
                    default:
                        throw new InvalidDataException($"Client sent invalid gameplay network message {type}");
                }
            });
        }

        private void ProcessAuthorityInterface(GameplayConnectionID connection, ulong rpcID, byte[] payload)
        {
            if (!m_connections.ContainsKey(connection))
                throw new InvalidOperationException("Authority interface received before gameplay synchronization completed");
            if (!RpcsByID.TryGetValue(rpcID, out ODRpcMeta rpc) || rpc.Mode != GameplayRpcMode.Authority)
                throw new InvalidDataException($"Unknown Authority interface {rpcID}");

            object boxedParam = GameplayRpcCodec.Read(Machine, Machine.Serializers, rpc.ParamReader, payload);
            IReplicatedInterface param = boxedParam as IReplicatedInterface;
            if (param == null || param.RpcMode != GameplayRpcMode.Authority)
                throw new InvalidDataException($"Authority interface {rpc.StableName} has an invalid generated contract");
            if (Options.AuthorizeServerRpc != null && !Options.AuthorizeServerRpc(connection, rpc, boxedParam))
                return;

            GameplayRpcContext? previous = Machine.PushRpcContext(connection);
            try
            {
                Machine.ExecuteReplicatedFromNetwork(param, GameplayRpcMode.Authority);
            }
            finally
            {
                Machine.RestoreRpcContext(previous);
            }
        }

        private void ProcessPredictableInterface(
            GameplayConnectionID connection,
            long requestID,
            ulong rpcID,
            byte[] payload)
        {
            if (!m_connections.ContainsKey(connection))
                throw new InvalidOperationException("Predictable interface received before gameplay synchronization completed");
            if (!RpcsByID.TryGetValue(rpcID, out ODRpcMeta rpc) || rpc.Mode != GameplayRpcMode.Predictable)
                throw new InvalidDataException($"Unknown Predictable interface {rpcID}");

            object boxedParam = GameplayRpcCodec.Read(Machine, Machine.Serializers, rpc.ParamReader, payload);
            IPredictableInterface param = boxedParam as IPredictableInterface;
            if (param == null || param.RpcMode != GameplayRpcMode.Predictable ||
                !Enum.IsDefined(typeof(GameplayPredictionMode), param.PredictionMode) ||
                param.PredictionMode == GameplayPredictionMode.None)
                throw new InvalidDataException($"Predictable interface {rpc.StableName} has an invalid generated contract");

            bool succeeded = Options.AuthorizeServerRpc == null ||
                Options.AuthorizeServerRpc(connection, rpc, boxedParam);
            if (succeeded)
            {
                GameplayRpcContext? previous = Machine.PushRpcContext(connection);
                try
                {
                    succeeded = Machine.ExecuteReplicatedFromNetwork(param, GameplayRpcMode.Predictable).Sussceeded;
                }
                finally
                {
                    Machine.RestoreRpcContext(previous);
                }
            }

            if (PredictionReports(param.PredictionMode, succeeded))
            {
                SendMessage(connection, GameplayNetworkMessageCodec.Write(
                    GameplayNetworkMessageType.PredictionResponse,
                    writer =>
                    {
                        writer.Write(requestID);
                        writer.Write(succeeded);
                    }));
            }
        }

        private void ProcessHello(
            GameplayConnectionID connection,
            ulong schemaID,
            ulong resourceCatalogID,
            byte[] connectionData)
        {
            bool wasSynchronized = m_connections.ContainsKey(connection);
            if (schemaID != Machine.NetworkSchemaID)
            {
                RejectConnection(connection,
                    $"OD network schema mismatch. Authority={Machine.NetworkSchemaID}, Client={schemaID}");
                return;
            }
            if (resourceCatalogID != Machine.ResourceCatalogID)
            {
                RejectConnection(connection,
                    $"OD resource catalog mismatch. Authority={Machine.ResourceCatalogID}, Client={resourceCatalogID}");
                return;
            }
            if (!wasSynchronized)
            {
                CommonGameplayObject? resolved = Options.ResolvePlayerController?.Invoke(
                    Machine, connection, connectionData ?? Array.Empty<byte>());
                PlayerController playerController;
                if (resolved.HasValue)
                {
                    CommonGameplayObject value = resolved.Value;
                    if (!ReferenceEquals(value.Machine, Machine) || !Machine.ContainsGameplayObject(value.ObjectID) ||
                        !Machine.GetGameplayObjectClass(value.ObjectID).Equals(PlayerController.ClassName))
                    {
                        RejectConnection(connection,
                            "Resolved PlayerController is not a GameplayMachine-managed PlayerController");
                        return;
                    }
                    playerController = new PlayerController(value);
                }
                else
                {
                    playerController = Machine.CreateAutomaticPlayerController();
                }
                Machine.BindPlayerControllerForNetwork(connection, playerController.ObjectID);
                Options.InitializePlayerController?.Invoke(
                    Machine, connection, playerController, connectionData ?? Array.Empty<byte>());
            }
            PlayerController? joinedPlayer = Machine.GetPlayerController(connection);
            if (!wasSynchronized && joinedPlayer.HasValue)
            {
                Machine.NotifyPlayerJoin(joinedPlayer.Value);
                Machine.ReconcileGameplayInterest();
            }
            SendWelcome(connection);
        }

        private void RejectConnection(GameplayConnectionID connection, string reason)
        {
            Machine.Logger.LogError(XLogger.LogVerbosity.Common, LogCatagories.GameplayMachine,
                $"Rejected gameplay network connection {connection}: {reason}");
            SendMessage(connection, GameplayNetworkMessageCodec.Write(
                GameplayNetworkMessageType.Reject, writer => writer.Write(reason)));
            Transport.Disconnect(connection, reason);
        }

        private void SendWelcome(GameplayConnectionID connection)
        {
            // The snapshot becomes this client's baseline. Do not carry pre-snapshot
            // ownership changes into a later delta and lose the old owner's reset.
            FlushChanges();
            m_connected.Add(connection);
            GObjectID playerControllerID = Machine.GetPlayerControllerID(connection);
            List<GObjectID> visibleObjects = Machine.GetAllGameplayObjectIDs()
                .Where(id => Machine.IsObjectVisibleTo(id, playerControllerID))
                .ToList();
            var visibleSet = new HashSet<GObjectID>(visibleObjects);
            GObjectID visibleRoot = visibleSet.Contains(Machine.RootObjectID) ? Machine.RootObjectID : default;
            byte[] snapshot = GameplayMachineSaveFormat.Save(
                Machine,
                Machine.ObjectManager,
                Machine.Serializers,
                visibleSet.Contains,
                (objectID, field) => Machine.IsFieldVisibleTo(objectID, field, playerControllerID),
                GameplaySerializationPurpose.Network,
                visibleRoot,
                recipient: playerControllerID);
            SendMessage(connection, GameplayNetworkMessageCodec.Write(GameplayNetworkMessageType.Welcome, writer =>
            {
                writer.Write(playerControllerID.ID);
                writer.Write(Revision);
                GameplayNetworkMessageCodec.WriteBytes(writer, snapshot);
            }));
            var state = new ConnectionState
            {
                PlayerControllerID = playerControllerID,
                RootObjectID = visibleRoot,
            };
            state.KnownObjects.UnionWith(visibleObjects);
            m_connections[connection] = state;
        }

    }

    internal sealed class ClientGameplayNetworkSession : GameplayNetworkSession
    {
        private sealed class PendingPrediction
        {
            public IPredictableInterface Param;
            public DateTime SentUtc;
        }

        private GameplayConnectionID m_authorityConnection;
        private readonly Dictionary<long, PendingPrediction> m_pendingPredictions =
            new Dictionary<long, PendingPrediction>();
        private long m_nextPredictionID;

        public ClientGameplayNetworkSession(GameplayMachine machine, IGameplayNetworkTransport transport, GameplayNetworkOptions options)
            : base(machine, transport, options) { }

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
                if (networkEvent.Type == GameplayNetworkTransportEventType.Connected)
                {
                    m_authorityConnection = networkEvent.Connection;
                    SendMessage(m_authorityConnection, GameplayNetworkMessageCodec.Write(
                        GameplayNetworkMessageType.Hello,
                        writer =>
                        {
                            writer.Write(Machine.NetworkSchemaID);
                            writer.Write(Machine.ResourceCatalogID);
                            GameplayNetworkMessageCodec.WriteBytes(writer, Options.ConnectionData);
                        }));
                    SetState(GameplayNetworkState.Synchronizing);
                }
                else if (networkEvent.Type == GameplayNetworkTransportEventType.Disconnected)
                {
                    DiscardFragments(networkEvent.Connection);
                    Machine.SetLocalPlayerControllerForReplication(default);
                    SetState(GameplayNetworkState.Disabled, networkEvent.Reason);
                    FailPendingPredictions();
                }
                else if (networkEvent.Type == GameplayNetworkTransportEventType.Data)
                {
                    try
                    {
                        byte[] message;
                        if (TryReceiveMessage(networkEvent.Connection, networkEvent.Data, out message))
                        {
                            ProcessMessage(message);
                        }
                    }
                    catch (Exception exception)
                    {
                        Machine.Logger.LogError(XLogger.LogVerbosity.Common, LogCatagories.Network,
                            $"Client failed to process a network message: {exception}");
                        Transport.Disconnect(m_authorityConnection, exception.Message);
                        SetState(GameplayNetworkState.Disabled, exception.Message);
                        FailPendingPredictions();
                    }
                }
            }
            CleanupPendingPredictions();
        }

        public ODCore.EventError CanSendAuthorityInterface(IReplicatedInterface param)
        {
            if (State != GameplayNetworkState.Ready)
                return "ClientProxy is not synchronized with an Authority";
            ODRpcMeta rpc;
            if (!RpcsByType.TryGetValue(param.GetType(), out rpc) || rpc.Mode != GameplayRpcMode.Authority)
                return $"No generated Authority RPC codec is registered for {param.GetType()}";
            return true;
        }

        public ODCore.EventError SendAuthorityInterface(IReplicatedInterface param)
        {
            ODCore.EventError error = CanSendAuthorityInterface(param);
            if (!error)
                return error;
            ODRpcMeta rpc = GetRpc(param.GetType());
            byte[] payload = GameplayRpcCodec.Write(Machine, Machine.Serializers, rpc.ParamWriter, param);
            SendMessage(m_authorityConnection, GameplayNetworkMessageCodec.Write(GameplayNetworkMessageType.AuthorityInterface, writer =>
            {
                writer.Write(rpc.StableID);
                GameplayNetworkMessageCodec.WriteBytes(writer, payload);
            }));
            return true;
        }

        public ODCore.EventError CanSendPredictableInterface(IPredictableInterface param)
        {
            if (State != GameplayNetworkState.Ready)
                return "ClientProxy is not synchronized with an Authority";
            ODRpcMeta rpc;
            if (!RpcsByType.TryGetValue(param.GetType(), out rpc) || rpc.Mode != GameplayRpcMode.Predictable)
                return $"No generated Predictable RPC codec is registered for {param.GetType()}";
            if (!Enum.IsDefined(typeof(GameplayPredictionMode), param.PredictionMode) ||
                param.PredictionMode == GameplayPredictionMode.None)
                return "Predictable PlayerInput must select a prediction type";
            return true;
        }

        public ODCore.EventError SendPredictableInterface(IPredictableInterface param)
        {
            ODCore.EventError error = CanSendPredictableInterface(param);
            if (!error)
                return error;

            ODRpcMeta rpc = GetRpc(param.GetType());
            byte[] payload = GameplayRpcCodec.Write(Machine, Machine.Serializers, rpc.ParamWriter, param);
            long requestID = ++m_nextPredictionID;
            m_pendingPredictions.Add(requestID, new PendingPrediction
            {
                Param = param,
                SentUtc = DateTime.UtcNow,
            });
            try
            {
                SendMessage(m_authorityConnection, GameplayNetworkMessageCodec.Write(
                    GameplayNetworkMessageType.PredictableInterface,
                    writer =>
                    {
                        writer.Write(requestID);
                        writer.Write(rpc.StableID);
                        GameplayNetworkMessageCodec.WriteBytes(writer, payload);
                    }));
            }
            catch (Exception exception)
            {
                m_pendingPredictions.Remove(requestID);
                return exception.Message;
            }
            return true;
        }

        public ODCore.EventError SnapshotPredictableInterface(
            IPredictableInterface param,
            out IPredictableInterface snapshot)
        {
            snapshot = null;
            ODCore.EventError error = CanSendPredictableInterface(param);
            if (!error)
                return error;

            ODRpcMeta rpc = GetRpc(param.GetType());
            byte[] payload = GameplayRpcCodec.Write(Machine, Machine.Serializers, rpc.ParamWriter, param);
            snapshot = GameplayRpcCodec.Read(Machine, Machine.Serializers, rpc.ParamReader, payload)
                as IPredictableInterface;
            if (snapshot == null || snapshot.RpcMode != GameplayRpcMode.Predictable ||
                snapshot.PredictionMode != param.PredictionMode)
                return $"Predictable interface {rpc.StableName} has an invalid generated contract";
            return true;
        }

        private void ProcessMessage(byte[] bytes)
        {
            GameplayNetworkMessageCodec.Read(bytes, Options.MaxMessageBytes, (type, reader) =>
            {
                switch (type)
                {
                    case GameplayNetworkMessageType.Welcome:
                        ProcessWelcome(
                            new GObjectID { ID = reader.ReadInt32() },
                            reader.ReadInt64(),
                            GameplayNetworkMessageCodec.ReadBytes(reader, Options.MaxMessageBytes));
                        break;
                    case GameplayNetworkMessageType.Reject:
                        string reason = reader.ReadString();
                        SetState(GameplayNetworkState.Disabled, reason);
                        FailPendingPredictions();
                        break;
                    case GameplayNetworkMessageType.ReplicationFrame:
                        ProcessFrame(GameplayNetworkMessageCodec.ReadBytes(reader, Options.MaxMessageBytes));
                        break;
                    case GameplayNetworkMessageType.MulticastInterface:
                        ProcessMulticastInterface(reader.ReadUInt64(), GameplayNetworkMessageCodec.ReadBytes(reader, Options.MaxMessageBytes));
                        break;
                    case GameplayNetworkMessageType.PredictionResponse:
                        ProcessPredictionResponse(reader.ReadInt64(), reader.ReadBoolean());
                        break;
                    case GameplayNetworkMessageType.MulticastEvent:
                        ProcessMulticastEvent(
                            reader.ReadUInt64(),
                            GameplayNetworkMessageCodec.ReadBytes(reader, Options.MaxMessageBytes));
                        break;
                    default:
                        throw new InvalidDataException($"Authority sent invalid gameplay network message {type}");
                }
            });
        }

        private void ProcessMulticastInterface(ulong rpcID, byte[] payload)
        {
            if (!RpcsByID.TryGetValue(rpcID, out ODRpcMeta rpc) || rpc.Mode != GameplayRpcMode.Multicast)
                throw new InvalidDataException($"Unknown Multicast interface {rpcID}");
            object boxedParam = GameplayRpcCodec.Read(Machine, Machine.Serializers, rpc.ParamReader, payload);
            IReplicatedInterface param = boxedParam as IReplicatedInterface;
            if (param == null || param.RpcMode != GameplayRpcMode.Multicast)
                throw new InvalidDataException($"Multicast interface {rpc.StableName} has an invalid generated contract");
            Machine.ExecuteReplicatedFromNetwork(param, GameplayRpcMode.Multicast);
        }

        private void ProcessMulticastEvent(ulong eventID, byte[] payload)
        {
            if (!MulticastEventsByID.TryGetValue(eventID, out ODMulticastEventMeta eventMeta))
                throw new InvalidDataException($"Unknown multicast event {eventID}");
            object value = GameplayRpcCodec.Read(
                Machine, Machine.Serializers, eventMeta.Reader, payload);
            if (!(value is IMulticastEvent multicastEvent))
                throw new InvalidDataException(
                    $"Multicast event {eventMeta.StableName} has an invalid generated contract");
            Machine.ReceiveMulticastEvent(multicastEvent);
        }

        private void ProcessPredictionResponse(long requestID, bool succeeded)
        {
            if (!m_pendingPredictions.TryGetValue(requestID, out PendingPrediction pending))
                throw new InvalidDataException($"Unknown prediction response {requestID}");
            m_pendingPredictions.Remove(requestID);
            if (!PredictionReports(pending.Param.PredictionMode, succeeded))
                throw new InvalidDataException($"Unexpected prediction response {requestID}");
            Machine.CompletePredictionFromNetwork(pending.Param, succeeded);
        }

        private void FailPendingPredictions()
        {
            PendingPrediction[] pending = m_pendingPredictions.Values.ToArray();
            m_pendingPredictions.Clear();
            foreach (PendingPrediction item in pending)
                if (PredictionReports(item.Param.PredictionMode, false))
                    Machine.CompletePredictionFromNetwork(item.Param, false);
        }

        private void CleanupPendingPredictions()
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(-Options.PredictionTimeoutMilliseconds);
            foreach (long requestID in m_pendingPredictions
                         .Where(item => item.Value.SentUtc < deadline)
                         .Select(item => item.Key)
                         .ToArray())
            {
                PendingPrediction pending = m_pendingPredictions[requestID];
                m_pendingPredictions.Remove(requestID);
                if (pending.Param.PredictionMode == GameplayPredictionMode.Both)
                    Machine.CompletePredictionFromNetwork(pending.Param, false);
            }
        }

        private void ProcessWelcome(GObjectID playerControllerID, long revision, byte[] snapshot)
        {
            Machine.BeginApplyingReplication();
            try
            {
                GameplayMachineSaveFormat.Load(Machine, Machine.ObjectManager, snapshot, Machine.Serializers,
                    GameplaySerializationPurpose.Network);
                Machine.SetLocalPlayerControllerForReplication(playerControllerID);
                Revision = revision;
            }
            finally
            {
                Machine.EndApplyingReplication();
            }
            SetState(GameplayNetworkState.Ready);
        }

        private void ProcessFrame(byte[] frame)
        {
            if (State != GameplayNetworkState.Ready)
            {
                return;
            }
            long incomingRevision = GameplayReplicationFormat.ReadRevision(Machine, frame);
            if (incomingRevision != Revision + 1)
            {
                SendMessage(m_authorityConnection, GameplayNetworkMessageCodec.Write(GameplayNetworkMessageType.ResyncRequest, null));
                SetState(GameplayNetworkState.Synchronizing, $"Replication gap: expected {Revision + 1}, received {incomingRevision}");
                return;
            }

            Machine.BeginApplyingReplication();
            long revision;
            try
            {
                revision = GameplayReplicationFormat.ApplyFrame(Machine, frame);
            }
            finally
            {
                Machine.EndApplyingReplication();
            }
            Revision = revision;
        }

        public override void Dispose()
        {
            Machine.SetLocalPlayerControllerForReplication(default);
            FailPendingPredictions();
            base.Dispose();
        }

    }

    public partial class GameplayMachine
    {
        [NonSerialized] private GameplaySynchronizationMode m_synchronizationMode;
        [NonSerialized] private GameplayMachineRole m_role = GameplayMachineRole.Authority;
        [NonSerialized] private GameplayNetworkSession m_networkSession;
        [NonSerialized] private IODModule[] m_modules = Array.Empty<IODModule>();
        [NonSerialized] private bool m_applyingReplication;
        [NonSerialized] private GObjectID m_clientLocalIDAllocator;
        [NonSerialized] private ODFieldSaveMeta[] m_networkFieldMetas = Array.Empty<ODFieldSaveMeta>();
        [NonSerialized] private Dictionary<ODFieldName, uint> m_networkFieldIndices =
            new Dictionary<ODFieldName, uint>();
        [NonSerialized] private Dictionary<GObjectID, GObjectID> m_networkOwners;
        [NonSerialized] private Dictionary<GObjectID, GameplayObjectReplicationMode> m_objectReplicationModes;
        [NonSerialized] private Dictionary<GameplayConnectionID, GObjectID> m_playerControllers;
        [NonSerialized] private Dictionary<GObjectID, GameplayConnectionID> m_playerConnections;
        [NonSerialized] private Dictionary<GObjectID, HashSet<GObjectID>> m_objectsByNetworkOwner;
        [NonSerialized] private HashSet<GObjectID> m_automaticPlayerControllers;
        [NonSerialized] private GObjectID m_localPlayerControllerID;
        [NonSerialized] private GameplayRpcContext? m_currentRpcContext;

        public GameplayMachineRole Role { get { return m_role; } }
        public GameplaySynchronizationMode SynchronizationMode { get { return m_synchronizationMode; } }
        public GameplayNetworkState NetworkState { get { return m_networkSession?.State ?? GameplayNetworkState.Disabled; } }
        public bool IsNetworkingEnabled { get { return m_networkSession != null; } }
        public long NetworkRevision { get { return m_networkSession?.Revision ?? 0; } }
        public IReadOnlyList<IODModule> Modules { get { return m_modules; } }
        public GameplayRpcContext? CurrentRpcContext { get { return m_currentRpcContext; } }

        protected virtual void OnPlayerJoin(PlayerController playerController) { }

        protected virtual void OnPlayerDisconnected(PlayerController playerController) { }

        protected virtual void OnPlayerJoin(LockstepPlayerController playerController) { }

        protected virtual void OnPlayerDisconnected(LockstepPlayerController playerController) { }

        internal void NotifyPlayerJoin(PlayerController playerController)
        {
            LogPlayerLifecycle($"Player joined. PlayerController={playerController.ObjectID.ID}");
            RunPlayerLifecycle(() => OnPlayerJoin(playerController));
        }

        internal void NotifyPlayerDisconnected(PlayerController playerController)
        {
            LogPlayerLifecycle($"Player disconnected. PlayerController={playerController.ObjectID.ID}");
            RunPlayerLifecycle(() => OnPlayerDisconnected(playerController));
        }

        internal void NotifyPlayerJoin(LockstepPlayerController playerController)
        {
            LogPlayerLifecycle(
                $"Player joined. PlayerController={playerController.ObjectID.ID}, Slot={playerController.SlotID}");
            RunPlayerLifecycle(() => OnPlayerJoin(playerController));
        }

        internal void NotifyPlayerDisconnected(LockstepPlayerController playerController)
        {
            LogPlayerLifecycle(
                $"Player disconnected. PlayerController={playerController.ObjectID.ID}, Slot={playerController.SlotID}");
            RunPlayerLifecycle(() => OnPlayerDisconnected(playerController));
        }

        private void LogPlayerLifecycle(string message)
        {
            if (Logger is GameplayLogger)
                Logger.Log(XLogger.LogVerbosity.Common, LogCatagories.Network, message);
        }

        private void RunPlayerLifecycle(Action callback)
        {
            m_engineMutationDepth++;
            try
            {
                callback();
            }
            finally
            {
                m_engineMutationDepth--;
            }
        }

        public PlayerController? LocalPlayerController
        {
            get
            {
                return m_synchronizationMode == GameplaySynchronizationMode.StateSync &&
                       m_localPlayerControllerID.IsValid && ContainsGameplayObject(m_localPlayerControllerID)
                    ? CreatePlayerControllerReference(m_localPlayerControllerID)
                    : (PlayerController?)null;
            }
        }

        public IEnumerable<PlayerController> GetPlayerControllers()
        {
            if (m_synchronizationMode != GameplaySynchronizationMode.StateSync)
                throw new InvalidOperationException("Use GetLockstepPlayerControllers in Lockstep mode");
            return m_playerControllers.Values.Concat(new[] { m_localPlayerControllerID })
                .Where(id => id.IsValid && ContainsGameplayObject(id))
                .Distinct()
                .Select(CreatePlayerControllerReference)
                .ToArray();
        }

        public PlayerController GetLocalPlayerController()
        {
            if (m_synchronizationMode != GameplaySynchronizationMode.StateSync)
                throw new InvalidOperationException("Use GetLocalLockstepPlayerController in Lockstep mode");
            PlayerController? result = LocalPlayerController;
            if (!result.HasValue)
                throw new InvalidOperationException("The local PlayerController is not available");
            return result.Value;
        }

        public ulong NetworkSchemaID
        {
            get
            {
                ulong result = ODSaveHash.Compute("GameplayMachine.Network.Schema.2");
                foreach (IODModule module in m_modules.OrderBy(item => item.Name.Name, StringComparer.Ordinal))
                {
                    result = CombineSchema(result, ODSaveHash.Compute(module.Name.Name));
                    IODNetworkModule networkModule = module as IODNetworkModule;
                    if (networkModule != null)
                    {
                        result = CombineSchema(result, networkModule.NetworkSchemaID);
                    }
                    else
                    {
                        foreach (ODClassSaveMeta item in module.GetAllODClassSaveMetas().OrderBy(item => item.StableID))
                        {
                            result = CombineSchema(result, item.StableID);
                        }
                        foreach (ODFieldSaveMeta item in module.GetAllODFieldSaveMetas().OrderBy(item => item.StableID))
                        {
                            result = CombineSchema(result, item.StableID);
                        }
                    }
                }
                return result;
            }
        }

        public ulong ResourceCatalogID
        {
            get
            {
                ulong result = 0;
                foreach (ulong catalogID in m_modules.OfType<IODResourceCatalogNetworkModule>()
                             .Select(module => module.ResourceCatalogID)
                             .Where(id => id != 0)
                             .Distinct()
                             .OrderBy(id => id))
                    result = result == 0 ? catalogID : CombineSchema(result, catalogID);
                return result;
            }
        }

        public IEnumerable<GameplayConnectionID> NetworkConnections
        {
            get
            {
                AuthorityGameplayNetworkSession authority = m_networkSession as AuthorityGameplayNetworkSession;
                return authority == null ? Enumerable.Empty<GameplayConnectionID>() : authority.Connections.ToArray();
            }
        }

        public PlayerController? GetPlayerController(GameplayConnectionID connection)
        {
            if (!m_playerControllers.TryGetValue(connection, out GObjectID objectID) ||
                !ContainsGameplayObject(objectID))
            {
                return null;
            }
            return CreatePlayerControllerReference(objectID);
        }

        public void BindPlayerController(
            GameplayConnectionID connection,
            IGameplayObjectOperator playerController)
        {
            if (m_role != GameplayMachineRole.Authority)
                throw new InvalidOperationException("Only an Authority can bind PlayerControllers to connections");
            if (!(m_networkSession is AuthorityGameplayNetworkSession authority) ||
                !authority.ContainsConnection(connection))
                throw new InvalidOperationException($"Gameplay connection {connection} is not connected");
            CheckValid(playerController);
            BindPlayerControllerCore(connection, playerController.ObjectID, true);
        }

        public void UnbindPlayerController(GameplayConnectionID connection)
        {
            if (m_role != GameplayMachineRole.Authority)
                throw new InvalidOperationException("Only an Authority can unbind PlayerControllers");
            BindPlayerControllerCore(connection, default, true);
        }

        public PlayerController? GetNetworkOwner(IGameplayObjectOperator gameplayObject)
        {
            CheckValid(gameplayObject);
            GObjectID ownerID = GetNetworkOwnerID(gameplayObject.ObjectID);
            return ownerID.IsValid && ContainsGameplayObject(ownerID)
                ? CreatePlayerControllerReference(ownerID)
                : (PlayerController?)null;
        }

        public GObjectID GetNetworkOwnerID(IGameplayObjectOperator gameplayObject)
        {
            CheckValid(gameplayObject);
            return GetNetworkOwnerID(gameplayObject.ObjectID);
        }

        public bool IsOwnedBy(
            IGameplayObjectOperator gameplayObject,
            IGameplayObjectOperator playerController)
        {
            CheckValid(gameplayObject);
            CheckValid(playerController);
            return GetNetworkOwnerID(gameplayObject.ObjectID).Equals(playerController.ObjectID);
        }

        public void SetNetworkOwner(
            IGameplayObjectOperator gameplayObject,
            IGameplayObjectOperator playerController)
        {
            if (m_role != GameplayMachineRole.Authority)
                throw new InvalidOperationException("Only an Authority can assign network ownership");
            CheckDataModifiedOutsideExecution();
            CheckValid(gameplayObject);
            CheckValid(playerController);
            if (!GetGameplayObjectClass(playerController.ObjectID).Equals(PlayerController.ClassName))
                throw new InvalidOperationException("Network owners must be GameplayMachine-managed PlayerControllers");
            SetNetworkOwnerCore(gameplayObject.ObjectID, playerController.ObjectID, true);
            FlushNetworkChangesOutsideExecution();
        }

        public void ClearNetworkOwner(IGameplayObjectOperator gameplayObject)
        {
            if (m_role != GameplayMachineRole.Authority)
                throw new InvalidOperationException("Only an Authority can clear network ownership");
            CheckDataModifiedOutsideExecution();
            CheckValid(gameplayObject);
            SetNetworkOwnerCore(gameplayObject.ObjectID, default, true);
            FlushNetworkChangesOutsideExecution();
        }

        public GameplayObjectReplicationMode GetGameplayObjectReplicationMode(
            IGameplayObjectOperator gameplayObject)
        {
            CheckValid(gameplayObject);
            return GetGameplayObjectReplicationMode(gameplayObject.ObjectID);
        }

        internal GObjectID LocalPlayerControllerID { get { return m_localPlayerControllerID; } }

        internal GObjectID GetPlayerControllerID(GameplayConnectionID connection)
        {
            return m_playerControllers.TryGetValue(connection, out GObjectID objectID) ? objectID : default;
        }

        internal IEnumerable<GObjectID> GetAllGameplayObjectIDs()
        {
            IGameplayObjectSaveManager manager = m_objectManager as IGameplayObjectSaveManager;
            return manager == null
                ? Enumerable.Empty<GObjectID>()
                : manager.GetGameplayObjectsForSave().Select(item => item.ObjectID).ToArray();
        }

        internal void BindPlayerControllerForNetwork(GameplayConnectionID connection, GObjectID objectID)
        {
            BindPlayerControllerCore(connection, objectID, true);
        }

        internal void UnbindPlayerControllerForNetwork(GameplayConnectionID connection)
        {
            BindPlayerControllerCore(connection, default, true);
        }

        internal GObjectID GetNetworkOwnerID(GObjectID objectID)
        {
            return m_networkOwners.TryGetValue(objectID, out GObjectID ownerID) ? ownerID : default;
        }

        internal GameplayObjectReplicationMode GetGameplayObjectReplicationMode(GObjectID objectID)
        {
            return m_objectReplicationModes.TryGetValue(objectID, out GameplayObjectReplicationMode mode)
                ? mode
                : GameplayObjectReplicationMode.AllClients;
        }

        internal bool IsObjectVisibleTo(GObjectID objectID, GObjectID playerControllerID)
        {
            var mode = GetGameplayObjectReplicationMode(objectID);
            return IsPartitionVisibleTo(objectID, playerControllerID) &&
                   IsSpatiallyVisibleTo(objectID, playerControllerID) &&
                   (mode == GameplayObjectReplicationMode.AllClients ||
                    mode == GameplayObjectReplicationMode.OwnerOnly && playerControllerID.IsValid && GetNetworkOwnerID(objectID).Equals(playerControllerID));
        }

        internal bool IsFieldVisibleTo(
            GObjectID objectID,
            ODFieldName fieldName,
            GObjectID playerControllerID)
        {
            ODFieldReplicationMode mode = fieldName.Meta.ReplicationMode;
            return mode == ODFieldReplicationMode.AllClients ||
                   mode == ODFieldReplicationMode.OwnerOnly && playerControllerID.IsValid &&
                   GetNetworkOwnerID(objectID).Equals(playerControllerID);
        }

        internal void InitializeGameplayNetworkMetadata()
        {
            m_networkOwners = new Dictionary<GObjectID, GObjectID>();
            m_objectReplicationModes = new Dictionary<GObjectID, GameplayObjectReplicationMode>();
            m_playerControllers = new Dictionary<GameplayConnectionID, GObjectID>();
            m_playerConnections = new Dictionary<GObjectID, GameplayConnectionID>();
            m_objectsByNetworkOwner = new Dictionary<GObjectID, HashSet<GObjectID>>();
            m_automaticPlayerControllers = new HashSet<GObjectID>();
            m_localPlayerControllerID = default;
            m_currentRpcContext = null;
        }

        internal void InitializeGameplayObjectNetworkMetadata(GObjectID objectID, ODClassName className)
        {
            m_objectReplicationModes[objectID] = className.Meta.ReplicationMode;
        }

        internal void SetGameplayObjectNetworkMetadataForLoad(
            GObjectID objectID,
            GObjectID ownerID,
            GameplayObjectReplicationMode mode)
        {
            GameplayObjectReplicationMode configuredMode = GetGameplayObjectClass(objectID).Meta.ReplicationMode;
            if (mode != configuredMode)
                throw new InvalidDataException(
                    $"GameplayObject {objectID.ID} replication mode does not match its class definition");
            m_objectReplicationModes[objectID] = configuredMode;
            SetNetworkOwnerCore(objectID, ownerID, false);
        }

        internal void ResetGameplayObjectNetworkMetadataForLoad()
        {
            m_networkOwners.Clear();
            m_objectReplicationModes.Clear();
            m_objectsByNetworkOwner.Clear();
        }

        internal void SetLocalPlayerControllerForReplication(GObjectID objectID)
        {
            m_localPlayerControllerID = objectID;
        }

        internal void SetLocalPlayerControllerForLoad(GObjectID objectID)
        {
            ODClassName expectedClass = m_synchronizationMode == GameplaySynchronizationMode.Lockstep
                ? LockstepPlayerController.ClassName
                : PlayerController.ClassName;
            if (objectID.IsValid &&
                (!ContainsGameplayObject(objectID) ||
                 !GetGameplayObjectClass(objectID).Equals(expectedClass)))
                throw new InvalidDataException("Gameplay save local PlayerController is invalid");
            m_localPlayerControllerID = objectID;
        }

        internal void InitializeAuthorityPlayerController()
        {
            if (m_role != GameplayMachineRole.Authority)
                return;
            if (!m_localPlayerControllerID.IsValid)
            {
                ODClassName className = m_synchronizationMode == GameplaySynchronizationMode.Lockstep
                    ? LockstepPlayerController.ClassName
                    : PlayerController.ClassName;
                m_localPlayerControllerID = CreateEngineGameplayObject(className).ObjectID;
            }
            SetNetworkOwnerCore(m_localPlayerControllerID, m_localPlayerControllerID, false);
            if (m_synchronizationMode != GameplaySynchronizationMode.Lockstep)
                NotifyPlayerJoin(CreatePlayerControllerReference(m_localPlayerControllerID));
        }

        internal PlayerController CreateAutomaticPlayerController()
        {
            CommonGameplayObject created = CreateEngineGameplayObject(PlayerController.ClassName);
            m_automaticPlayerControllers.Add(created.ObjectID);
            return CreatePlayerControllerReference(created.ObjectID);
        }

        internal LockstepPlayerController CreateAutomaticLockstepPlayerController()
        {
            CommonGameplayObject created = CreateEngineGameplayObject(LockstepPlayerController.ClassName);
            m_automaticPlayerControllers.Add(created.ObjectID);
            return new LockstepPlayerController { Machine = this, ObjectID = created.ObjectID };
        }

        internal bool IsAutomaticPlayerController(GObjectID objectID)
        {
            return m_automaticPlayerControllers.Contains(objectID);
        }

        internal void ForgetAutomaticPlayerController(GObjectID objectID)
        {
            m_automaticPlayerControllers.Remove(objectID);
        }

        internal GameplayRpcContext? PushRpcContext(GameplayConnectionID connection)
        {
            GameplayRpcContext? previous = m_currentRpcContext;
            m_currentRpcContext = new GameplayRpcContext(connection, GetPlayerController(connection));
            return previous;
        }

        internal GameplayRpcContext? PushPlayerControllerContext(PlayerController playerController)
        {
            if (!ReferenceEquals(playerController.Machine, this) ||
                !ContainsGameplayObject(playerController.ObjectID) ||
                !GetGameplayObjectClass(playerController.ObjectID).Equals(PlayerController.ClassName))
                throw new InvalidOperationException("Debug PlayerController does not exist in this GameplayMachine");

            GameplayRpcContext? previous = m_currentRpcContext;
            m_currentRpcContext = new GameplayRpcContext(default, playerController);
            return previous;
        }

        internal GameplayRpcContext? PushPlayerControllerContext(LockstepPlayerController playerController)
        {
            if (!ReferenceEquals(playerController.Machine, this) ||
                !ContainsGameplayObject(playerController.ObjectID) ||
                !GetGameplayObjectClass(playerController.ObjectID).Equals(LockstepPlayerController.ClassName))
                throw new InvalidOperationException(
                    "Debug LockstepPlayerController does not exist in this GameplayMachine");

            GameplayRpcContext? previous = m_currentRpcContext;
            m_currentRpcContext = new GameplayRpcContext(default, null, playerController);
            return previous;
        }

        internal void RestoreRpcContext(GameplayRpcContext? previous)
        {
            m_currentRpcContext = previous;
        }

        internal bool TryGetExecutingPlayerController(out PlayerController playerController)
        {
            if (m_currentRpcContext?.PlayerController is PlayerController contextualPlayer)
            {
                playerController = contextualPlayer;
                return true;
            }

            if (LocalPlayerController is PlayerController localPlayer)
            {
                playerController = localPlayer;
                return true;
            }

            playerController = default;
            return false;
        }

        internal bool TryGetExecutingLockstepPlayerController(out LockstepPlayerController playerController)
        {
            if (m_currentRpcContext?.LockstepPlayerController is LockstepPlayerController contextualPlayer)
            {
                playerController = contextualPlayer;
                return true;
            }

            if (LocalLockstepPlayerController is LockstepPlayerController localPlayer)
            {
                playerController = localPlayer;
                return true;
            }

            playerController = default;
            return false;
        }

        internal object CreateInitialGameplayFieldValue(ODFieldName fieldName)
        {
            ODFieldMeta meta = fieldName.Meta;
            return meta.InitialValueFactory != null ? meta.InitialValueFactory() : meta.DefaultObject;
        }

        private void BindPlayerControllerCore(
            GameplayConnectionID connection,
            GObjectID playerControllerID,
            bool notifySession)
        {
            GObjectID oldID = m_playerControllers.TryGetValue(connection, out GObjectID existing)
                ? existing
                : default;
            if (oldID.Equals(playerControllerID))
                return;
            if (playerControllerID.IsValid)
            {
                if (!ContainsGameplayObject(playerControllerID))
                    throw new NotExistException();
                if (m_playerConnections.TryGetValue(playerControllerID, out GameplayConnectionID oldConnection) &&
                    !oldConnection.Equals(connection))
                    throw new InvalidOperationException(
                        $"GameplayObject {playerControllerID.ID} is already bound to connection {oldConnection}");
            }

            if (oldID.IsValid)
            {
                m_playerControllers.Remove(connection);
                m_playerConnections.Remove(oldID);
            }
            if (playerControllerID.IsValid)
            {
                m_playerControllers[connection] = playerControllerID;
                m_playerConnections[playerControllerID] = connection;
                SetNetworkOwnerCore(playerControllerID, playerControllerID, notifySession);
            }

            if (notifySession && m_networkSession is AuthorityGameplayNetworkSession authority)
                authority.OnPlayerControllerChanged(connection, oldID, playerControllerID);
            if (notifySession)
                BroadCastMachineEvent(new GameplayPlayerControllerChangedParam
                {
                    Connection = connection,
                    OldPlayerControllerID = oldID,
                    NewPlayerControllerID = playerControllerID,
                });
        }

        private void SetNetworkOwnerCore(GObjectID objectID, GObjectID ownerID, bool notify)
        {
            GObjectID oldOwnerID = GetNetworkOwnerID(objectID);
            if (oldOwnerID.Equals(ownerID))
                return;
            if (notify) MarkPartitionDirty(objectID);
            if (oldOwnerID.IsValid && m_objectsByNetworkOwner.TryGetValue(oldOwnerID, out HashSet<GObjectID> oldObjects))
            {
                oldObjects.Remove(objectID);
                if (oldObjects.Count == 0)
                    m_objectsByNetworkOwner.Remove(oldOwnerID);
            }
            if (ownerID.IsValid)
            {
                m_networkOwners[objectID] = ownerID;
                if (!m_objectsByNetworkOwner.TryGetValue(ownerID, out HashSet<GObjectID> objects))
                {
                    objects = new HashSet<GObjectID>();
                    m_objectsByNetworkOwner.Add(ownerID, objects);
                }
                objects.Add(objectID);
            }
            else
            {
                m_networkOwners.Remove(objectID);
            }

            if (notify)
                m_networkSession?.OnObjectNetworkMetadataChanged(objectID, oldOwnerID);
            if (notify)
                RecordSpatialOwnershipChanged(objectID, oldOwnerID, ownerID);
            if (notify)
                BroadCastMachineEvent(new GameplayObjectNetworkOwnerChangedParam
                {
                    ObjectID = objectID,
                    OldOwnerID = oldOwnerID,
                    NewOwnerID = ownerID,
                });
        }

        private void RemoveGameplayObjectNetworkMetadata(GObjectID objectID)
        {
            if (m_objectsByNetworkOwner.TryGetValue(objectID, out HashSet<GObjectID> ownedObjects))
            {
                foreach (GObjectID ownedObject in ownedObjects.ToArray())
                    if (ContainsGameplayObject(ownedObject))
                        SetNetworkOwnerCore(ownedObject, default, true);
            }
            if (m_playerConnections.TryGetValue(objectID, out GameplayConnectionID connection))
                BindPlayerControllerCore(connection, default, true);

            GObjectID ownerID = GetNetworkOwnerID(objectID);
            if (ownerID.IsValid && m_objectsByNetworkOwner.TryGetValue(ownerID, out HashSet<GObjectID> ownerObjects))
            {
                ownerObjects.Remove(objectID);
                if (ownerObjects.Count == 0)
                    m_objectsByNetworkOwner.Remove(ownerID);
            }
            m_networkOwners.Remove(objectID);
            m_objectReplicationModes.Remove(objectID);
            ForgetGameplaySpatialMetadata(objectID);
        }

        private CommonGameplayObject CreateCommonGameplayObjectReference(GObjectID objectID)
        {
            return new CommonGameplayObject { Machine = this, ObjectID = objectID };
        }

        private PlayerController CreatePlayerControllerReference(GObjectID objectID)
        {
            return new PlayerController { Machine = this, ObjectID = objectID };
        }

        internal void ConfigureRuntime(GameplayMachineRole role, IEnumerable<IODModule> modules)
        {
            m_role = role;
            m_modules = modules?.Where(item => item != null).ToArray() ?? Array.Empty<IODModule>();
            ConfigureGameplayInterestModules();
            m_networkFieldMetas = m_modules
                .SelectMany(item => item.GetAllODFieldSaveMetas() ?? Enumerable.Empty<ODFieldSaveMeta>())
                .Where(item => GameplayMachineBase.GetODFieldMeta(item.FieldName).ReplicationMode !=
                    ODFieldReplicationMode.None)
                .OrderBy(item => item.StableID)
                .ToArray();
            m_networkFieldIndices = new Dictionary<ODFieldName, uint>(m_networkFieldMetas.Length);
            for (int index = 0; index < m_networkFieldMetas.Length; index++)
            {
                m_networkFieldIndices.Add(m_networkFieldMetas[index].FieldName, checked((uint)index));
            }
        }

        internal uint GetNetworkFieldIndex(ODFieldName fieldName)
        {
            if (m_networkFieldIndices.TryGetValue(fieldName, out uint index))
            {
                return index;
            }
            throw new InvalidOperationException($"Field {fieldName} is not part of this GameplayMachine network schema");
        }

        internal ODFieldSaveMeta GetNetworkFieldMeta(uint index)
        {
            if (index >= m_networkFieldMetas.Length)
            {
                throw new InvalidDataException($"Gameplay replication contains unknown field index {index}");
            }
            return m_networkFieldMetas[index];
        }

        private GObjectID AllocateClientLocalObjectID()
        {
            if (m_clientLocalIDAllocator.ID == int.MinValue)
            {
                throw new InvalidOperationException("Client-local GameplayObject ID space is exhausted");
            }
            m_clientLocalIDAllocator.ID--;
            return m_clientLocalIDAllocator;
        }

        public void EnableNetworking(IGameplayNetworkTransport transport, GameplayNetworkOptions options = null)
        {
            if (m_synchronizationMode != GameplaySynchronizationMode.StateSync)
                throw new InvalidOperationException("EnableNetworking is only available in StateSync mode");
            if (m_role != GameplayMachineRole.Authority)
            {
                throw new InvalidOperationException("ClientProxy GameplayMachines cannot host network sessions");
            }
            if (m_networkSession != null)
            {
                throw new InvalidOperationException("GameplayMachine networking is already enabled");
            }
            EnsureReplicationManager();
            GameplayNetworkSession session = new AuthorityGameplayNetworkSession(this, transport, options);
            StartNetworkSession(session);
        }

        public void ConnectNetworking(IGameplayNetworkTransport transport, GameplayNetworkOptions options = null)
        {
            if (m_synchronizationMode != GameplaySynchronizationMode.StateSync)
                throw new InvalidOperationException("ConnectNetworking is only available in StateSync mode");
            if (m_role != GameplayMachineRole.ClientProxy)
            {
                throw new InvalidOperationException("Only ClientProxy GameplayMachines connect to an Authority");
            }
            if (m_networkSession != null)
            {
                throw new InvalidOperationException("GameplayMachine networking is already enabled");
            }
            EnsureReplicationManager();
            GameplayNetworkSession session = new ClientGameplayNetworkSession(this, transport, options);
            StartNetworkSession(session);
        }

        public void DisableNetworking()
        {
            GameplayNetworkSession session = m_networkSession;
            m_networkSession = null;
            session?.Dispose();
        }

        public void UpdateNetwork()
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            double elapsedMilliseconds = m_lastUpdateTimestamp == 0
                ? 0d
                : (now - m_lastUpdateTimestamp) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            m_lastUpdateTimestamp = now;
            Update(elapsedMilliseconds / 1000d);
        }

        /// <summary>
        /// Advances the machine from a game engine update. Hosts with their own clock should call
        /// this overload so fixed ticks, networking, and the debug server share one update pump.
        /// </summary>
        public void Update(double deltaSeconds)
        {
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));

            if (m_synchronizationMode == GameplaySynchronizationMode.StateSync &&
                m_role == GameplayMachineRole.Authority &&
                m_tickMode == GameplayTickMode.FixedRate)
            {
                m_tickAccumulatorMilliseconds += deltaSeconds * 1000d;
                int catchUpTicks = 0;
                const int maximumCatchUpTicksPerUpdate = 8;
                while (m_tickAccumulatorMilliseconds >= m_tickIntervalMilliseconds &&
                       catchUpTicks++ < maximumCatchUpTicksPerUpdate)
                {
                    m_tickAccumulatorMilliseconds -= m_tickIntervalMilliseconds;
                    RunGameplayTick();
                }
                if (catchUpTicks > maximumCatchUpTicksPerUpdate)
                    m_tickAccumulatorMilliseconds = Math.Min(
                        m_tickAccumulatorMilliseconds, m_tickIntervalMilliseconds);
            }

            if (m_synchronizationMode == GameplaySynchronizationMode.StateSync)
                ReconcileGameplayInterest();
            m_networkSession?.Update();
            // Existing hosts already pump this method every frame. Keep debug commands on the same
            // gameplay thread so enabling the debugger does not require a second runner integration.
            UpdateDebug();
        }

        private ODCore.EventError CanSendAuthorityInterface(IReplicatedInterface param)
        {
            ClientGameplayNetworkSession client = m_networkSession as ClientGameplayNetworkSession;
            return client == null
                ? (ODCore.EventError)"ClientProxy is not connected to an Authority"
                : client.CanSendAuthorityInterface(param);
        }

        private ODCore.EventError SendAuthorityInterface(IReplicatedInterface param)
        {
            ClientGameplayNetworkSession client = m_networkSession as ClientGameplayNetworkSession;
            return client == null
                ? (ODCore.EventError)"ClientProxy is not connected to an Authority"
                : client.SendAuthorityInterface(param);
        }

        private ODCore.EventError CanSendPredictableInterface(IPredictableInterface param)
        {
            ClientGameplayNetworkSession client = m_networkSession as ClientGameplayNetworkSession;
            return client == null
                ? (ODCore.EventError)"ClientProxy is not connected to an Authority"
                : client.CanSendPredictableInterface(param);
        }

        private ODCore.EventError SendPredictableInterface(IPredictableInterface param)
        {
            ClientGameplayNetworkSession client = m_networkSession as ClientGameplayNetworkSession;
            return client == null
                ? (ODCore.EventError)"ClientProxy is not connected to an Authority"
                : client.SendPredictableInterface(param);
        }

        private ODCore.EventError SnapshotPredictableInterface(
            IPredictableInterface param,
            out IPredictableInterface snapshot)
        {
            ClientGameplayNetworkSession client = m_networkSession as ClientGameplayNetworkSession;
            if (client == null)
            {
                snapshot = null;
                return "ClientProxy is not connected to an Authority";
            }
            return client.SnapshotPredictableInterface(param, out snapshot);
        }

        private void BroadcastMulticastInterface(IReplicatedInterface param)
        {
            AuthorityGameplayNetworkSession authority = m_networkSession as AuthorityGameplayNetworkSession;
            authority?.BroadcastMulticastInterface(param);
        }

        private void BroadcastMulticastEvent(IMulticastEvent multicastEvent)
        {
            AuthorityGameplayNetworkSession authority =
                m_networkSession as AuthorityGameplayNetworkSession;
            authority?.BroadcastMulticastEvent(multicastEvent);
        }

        internal void RecordNetworkObjectLife(GObjectID objectID, bool created)
        {
            if (m_applyingReplication)
            {
                return;
            }
            m_networkSession?.OnObjectLifeChanged(objectID, created);
            FlushNetworkChangesOutsideExecution();
        }

        internal void RecordNetworkFieldChange(
            GObjectID objectID,
            ODFieldName fieldName,
            ObjectEventTypes eventType,
            object context,
            object newValue,
            object oldValue)
        {
            if (m_applyingReplication)
            {
                return;
            }
            m_networkSession?.OnFieldChanged(objectID, fieldName, eventType, context, newValue, oldValue);
            FlushNetworkChangesOutsideExecution();
        }

        internal void RecordNetworkFieldChanges(
            GObjectID objectID,
            ODFieldName fieldName,
            IEnumerable addedValues,
            IEnumerable removedValues,
            IEnumerable changedValues)
        {
            if (m_applyingReplication)
                return;
            if (addedValues != null)
                foreach (object item in addedValues)
                    m_networkSession?.OnFieldChanged(objectID, fieldName, ObjectEventTypes.Add, item, null, null);
            if (removedValues != null)
                foreach (object item in removedValues)
                    m_networkSession?.OnFieldChanged(objectID, fieldName, ObjectEventTypes.Remove, item, null, null);
            if (changedValues != null)
                foreach (object item in changedValues)
                    m_networkSession?.OnFieldChanged(objectID, fieldName, ObjectEventTypes.ItemChanged, item, null, null);
            FlushNetworkChangesOutsideExecution();
        }

        internal void RecordNetworkRootChange()
        {
            if (m_applyingReplication)
            {
                return;
            }
            m_networkSession?.OnRootChanged();
            FlushNetworkChangesOutsideExecution();
        }

        internal void FlushNetworkChanges()
        {
            m_networkSession?.FlushChanges();
        }

        internal void BeginApplyingReplication() { m_applyingReplication = true; }

        internal void EndApplyingReplication()
        {
            m_applyingReplication = false;
            if (m_useDelayedCallback)
                m_delayedEvents.FlushAllEvents();
        }

        internal void ApplyReplicationData(
            IGameplayObjectReplicationManager manager,
            IEnumerable<GObjectID> deleted,
            IEnumerable<GameplayReplicationObjectData> objects,
            bool rootChanged,
            GObjectID rootObjectID,
            ISet<GObjectID> unavailable = null)
        {
            List<GObjectID> deletedItems = deleted.ToList();
            List<GameplayReplicationObjectData> objectItems = objects.ToList();
            List<GObjectID> created = new List<GObjectID>();
            List<GObjectID> removed = new List<GObjectID>();
            var resolutionFields = CaptureResolutionFields(deletedItems.Concat(objectItems.Where(o => o.Created).Select(o => o.ObjectID)));
            Dictionary<GObjectID, List<GameplayReplicationDeltaEvent>> changedFields =
                new Dictionary<GObjectID, List<GameplayReplicationDeltaEvent>>();

            foreach (GameplayReplicationObjectData item in objectItems)
            {
                IGameplayObject existing;
                bool exists = m_objectManager.TryGetGameplayObject(item.ObjectID, out existing);
                if (item.Created)
                {
                    if (exists)
                    {
                        throw new InvalidDataException($"Replicated GameplayObject {item.ObjectID.ID} was created twice");
                    }
                    IGameplayObject gameplayObject = m_objectManager.CreateGameplayObject(
                        item.ClassMeta.ClassName,
                        item.ObjectID);
                    InitializeGameplayObjectFields(gameplayObject);
                    InitializeGameplayObjectNetworkMetadata(item.ObjectID, item.ClassMeta.ClassName);
                    manager.EnsureIDAllocatorForReplication(item.ObjectID);
                    created.Add(item.ObjectID);
                }
                else if (!exists)
                {
                    throw new InvalidDataException($"Replicated GameplayObject {item.ObjectID.ID} does not exist");
                }

                ODClassName className = item.Created ? item.ClassMeta.ClassName : existing.ObjectClass;
                if (item.MetadataChanged)
                {
                    SetGameplayObjectNetworkMetadataForLoad(item.ObjectID, item.OwnerID, item.ReplicationMode);
                    RegisterGameplayObjectPartition(item.ObjectID, item.PartitionID);
                }
                List<GameplayReplicationDeltaEvent> fields = new List<GameplayReplicationDeltaEvent>();
                foreach (GameplayReplicationFieldData field in item.Fields)
                {
                    ODFieldMeta fieldMeta = GameplayMachineBase.GetODFieldMeta(field.Meta.FieldName);
                    if (fieldMeta.ReplicationMode == ODFieldReplicationMode.None ||
                        !GameplayMachineBase.CanCastTo(className, fieldMeta.FromClass))
                    {
                        throw new InvalidDataException(
                            $"Replicated field {field.Meta.StableName} is invalid for object {item.ObjectID.ID}");
                    }
                }
                changedFields.Add(item.ObjectID, fields);
                foreach (GameplayReplicationFieldData field in item.Fields)
                {
                    if (!field.IsDelta)
                    {
                        object value = field.Value;
                        if (field.PatchMask != 0)
                        {
                            if (item.Created)
                                throw new InvalidDataException("A newly replicated object cannot contain field patches");
                            object current = manager.GetStoredFieldsForSave(item.ObjectID)
                                .FirstOrDefault(pair => pair.Key.Equals(field.Meta.FieldName)).Value;
                            if (current == null)
                                current = CreateInitialGameplayFieldValue(field.Meta.FieldName);
                            value = GameplayReplicationFormat.MergePatch(
                                field.Meta, current, field.Value, field.PatchMask);
                        }
                        manager.SetStoredFieldForLoad(item.ObjectID, field.Meta.FieldName, value);
                        fields.Add(new GameplayReplicationDeltaEvent
                        {
                            Field = field.Meta.FieldName,
                            EventType = ObjectEventTypes.Changed,
                        });
                        continue;
                    }
                    if (item.Created)
                        throw new InvalidDataException("A newly replicated object cannot contain collection deltas");
                    object storedCollection = manager.GetStoredFieldsForSave(item.ObjectID)
                        .FirstOrDefault(pair => pair.Key.Equals(field.Meta.FieldName)).Value;
                    if (!(storedCollection is IReplicatedCollection collection))
                        throw new InvalidDataException($"Field {field.Meta.StableName} has no replicated collection storage");
                    foreach (GameplayReplicationDelta delta in field.Deltas)
                    {
                        object oldValue = delta.EventType == ObjectEventTypes.ItemChanged
                            ? collection.Get(delta.Key)
                            : null;
                        switch (delta.EventType)
                        {
                            case ObjectEventTypes.Add:
                                collection.ApplyReplicationAdd(delta.Key, delta.Value);
                                break;
                            case ObjectEventTypes.Remove:
                                collection.ApplyReplicationRemove(delta.Key);
                                break;
                            case ObjectEventTypes.ItemChanged:
                                collection.ApplyReplicationItemChanged(delta.Key, delta.Value);
                                break;
                            default:
                                throw new InvalidDataException("Unsupported replicated collection delta");
                        }
                        fields.Add(new GameplayReplicationDeltaEvent
                        {
                            Field = field.Meta.FieldName,
                            EventType = delta.EventType,
                            Key = delta.Key,
                            Value = delta.Value,
                            OldValue = oldValue,
                        });
                    }
                }
            }

            // Apply reference-clearing field deltas while their targets still exist, then remove the
            // targets before callbacks run so generated getters already observe null.
            foreach (GObjectID objectID in deletedItems)
            {
                if (m_objectManager.ContainsGameplayObject(objectID))
                {
                    m_referenceIndex.RemoveOwner(objectID);
                    m_eventSystemManager.ClearEvent(objectID);
                    m_objectManager.RemoveGameplayObject(objectID);
                    RemoveGameplayObjectNetworkMetadata(objectID);
                    if (unavailable == null || !unavailable.Contains(objectID)) ForgetGameplayObjectPartition(objectID);
                    removed.Add(objectID);
                }
            }

            if (rootChanged)
            {
                SetRootForLoad(rootObjectID);
            }

            foreach (GObjectID objectID in created)
            {
                BroadCastObjectLifeEvent(objectID, true);
            }
            foreach (KeyValuePair<GObjectID, List<GameplayReplicationDeltaEvent>> item in changedFields)
            {
                foreach (GameplayReplicationDeltaEvent field in item.Value)
                {
                    NotifyFieldChangedEvent(item.Key, field.Field, field.EventType, field.Key,
                        field.Value, field.OldValue, disableEqualTest: true);
                }
            }
            foreach (GObjectID objectID in removed)
            {
                if (unavailable != null && unavailable.Contains(objectID)) NotifyAvailability(objectID, false);
                else BroadCastObjectLifeEvent(objectID, false);
            }
            resolutionFields.ExceptWith(changedFields.SelectMany(pair => pair.Value.Select(field => new GameplayReferenceField(pair.Key, field.Field))));
            NotifyResolutionFields(resolutionFields);
        }

        private sealed class GameplayReplicationDeltaEvent
        {
            public ODFieldName Field;
            public ObjectEventTypes EventType;
            public object Key;
            public object Value;
            public object OldValue;
        }

        private void FlushNetworkChangesOutsideExecution()
        {
            if (!DuringExecution && m_partitionMutationDepth == 0)
            {
                m_networkSession?.FlushChanges();
            }
        }

        private void EnsureReplicationManager()
        {
            if (!(m_objectManager is IGameplayObjectReplicationManager))
            {
                throw new NotSupportedException($"{m_objectManager.GetType()} does not support gameplay replication");
            }
        }

        private void StartNetworkSession(GameplayNetworkSession session)
        {
            m_networkSession = session;
            try
            {
                session.Start();
            }
            catch
            {
                m_networkSession = null;
                session.Dispose();
                throw;
            }
        }

        private static ulong CombineSchema(ulong current, ulong value)
        {
            unchecked
            {
                return (current ^ value) * 1099511628211UL;
            }
        }
    }
}
