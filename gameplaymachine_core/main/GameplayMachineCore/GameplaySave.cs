using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GMCore
{
    public enum GameplaySerializationPurpose
    {
        Save,
        Network,
    }

    [Serializable]
    public struct ODResourceID : IEquatable<ODResourceID>
    {
        public Guid Value;

        public bool IsValid { get { return Value != Guid.Empty; } }

        public ODResourceID(Guid value) { Value = value; }
        public bool Equals(ODResourceID other) { return Value.Equals(other.Value); }
        public override bool Equals(object obj) { return obj is ODResourceID other && Equals(other); }
        public override int GetHashCode() { return Value.GetHashCode(); }
        public override string ToString() { return Value.ToString("N"); }
        public byte[] ToByteArray()
        {
            byte[] value = Value.ToByteArray();
            return new[]
            {
                value[3], value[2], value[1], value[0], value[5], value[4], value[7], value[6],
                value[8], value[9], value[10], value[11], value[12], value[13], value[14], value[15],
            };
        }
        public static ODResourceID FromByteArray(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 16)
                throw new ArgumentException("An OD resource ID must contain exactly 16 bytes", nameof(bytes));
            byte[] value =
            {
                bytes[3], bytes[2], bytes[1], bytes[0], bytes[5], bytes[4], bytes[7], bytes[6],
                bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15],
            };
            return new ODResourceID(new Guid(value));
        }
        public static bool TryParse(string value, out ODResourceID result)
        {
            Guid parsed;
            bool succeeded = Guid.TryParseExact(value, "N", out parsed);
            result = succeeded ? new ODResourceID(parsed) : default;
            return succeeded;
        }
        public static bool operator ==(ODResourceID left, ODResourceID right) { return left.Equals(right); }
        public static bool operator !=(ODResourceID left, ODResourceID right) { return !left.Equals(right); }
    }

    public interface IGMSerializer
    {
        IEnumerable<Type> GetSerializeTypes();
        void Write(GameplaySaveWriter writer, object value);
        object Read(GameplaySaveReader reader, Type type);
    }

    public delegate void ODSaveValueWriter(GameplaySaveWriter writer, object value);
    public delegate object ODSaveValueReader(GameplaySaveReader reader);

    public enum ODCollectionDeltaKind : byte
    {
        None,
        Set,
        Map,
    }

    public enum ODFieldPatchKind : byte
    {
        None,
        Transform,
    }

    public struct ODClassSaveMeta
    {
        public ulong StableID;
        public string StableName;
        public ODClassName ClassName;
    }

    public struct ODFieldSaveMeta
    {
        public ulong StableID;
        public string StableName;
        public ODFieldName FieldName;
        public ODSaveValueWriter Writer;
        public ODSaveValueReader Reader;
        public ODFieldPatchKind PatchKind;
        public ODCollectionDeltaKind DeltaKind;
        public ODSaveValueWriter DeltaKeyWriter;
        public ODSaveValueReader DeltaKeyReader;
        public ODSaveValueWriter DeltaValueWriter;
        public ODSaveValueReader DeltaValueReader;
    }

    public static class ODSaveHash
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong Compute(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            ulong result = Offset;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            for (int i = 0; i < bytes.Length; i++)
            {
                result ^= bytes[i];
                result *= Prime;
            }
            return result;
        }
    }

    internal sealed class GameplaySaveCustomCodecs
    {
        private readonly Dictionary<Type, IGMSerializer> m_codecs = new Dictionary<Type, IGMSerializer>();

        public GameplaySaveCustomCodecs(IEnumerable<IGMSerializer> serializers)
        {
            if (serializers == null)
            {
                return;
            }

            foreach (IGMSerializer serializer in serializers)
            {
                if (serializer == null)
                {
                    continue;
                }

                IEnumerable<Type> types = serializer.GetSerializeTypes()
                    ?? throw new InvalidOperationException($"{serializer.GetType()} returned null serialize types");
                foreach (Type type in types)
                {
                    if (type == null)
                    {
                        throw new InvalidOperationException($"{serializer.GetType()} returned a null serialize type");
                    }
                    if (m_codecs.ContainsKey(type))
                    {
                        throw new InvalidOperationException($"A save codec for {type} is already registered");
                    }
                    m_codecs.Add(type, serializer);
                }
            }
        }

        public IGMSerializer Get(Type type)
        {
            IGMSerializer result;
            if (m_codecs.TryGetValue(type, out result))
            {
                return result;
            }
            throw new InvalidOperationException($"No custom save codec is registered for {type}");
        }
    }

    public sealed class GameplaySaveWriter : IDisposable
    {
        private readonly MemoryStream m_stream;
        private readonly BinaryWriter m_writer;
        private readonly GameplaySaveCustomCodecs m_customCodecs;
        private bool m_finished;

        public GameplayMachine Machine { get; internal set; }
        public GameplaySerializationPurpose Purpose { get; }
        public bool IsNetwork { get { return Purpose == GameplaySerializationPurpose.Network; } }

        internal long Position
        {
            get { return m_stream.Position; }
            set { m_stream.Position = value; }
        }

        internal GameplaySaveWriter(IEnumerable<IGMSerializer> serializers,
            GameplaySerializationPurpose purpose = GameplaySerializationPurpose.Save)
        {
            m_stream = new MemoryStream();
            m_writer = new BinaryWriter(m_stream, Encoding.UTF8, true);
            m_customCodecs = new GameplaySaveCustomCodecs(serializers);
            Purpose = purpose;
        }

        public void WriteBoolean(bool value) { m_writer.Write(value); }
        public void WriteByte(byte value) { m_writer.Write(value); }
        public void WriteSByte(sbyte value) { m_writer.Write(value); }
        public void WriteInt16(short value) { m_writer.Write(value); }
        public void WriteUInt16(ushort value) { m_writer.Write(value); }
        public void WriteInt32(int value) { m_writer.Write(value); }
        public void WriteUInt32(uint value) { m_writer.Write(value); }
        public void WriteInt64(long value) { m_writer.Write(value); }
        public void WriteUInt64(ulong value) { m_writer.Write(value); }
        public void WriteSingle(float value) { m_writer.Write(value); }
        public void WriteDouble(double value) { m_writer.Write(value); }
        public void WriteDecimal(decimal value) { m_writer.Write(value); }
        public void WriteChar(char value) { WriteUInt16(value); }

        public void WriteString(string value)
        {
            WriteBoolean(value != null);
            if (value != null)
            {
                m_writer.Write(value);
            }
        }

        public void WriteObjectID(GObjectID value)
        {
            if (value.ID < 0)
            {
                throw new InvalidOperationException(
                    $"Client-local GameplayObject ID {value.ID} cannot cross a save or network boundary");
            }
            WriteVarUInt32((uint)value.ID);
        }

        public void WriteResourceID(ODResourceID value)
        {
            m_writer.Write(value.ToByteArray());
        }

        public void WriteCount(int value)
        {
            if (value < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            WriteVarUInt32((uint)(value + 1));
        }

        public void WriteVarUInt32(uint value)
        {
            while (value >= 0x80)
            {
                WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            WriteByte((byte)value);
        }

        public void WriteVarInt32(int value)
        {
            WriteVarUInt32(unchecked((uint)((value << 1) ^ (value >> 31))));
        }

        public void WriteVarUInt64(ulong value)
        {
            while (value >= 0x80)
            {
                WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            WriteByte((byte)value);
        }

        public void WriteVarInt64(long value)
        {
            WriteVarUInt64(unchecked((ulong)((value << 1) ^ (value >> 63))));
        }

        public void WriteCustom(Type type, object value)
        {
            WriteBoolean(value != null);
            if (value != null)
            {
                m_customCodecs.Get(type).Write(this, value);
            }
        }

        public void WriteCustom<T>(T value)
        {
            WriteCustom(typeof(T), value);
        }

        internal long BeginPayload()
        {
            long lengthPosition = Position;
            WriteInt32(0);
            return lengthPosition;
        }

        internal void EndPayload(long lengthPosition)
        {
            long endPosition = Position;
            long payloadLength = endPosition - lengthPosition - sizeof(int);
            if (payloadLength > int.MaxValue)
            {
                throw new InvalidOperationException("A save field payload is too large");
            }

            Position = lengthPosition;
            WriteInt32((int)payloadLength);
            Position = endPosition;
        }

        internal byte[] Finish()
        {
            if (m_finished)
            {
                throw new InvalidOperationException("Save writer has already been finished");
            }
            m_finished = true;
            m_writer.Flush();

            byte[] payload = m_stream.ToArray();
            uint checksum = GameplaySaveChecksum.Compute(payload, 0, payload.Length);
            byte[] result = new byte[payload.Length + sizeof(uint)];
            Buffer.BlockCopy(payload, 0, result, 0, payload.Length);
            result[payload.Length] = (byte)checksum;
            result[payload.Length + 1] = (byte)(checksum >> 8);
            result[payload.Length + 2] = (byte)(checksum >> 16);
            result[payload.Length + 3] = (byte)(checksum >> 24);
            return result;
        }

        public void Dispose()
        {
            m_writer.Dispose();
            m_stream.Dispose();
        }
    }

    public sealed class GameplaySaveReader : IDisposable
    {
        private readonly MemoryStream m_stream;
        private readonly BinaryReader m_reader;
        private readonly GameplaySaveCustomCodecs m_customCodecs;

        public GameplayMachine Machine { get; internal set; }
        public GameplaySerializationPurpose Purpose { get; }
        public bool IsNetwork { get { return Purpose == GameplaySerializationPurpose.Network; } }

        internal long Position
        {
            get { return m_stream.Position; }
            set { m_stream.Position = value; }
        }

        internal long Length { get { return m_stream.Length; } }

        internal GameplaySaveReader(byte[] bytes, IEnumerable<IGMSerializer> serializers,
            GameplaySerializationPurpose purpose = GameplaySerializationPurpose.Save)
        {
            if (bytes == null || bytes.Length < sizeof(uint))
            {
                throw new InvalidDataException("Gameplay save is empty or truncated");
            }

            int payloadLength = bytes.Length - sizeof(uint);
            uint expected = (uint)(bytes[payloadLength]
                | (bytes[payloadLength + 1] << 8)
                | (bytes[payloadLength + 2] << 16)
                | (bytes[payloadLength + 3] << 24));
            uint actual = GameplaySaveChecksum.Compute(bytes, 0, payloadLength);
            if (actual != expected)
            {
                throw new InvalidDataException("Gameplay save checksum does not match");
            }

            m_stream = new MemoryStream(bytes, 0, payloadLength, false, true);
            m_reader = new BinaryReader(m_stream, Encoding.UTF8, true);
            m_customCodecs = new GameplaySaveCustomCodecs(serializers);
            Purpose = purpose;
        }

        public bool ReadBoolean() { return m_reader.ReadBoolean(); }
        public byte ReadByte() { return m_reader.ReadByte(); }
        public sbyte ReadSByte() { return m_reader.ReadSByte(); }
        public short ReadInt16() { return m_reader.ReadInt16(); }
        public ushort ReadUInt16() { return m_reader.ReadUInt16(); }
        public int ReadInt32() { return m_reader.ReadInt32(); }
        public uint ReadUInt32() { return m_reader.ReadUInt32(); }
        public long ReadInt64() { return m_reader.ReadInt64(); }
        public ulong ReadUInt64() { return m_reader.ReadUInt64(); }
        public float ReadSingle() { return m_reader.ReadSingle(); }
        public double ReadDouble() { return m_reader.ReadDouble(); }
        public decimal ReadDecimal() { return m_reader.ReadDecimal(); }
        public char ReadChar() { return (char)ReadUInt16(); }

        public string ReadString()
        {
            return ReadBoolean() ? m_reader.ReadString() : null;
        }

        public GObjectID ReadObjectID()
        {
            uint value = ReadVarUInt32();
            if (value > int.MaxValue)
            {
                throw new InvalidDataException("Gameplay save contains an invalid object ID");
            }
            return new GObjectID { ID = (int)value };
        }

        public ODResourceID ReadResourceID()
        {
            byte[] bytes = m_reader.ReadBytes(16);
            if (bytes.Length != 16)
                throw new EndOfStreamException();
            return ODResourceID.FromByteArray(bytes);
        }

        public int ReadCount()
        {
            uint encoded = ReadVarUInt32();
            if (encoded > int.MaxValue + 1UL)
            {
                throw new InvalidDataException("Gameplay save collection count is invalid");
            }
            return (int)encoded - 1;
        }

        public uint ReadVarUInt32()
        {
            uint result = 0;
            int shift = 0;
            while (shift < 35)
            {
                byte value = ReadByte();
                if (shift == 28 && (value & 0xF0) != 0)
                {
                    throw new InvalidDataException("Gameplay save contains an invalid variable-length integer");
                }
                result |= (uint)(value & 0x7F) << shift;
                if ((value & 0x80) == 0)
                {
                    return result;
                }
                shift += 7;
            }
            throw new InvalidDataException("Gameplay save contains an invalid variable-length integer");
        }

        public int ReadVarInt32()
        {
            uint value = ReadVarUInt32();
            return unchecked((int)((value >> 1) ^ (uint)-(int)(value & 1)));
        }

        public ulong ReadVarUInt64()
        {
            ulong result = 0;
            int shift = 0;
            while (shift < 70)
            {
                byte value = ReadByte();
                if (shift == 63 && (value & 0xFE) != 0)
                {
                    throw new InvalidDataException("Gameplay save contains an invalid variable-length integer");
                }
                result |= (ulong)(value & 0x7F) << shift;
                if ((value & 0x80) == 0)
                {
                    return result;
                }
                shift += 7;
            }
            throw new InvalidDataException("Gameplay save contains an invalid variable-length integer");
        }

        public long ReadVarInt64()
        {
            ulong value = ReadVarUInt64();
            return unchecked((long)((value >> 1) ^ (ulong)-(long)(value & 1)));
        }

        public object ReadCustom(Type type)
        {
            return ReadBoolean() ? m_customCodecs.Get(type).Read(this, type) : null;
        }

        public T ReadCustom<T>()
        {
            object value = ReadCustom(typeof(T));
            return value == null ? default : (T)value;
        }

        internal void Skip(int byteCount)
        {
            if (byteCount < 0 || Position + byteCount > Length)
            {
                throw new InvalidDataException("Gameplay save field payload is invalid");
            }
            Position += byteCount;
        }

        internal void EnsureAt(long expectedPosition)
        {
            if (Position != expectedPosition)
            {
                throw new InvalidDataException(
                    $"Gameplay save field codec consumed an unexpected number of bytes (position {Position}, expected {expectedPosition})");
            }
        }

        public void Dispose()
        {
            m_reader.Dispose();
            m_stream.Dispose();
        }
    }

    internal static class GameplaySaveChecksum
    {
        private static readonly uint[] s_table = CreateTable();

        private static uint[] CreateTable()
        {
            uint[] result = new uint[256];
            for (uint i = 0; i < result.Length; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) != 0 ? 0xEDB88320U ^ (value >> 1) : value >> 1;
                }
                result[i] = value;
            }
            return result;
        }

        public static uint Compute(byte[] bytes, int offset, int count)
        {
            uint result = uint.MaxValue;
            int end = checked(offset + count);
            for (int i = offset; i < end; i++)
            {
                result = s_table[(result ^ bytes[i]) & 0xFF] ^ (result >> 8);
            }
            return ~result;
        }
    }

    internal static class GameplayMachineSaveFormat
    {
        private const uint Magic = 0x32534D47U;
        private const ushort FormatVersion = 9;

        public static byte[] Save(
            GameplayMachine machine,
            IGameplayObjectManager objectManager,
            IEnumerable<IGMSerializer> serializers,
            Func<GObjectID, bool> includeObject = null,
            Func<GObjectID, ODFieldName, bool> includeField = null,
            GameplaySerializationPurpose purpose = GameplaySerializationPurpose.Save,
            GObjectID? rootObjectIDOverride = null,
            bool includePartitions = true,
            bool embedPartitionPayloads = true,
            GObjectID recipient = default)
        {
            IGameplayObjectSaveManager saveManager = objectManager as IGameplayObjectSaveManager;
            if (saveManager == null)
            {
                throw new NotSupportedException($"{objectManager.GetType()} does not support gameplay snapshots");
            }

            using (GameplaySaveWriter writer = new GameplaySaveWriter(serializers, purpose))
            {
                writer.Machine = machine;
                writer.WriteUInt32(Magic);
                writer.WriteUInt16(FormatVersion);
                writer.WriteUInt16(0);
                writer.WriteObjectID(saveManager.IDAllocator);
                writer.WriteObjectID(rootObjectIDOverride ?? machine.RootObjectID);
                writer.WriteObjectID(purpose == GameplaySerializationPurpose.Save
                    ? machine.LocalPlayerControllerID
                    : default);

                List<IGameplayObject> objects = saveManager.GetGameplayObjectsForSave()
                    .Where(item => purpose == GameplaySerializationPurpose.Network || !includePartitions || machine.GetPartition(machine.GetGameplayObjectPartition(item.ObjectID)).Kind != PartitionKind.Transient)
                    .Where(item => includeObject == null || includeObject(item.ObjectID))
                    .OrderBy(item => item.ObjectID.ID)
                    .ToList();
                writer.WriteCount(objects.Count);
                foreach (IGameplayObject gameplayObject in objects)
                {
                    writer.WriteObjectID(gameplayObject.ObjectID);
                    writer.WriteUInt64(GameplayMachineBase.GetODClassSaveMeta(gameplayObject.ObjectClass).StableID);
                    writer.WriteObjectID(machine.GetNetworkOwnerID(gameplayObject.ObjectID));
                    writer.WriteByte((byte)machine.GetGameplayObjectReplicationMode(gameplayObject.ObjectID));
                    writer.WriteInt32(machine.GetGameplayObjectPartition(gameplayObject.ObjectID).Value);

                    List<KeyValuePair<ODFieldName, object>> fields = saveManager
                        .GetStoredFieldsForSave(gameplayObject.ObjectID)
                        .Where(field => includeField == null || includeField(gameplayObject.ObjectID, field.Key))
                        .OrderBy(field => GameplayMachineBase.GetODFieldSaveMeta(field.Key).StableID)
                        .ToList();
                    writer.WriteCount(fields.Count);
                    foreach (KeyValuePair<ODFieldName, object> field in fields)
                    {
                        ODFieldSaveMeta saveMeta = GameplayMachineBase.GetODFieldSaveMeta(field.Key);
                        writer.WriteUInt64(saveMeta.StableID);
                        long payloadPosition = writer.BeginPayload();
                        saveMeta.Writer(writer, field.Value);
                        writer.EndPayload(payloadPosition);
                    }
                }

                writer.WriteBoolean(includePartitions);
                if (includePartitions) machine.WritePartitionManifest(writer, purpose == GameplaySerializationPurpose.Save, embedPartitionPayloads, recipient);
                return writer.Finish();
            }
        }

        // These records deliberately use the same canonical field writers and ordering as a
        // network snapshot, but split the state into independently replaceable Merkle leaves.
        internal static byte[] SaveLockstepMetadataLeaf(
            GameplayMachine machine,
            IGameplayObjectSaveManager saveManager,
            IEnumerable<IGMSerializer> serializers)
        {
            using (GameplaySaveWriter writer = new GameplaySaveWriter(
                       serializers, GameplaySerializationPurpose.Network))
            {
                writer.Machine = machine;
                writer.WriteByte(0xA1);
                writer.WriteUInt16(FormatVersion);
                writer.WriteObjectID(saveManager.IDAllocator);
                writer.WriteObjectID(machine.RootObjectID);
                return writer.Finish();
            }
        }

        internal static byte[] SaveLockstepObjectLeaf(
            GameplayMachine machine,
            IGameplayObjectSaveManager saveManager,
            IGameplayObject gameplayObject,
            IEnumerable<IGMSerializer> serializers)
        {
            using (GameplaySaveWriter writer = new GameplaySaveWriter(
                       serializers, GameplaySerializationPurpose.Network))
            {
                writer.Machine = machine;
                writer.WriteByte(0xA2);
                writer.WriteUInt16(FormatVersion);
                writer.WriteObjectID(gameplayObject.ObjectID);
                writer.WriteUInt64(GameplayMachineBase.GetODClassSaveMeta(gameplayObject.ObjectClass).StableID);
                writer.WriteObjectID(machine.GetNetworkOwnerID(gameplayObject.ObjectID));
                writer.WriteByte((byte)machine.GetGameplayObjectReplicationMode(gameplayObject.ObjectID));
                writer.WriteInt32(machine.GetGameplayObjectPartition(gameplayObject.ObjectID).Value);

                List<KeyValuePair<ODFieldName, object>> fields = saveManager
                    .GetStoredFieldsForSave(gameplayObject.ObjectID)
                    .OrderBy(field => GameplayMachineBase.GetODFieldSaveMeta(field.Key).StableID)
                    .ToList();
                writer.WriteCount(fields.Count);
                foreach (KeyValuePair<ODFieldName, object> field in fields)
                {
                    ODFieldSaveMeta saveMeta = GameplayMachineBase.GetODFieldSaveMeta(field.Key);
                    writer.WriteUInt64(saveMeta.StableID);
                    long payloadPosition = writer.BeginPayload();
                    saveMeta.Writer(writer, field.Value);
                    writer.EndPayload(payloadPosition);
                }
                return writer.Finish();
            }
        }

        public static void Load(
            GameplayMachine machine,
            IGameplayObjectManager objectManager,
            byte[] bytes,
            IEnumerable<IGMSerializer> serializers,
            GameplaySerializationPurpose purpose = GameplaySerializationPurpose.Save,
            bool merge = false,
            bool deferRootValidation = false,
            ISet<GObjectID> expectedObjects = null)
        {
            IGameplayObjectSaveManager saveManager = objectManager as IGameplayObjectSaveManager;
            if (saveManager == null)
            {
                throw new NotSupportedException($"{objectManager.GetType()} does not support gameplay snapshots");
            }

            using (GameplaySaveReader reader = new GameplaySaveReader(bytes, serializers, purpose))
            {
                reader.Machine = machine;
                ValidateHeader(reader);

                GObjectID idAllocator = reader.ReadObjectID();
                GObjectID rootObjectID = reader.ReadObjectID();
                GObjectID localPlayerControllerID = reader.ReadObjectID();
                int objectCount = ReadRequiredCount(reader, "object");
                if (expectedObjects != null && objectCount != expectedObjects.Count)
                    throw new InvalidDataException("Partition payload membership does not match the manifest");
                long objectsPosition = reader.Position;
                if (!merge)
                {
                    saveManager.ResetForLoad(idAllocator);
                    machine.ResetGameplayObjectNetworkMetadataForLoad();
                }
                var loadedObjectIDs = new List<GObjectID>(objectCount);
                var memberships = new Dictionary<GObjectID, PartitionID>();

                int largestObjectID = 0;
                for (int i = 0; i < objectCount; i++)
                {
                    GObjectID objectID = reader.ReadObjectID();
                    if (!objectID.IsValid)
                    {
                        throw new InvalidDataException("Gameplay save contains an invalid object ID");
                    }
                    if (expectedObjects != null && !expectedObjects.Contains(objectID))
                        throw new InvalidDataException("Partition payload contains an object outside its partition");
                    loadedObjectIDs.Add(objectID);
                    largestObjectID = Math.Max(largestObjectID, objectID.ID);
                    ODClassSaveMeta classMeta = GameplayMachineBase.GetODClassSaveMeta(reader.ReadUInt64());
                    GObjectID ownerID = reader.ReadObjectID();
                    GameplayObjectReplicationMode replicationMode = (GameplayObjectReplicationMode)reader.ReadByte();
                    memberships[objectID] = new PartitionID(reader.ReadInt32());
                    if (expectedObjects != null && !memberships[objectID].Equals(machine.GetGameplayObjectPartition(objectID)))
                        throw new InvalidDataException("Partition payload changed object membership");
                    if (!Enum.IsDefined(typeof(GameplayObjectReplicationMode), replicationMode))
                        throw new InvalidDataException("Gameplay save contains an invalid object replication mode");
                    IGameplayObject gameplayObject = objectManager.CreateGameplayObject(classMeta.ClassName, objectID);
                    machine.InitializeGameplayObjectFields(gameplayObject);
                    machine.InitializeGameplayObjectNetworkMetadata(objectID, classMeta.ClassName);
                    machine.SetGameplayObjectNetworkMetadataForLoad(objectID, ownerID, replicationMode);

                    int fieldCount = ReadRequiredCount(reader, "field");
                    for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
                    {
                        reader.ReadUInt64();
                        reader.Skip(ReadPayloadLength(reader));
                    }
                }

                if (idAllocator.ID < largestObjectID)
                {
                    throw new InvalidDataException("Gameplay save object allocator is behind an existing object ID");
                }

                reader.Position = objectsPosition;
                for (int i = 0; i < objectCount; i++)
                {
                    GObjectID objectID = reader.ReadObjectID();
                    ODClassSaveMeta classMeta = GameplayMachineBase.GetODClassSaveMeta(reader.ReadUInt64());
                    reader.ReadObjectID();
                    reader.ReadByte();
                    reader.ReadInt32();
                    int fieldCount = ReadRequiredCount(reader, "field");
                    for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
                    {
                        ulong fieldID = reader.ReadUInt64();
                        int payloadLength = ReadPayloadLength(reader);
                        long payloadEnd = checked(reader.Position + payloadLength);

                        ODFieldSaveMeta saveMeta;
                        if (!GameplayMachineBase.TryGetODFieldSaveMeta(fieldID, out saveMeta))
                        {
                            reader.Skip(payloadLength);
                            continue;
                        }

                        ODFieldMeta fieldMeta = GameplayMachineBase.GetODFieldMeta(saveMeta.FieldName);
                        if (!GameplayMachineBase.CanCastTo(classMeta.ClassName, fieldMeta.FromClass))
                        {
                            throw new InvalidDataException(
                                $"Gameplay save field {saveMeta.StableName} does not belong to class {classMeta.StableName}");
                        }

                        object value = saveMeta.Reader(reader);
                        if (reader.Position != payloadEnd)
                        {
                            throw new InvalidDataException(
                                $"Gameplay save field {saveMeta.StableName} consumed an unexpected number of bytes " +
                                $"(position {reader.Position}, expected {payloadEnd})");
                        }
                        saveManager.SetStoredFieldForLoad(objectID, saveMeta.FieldName, value);
                    }
                }

                if (reader.ReadBoolean()) machine.ReadPartitionManifest(reader, !merge);
                foreach (var member in memberships) machine.RegisterGameplayObjectPartition(member.Key, member.Value);
                if (!merge)
                {
                    if (deferRootValidation) machine.SetPartitionedRootsForLoad(rootObjectID, localPlayerControllerID);
                    else
                    {
                        machine.SetRootForLoad(rootObjectID);
                        machine.SetLocalPlayerControllerForLoad(localPlayerControllerID);
                    }
                }
                machine.RebuildGameplayReferenceIndex();
                machine.RebuildGameplaySpatialIndex();

                reader.EnsureAt(reader.Length);
                if (purpose == GameplaySerializationPurpose.Network)
                    foreach (GObjectID objectID in loadedObjectIDs)
                        machine.NotifyGameplayObjectCreatedFromReplication(objectID);
            }
        }

        private static void ValidateHeader(GameplaySaveReader reader)
        {
            if (reader.ReadUInt32() != Magic)
            {
                throw new InvalidDataException("Gameplay save magic does not match GMS2");
            }

            ushort version = reader.ReadUInt16();
            if (version != FormatVersion)
            {
                throw new InvalidDataException($"Unsupported gameplay save format version {version}");
            }
            reader.ReadUInt16();
        }

        private static int ReadRequiredCount(GameplaySaveReader reader, string name)
        {
            int result = reader.ReadCount();
            if (result < 0)
            {
                throw new InvalidDataException($"Gameplay save {name} count cannot be null");
            }
            return result;
        }

        private static int ReadPayloadLength(GameplaySaveReader reader)
        {
            int result = reader.ReadInt32();
            if (result < 0)
            {
                throw new InvalidDataException("Gameplay save field payload length cannot be negative");
            }
            return result;
        }
    }
}
