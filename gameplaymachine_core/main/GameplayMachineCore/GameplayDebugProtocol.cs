using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GMCore
{
    internal enum GameplayDebugMessageType : byte
    {
        Hello = 1,
        MachineInfo = 2,
        Schema = 3,
        Objects = 4,
        ObjectSnapshot = 5,
        SetField = 6,
        CreateObject = 7,
        DeleteObject = 8,
        InvokeInterface = 9,
        Response = 100,
        Trace = 101,
        ObjectChanged = 102,
    }

    internal static class GameplayDebugProtocol
    {
        public const uint Magic = 0x4742444f; // ODBG
        public const ushort Version = 8;
        public const int MaxPacketBytes = 16 * 1024 * 1024;

        public static byte[] Packet(GameplayDebugMessageType type, long requestID, Action<BinaryWriter> body = null)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write((byte)type);
                writer.Write(requestID);
                body?.Invoke(writer);
                return stream.ToArray();
            }
        }

        public static void ReadPacket(byte[] packet, Action<GameplayDebugMessageType, long, BinaryReader> read)
        {
            if (packet == null || packet.Length > MaxPacketBytes)
                throw new InvalidDataException("Invalid OD debug packet size");
            using (var stream = new MemoryStream(packet, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != Version)
                    throw new InvalidDataException("OD debug protocol version does not match");
                var type = (GameplayDebugMessageType)reader.ReadByte();
                long requestID = reader.ReadInt64();
                read(type, requestID, reader);
                if (stream.Position != stream.Length)
                    throw new InvalidDataException($"OD debug packet {type} contains {stream.Length - stream.Position} trailing bytes");
            }
        }

        public static void WriteFrame(Stream stream, byte[] packet)
        {
            byte[] length = BitConverter.GetBytes(packet.Length);
            stream.Write(length, 0, length.Length);
            stream.Write(packet, 0, packet.Length);
            stream.Flush();
        }

        public static byte[] ReadFrame(Stream stream)
        {
            if (!TryReadFrame(stream, out byte[] packet))
                throw new EndOfStreamException("OD debug connection closed before the next frame");
            return packet;
        }

        public static bool TryReadFrame(Stream stream, out byte[] packet)
        {
            packet = null;
            if (!TryReadExactly(stream, 4, out byte[] lengthBytes))
                return false;
            int length = BitConverter.ToInt32(lengthBytes, 0);
            if (length <= 0 || length > MaxPacketBytes)
                throw new InvalidDataException("Invalid OD debug frame size");
            packet = ReadExactly(stream, length);
            return true;
        }

        private static byte[] ReadExactly(Stream stream, int count)
        {
            byte[] result = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(result, offset, count - offset);
                if (read <= 0)
                    throw new EndOfStreamException();
                offset += read;
            }
            return result;
        }

        private static bool TryReadExactly(Stream stream, int count, out byte[] result)
        {
            result = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(result, offset, count - offset);
                if (read <= 0)
                {
                    if (offset == 0)
                    {
                        result = null;
                        return false;
                    }
                    throw new EndOfStreamException("OD debug connection closed in the middle of a frame header");
                }
                offset += read;
            }
            return true;
        }

        public static void WriteValue(BinaryWriter writer, GameplayDebugValue value)
        {
            value = value ?? GameplayDebugValue.Null();
            writer.Write((byte)value.Kind);
            WriteNullableString(writer, value.TypeName);
            WriteNullableString(writer, value.Scalar);
            writer.Write(value.ObjectID);
            writer.Write(value.Items?.Count ?? 0);
            if (value.Items != null)
                foreach (GameplayDebugValue item in value.Items)
                    WriteValue(writer, item);
            writer.Write(value.Members?.Count ?? 0);
            if (value.Members != null)
                foreach (GameplayDebugNamedValue member in value.Members)
                {
                    writer.Write(member.Name ?? string.Empty);
                    WriteValue(writer, member.Value);
                }
            writer.Write(value.Entries?.Count ?? 0);
            if (value.Entries != null)
                foreach (GameplayDebugMapEntry entry in value.Entries)
                {
                    WriteValue(writer, entry.Key);
                    WriteValue(writer, entry.Value);
                }
        }

        public static GameplayDebugValue ReadValue(BinaryReader reader)
        {
            var value = new GameplayDebugValue
            {
                Kind = (GameplayDebugValueKind)reader.ReadByte(),
                TypeName = ReadNullableString(reader),
                Scalar = ReadNullableString(reader),
                ObjectID = reader.ReadInt32(),
            };
            int itemCount = ReadCount(reader);
            for (int i = 0; i < itemCount; i++) value.Items.Add(ReadValue(reader));
            int memberCount = ReadCount(reader);
            for (int i = 0; i < memberCount; i++)
                value.Members.Add(new GameplayDebugNamedValue { Name = reader.ReadString(), Value = ReadValue(reader) });
            int entryCount = ReadCount(reader);
            for (int i = 0; i < entryCount; i++)
                value.Entries.Add(new GameplayDebugMapEntry { Key = ReadValue(reader), Value = ReadValue(reader) });
            return value;
        }

        public static void WriteMachineInfo(BinaryWriter writer, GameplayDebugMachineInfo value)
        {
            writer.Write(value.MachineKey);
            writer.Write((byte)value.Access);
            writer.Write((byte)value.Role);
            writer.Write((byte)value.NetworkState);
            writer.Write(value.NetworkRevision);
            writer.Write(value.NetworkSchemaID);
            writer.Write(value.ResourceCatalogID);
            writer.Write(value.DebugSchemaID);
            writer.Write(value.ObjectCount);
            writer.Write(value.RootObjectID);
            writer.Write(value.DuringExecution);
            writer.Write(value.HasRoutine);
            writer.Write(value.Modules?.Length ?? 0);
            if (value.Modules != null) foreach (string module in value.Modules) writer.Write(module ?? string.Empty);
        }

        public static GameplayDebugMachineInfo ReadMachineInfo(BinaryReader reader)
        {
            var value = new GameplayDebugMachineInfo
            {
                MachineKey = reader.ReadInt32(),
                Access = (GameplayDebugAccess)reader.ReadByte(),
                Role = (GameplayMachineRole)reader.ReadByte(),
                NetworkState = (GameplayNetworkState)reader.ReadByte(),
                NetworkRevision = reader.ReadInt64(),
                NetworkSchemaID = reader.ReadUInt64(),
                ResourceCatalogID = reader.ReadUInt64(),
                DebugSchemaID = reader.ReadUInt64(),
                ObjectCount = reader.ReadInt32(),
                RootObjectID = reader.ReadInt32(),
                DuringExecution = reader.ReadBoolean(),
                HasRoutine = reader.ReadBoolean(),
            };
            int count = ReadCount(reader);
            value.Modules = new string[count];
            for (int i = 0; i < count; i++) value.Modules[i] = reader.ReadString();
            return value;
        }

        public static void WriteFieldInfo(BinaryWriter writer, GameplayDebugFieldInfo value)
        {
            writer.Write(value.StableID);
            writer.Write(value.Name ?? string.Empty);
            writer.Write(value.TypeName ?? string.Empty);
            WriteNullableString(writer, value.KeyTypeName);
            writer.Write((byte)value.Container);
            writer.Write(value.IsDriven);
            writer.Write((byte)value.ReplicationMode);
            writer.Write(value.IsGameplayObjectReference);
            writer.Write(value.GameplayObjectClassID);
            writer.Write(value.IsResourceReference);
            writer.Write(value.IsResourceKeyReference);
            WriteResourceOptions(writer, value.ResourceOptions);
            WriteResourceOptions(writer, value.ResourceKeyOptions);
            WriteValue(writer, value.DefaultValue);
        }

        public static GameplayDebugFieldInfo ReadFieldInfo(BinaryReader reader) => new GameplayDebugFieldInfo
        {
            StableID = reader.ReadUInt64(),
            Name = reader.ReadString(),
            TypeName = reader.ReadString(),
            KeyTypeName = ReadNullableString(reader),
            Container = (GameplayDebugContainerKind)reader.ReadByte(),
            IsDriven = reader.ReadBoolean(),
            ReplicationMode = (ODFieldReplicationMode)reader.ReadByte(),
            IsGameplayObjectReference = reader.ReadBoolean(),
            GameplayObjectClassID = reader.ReadUInt64(),
            IsResourceReference = reader.ReadBoolean(),
            IsResourceKeyReference = reader.ReadBoolean(),
            ResourceOptions = ReadResourceOptions(reader),
            ResourceKeyOptions = ReadResourceOptions(reader),
            DefaultValue = ReadValue(reader),
        };

        private static void WriteResourceOptions(BinaryWriter writer, IReadOnlyList<GameplayDebugResourceOption> options)
        {
            writer.Write(options?.Count ?? 0);
            if (options == null) return;
            foreach (GameplayDebugResourceOption option in options)
            {
                writer.Write(option.ID ?? string.Empty);
                writer.Write(option.Name ?? string.Empty);
            }
        }

        private static List<GameplayDebugResourceOption> ReadResourceOptions(BinaryReader reader)
        {
            int count = ReadCount(reader);
            var result = new List<GameplayDebugResourceOption>(count);
            for (int i = 0; i < count; i++)
                result.Add(new GameplayDebugResourceOption { ID = reader.ReadString(), Name = reader.ReadString() });
            return result;
        }

        public static void WriteObjectInfo(BinaryWriter writer, GameplayDebugObjectInfo value)
        {
            writer.Write(value.ObjectID);
            writer.Write(value.ClassID);
            writer.Write(value.ClassName ?? string.Empty);
            writer.Write(value.IsRoot);
        }

        public static GameplayDebugObjectInfo ReadObjectInfo(BinaryReader reader) => new GameplayDebugObjectInfo
        {
            ObjectID = reader.ReadInt32(),
            ClassID = reader.ReadUInt64(),
            ClassName = reader.ReadString(),
            IsRoot = reader.ReadBoolean(),
        };

        public static void WriteTrace(BinaryWriter writer, GameplayExecutionTraceEvent value)
        {
            writer.Write((byte)value.Stage);
            writer.Write(value.TraceID);
            writer.Write(value.SpanID);
            writer.Write(value.ParentSpanID);
            writer.Write(value.Depth);
            writer.Write(value.InterfaceName ?? string.Empty);
            writer.Write((byte)value.RpcMode);
            writer.Write(value.Origin ?? string.Empty);
            writer.Write(value.TimestampUtc.ToBinary());
            writer.Write(value.DurationMilliseconds);
            writer.Write(value.Succeeded);
            WriteNullableString(writer, value.Error);
            WriteValue(writer, value.Input);
            WriteValue(writer, value.Output);
        }

        public static GameplayExecutionTraceEvent ReadTrace(BinaryReader reader) => new GameplayExecutionTraceEvent
        {
            Stage = (GameplayExecutionTraceStage)reader.ReadByte(),
            TraceID = reader.ReadInt64(),
            SpanID = reader.ReadInt64(),
            ParentSpanID = reader.ReadInt64(),
            Depth = reader.ReadInt32(),
            InterfaceName = reader.ReadString(),
            RpcMode = (GameplayRpcMode)reader.ReadByte(),
            Origin = reader.ReadString(),
            TimestampUtc = DateTime.FromBinary(reader.ReadInt64()),
            DurationMilliseconds = reader.ReadDouble(),
            Succeeded = reader.ReadBoolean(),
            Error = ReadNullableString(reader),
            Input = ReadValue(reader),
            Output = ReadValue(reader),
        };

        public static void WriteNullableString(BinaryWriter writer, string value)
        {
            writer.Write(value != null);
            if (value != null) writer.Write(value);
        }

        public static string ReadNullableString(BinaryReader reader) => reader.ReadBoolean() ? reader.ReadString() : null;

        public static int ReadCount(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > 1_000_000) throw new InvalidDataException("Invalid OD debug collection count");
            return count;
        }
    }
}
