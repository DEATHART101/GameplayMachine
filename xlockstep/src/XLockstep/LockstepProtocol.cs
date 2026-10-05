using System;
using System.IO;

namespace XLockstep
{
    public enum LockstepMessageKind : byte
    {
        Hello = 1,
        Welcome = 2,
        SubmitInput = 3,
        InputAccepted = 4,
        Frame = 5,
        StateHash = 6,
        Reject = 7,
        Ping = 8,
        Pong = 9,
        AdvanceTicks = 10,
        Ready = 11,
    }

    public enum LockstepStateHashKind : byte
    {
        Incremental = 0,
        FullAudit = 1,
    }

    public sealed class LockstepMessage
    {
        public LockstepMessageKind Kind { get; set; }
        public ulong SchemaId { get; set; }
        public ulong ResourceCatalogId { get; set; }
        public ulong[] InterfaceTypeIds { get; set; } = Array.Empty<ulong>();
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public int PlayerId { get; set; }
        public int PlayerCount { get; set; }
        public int InputDelay { get; set; }
        public int MaximumEmptyTicks { get; set; }
        public int StateHashIntervalTicks { get; set; }
        public int FullStateHashIntervalTicks { get; set; }
        public LockstepTick Tick { get; set; }
        public LockstepTick FromTick { get; set; }
        public LockstepTick RequestedTick { get; set; }
        public long CommandId { get; set; }
        public long Sequence { get; set; }
        public ulong TypeId { get; set; }
        public LockstepCommand? Command { get; set; }
        public LockstepFrame? Frame { get; set; }
        public ulong StateHash { get; set; }
        public LockstepStateHashKind StateHashKind { get; set; }
        public string Error { get; set; } = string.Empty;
    }

    public static class LockstepProtocol
    {
        private const uint Magic = 0x31534C58U; // XLS1
        private const ushort Version = 4;

        public static byte[] Write(LockstepMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write((byte)message.Kind);
                switch (message.Kind)
                {
                    case LockstepMessageKind.Hello:
                        writer.Write(message.SchemaId);
                        writer.Write(message.ResourceCatalogId);
                        WriteUInt64Array(writer, message.InterfaceTypeIds);
                        WriteBytes(writer, message.Data);
                        break;
                    case LockstepMessageKind.Welcome:
                        writer.Write(message.PlayerId);
                        writer.Write(message.PlayerCount);
                        writer.Write(message.Tick.Value);
                        writer.Write(message.InputDelay);
                        writer.Write(message.MaximumEmptyTicks);
                        writer.Write(message.StateHashIntervalTicks);
                        writer.Write(message.FullStateHashIntervalTicks);
                        break;
                    case LockstepMessageKind.SubmitInput:
                        WriteCommand(writer, Required(message.Command));
                        break;
                    case LockstepMessageKind.InputAccepted:
                        writer.Write(message.CommandId);
                        writer.Write(message.RequestedTick.Value);
                        writer.Write(message.Tick.Value);
                        break;
                    case LockstepMessageKind.Frame:
                        WriteFrame(writer, Required(message.Frame));
                        break;
                    case LockstepMessageKind.StateHash:
                        writer.Write(message.PlayerId);
                        writer.Write(message.Tick.Value);
                        writer.Write(message.StateHash);
                        writer.Write((byte)message.StateHashKind);
                        break;
                    case LockstepMessageKind.Reject:
                        writer.Write(message.Error ?? string.Empty);
                        break;
                    case LockstepMessageKind.Ping:
                    case LockstepMessageKind.Pong:
                        writer.Write(message.Sequence);
                        break;
                    case LockstepMessageKind.AdvanceTicks:
                        writer.Write(message.FromTick.Value);
                        writer.Write(message.Tick.Value);
                        break;
                    case LockstepMessageKind.Ready:
                        break;
                    default:
                        throw new InvalidDataException("Unknown lockstep message kind");
                }
                return stream.ToArray();
            }
        }

        public static LockstepMessage Read(byte[] bytes, int maximumMessageBytes = 16 * 1024 * 1024)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > maximumMessageBytes)
                throw new InvalidDataException("Lockstep message size is invalid");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != Version)
                    throw new InvalidDataException("Lockstep protocol header does not match");
                var result = new LockstepMessage { Kind = (LockstepMessageKind)reader.ReadByte() };
                switch (result.Kind)
                {
                    case LockstepMessageKind.Hello:
                        result.SchemaId = reader.ReadUInt64();
                        result.ResourceCatalogId = reader.ReadUInt64();
                        result.InterfaceTypeIds = ReadUInt64Array(reader, maximumMessageBytes);
                        result.Data = ReadBytes(reader, maximumMessageBytes);
                        break;
                    case LockstepMessageKind.Welcome:
                        result.PlayerId = reader.ReadInt32();
                        result.PlayerCount = reader.ReadInt32();
                        result.Tick = new LockstepTick(reader.ReadInt64());
                        result.InputDelay = reader.ReadInt32();
                        result.MaximumEmptyTicks = reader.ReadInt32();
                        result.StateHashIntervalTicks = reader.ReadInt32();
                        result.FullStateHashIntervalTicks = reader.ReadInt32();
                        break;
                    case LockstepMessageKind.SubmitInput:
                        result.Command = ReadCommand(reader, maximumMessageBytes);
                        break;
                    case LockstepMessageKind.InputAccepted:
                        result.CommandId = reader.ReadInt64();
                        result.RequestedTick = new LockstepTick(reader.ReadInt64());
                        result.Tick = new LockstepTick(reader.ReadInt64());
                        break;
                    case LockstepMessageKind.Frame:
                        result.Frame = ReadFrame(reader, maximumMessageBytes);
                        break;
                    case LockstepMessageKind.StateHash:
                        result.PlayerId = reader.ReadInt32();
                        result.Tick = new LockstepTick(reader.ReadInt64());
                        result.StateHash = reader.ReadUInt64();
                        result.StateHashKind = (LockstepStateHashKind)reader.ReadByte();
                        if (!Enum.IsDefined(typeof(LockstepStateHashKind), result.StateHashKind))
                            throw new InvalidDataException("Lockstep state hash kind is invalid");
                        break;
                    case LockstepMessageKind.Reject:
                        result.Error = reader.ReadString();
                        break;
                    case LockstepMessageKind.Ping:
                    case LockstepMessageKind.Pong:
                        result.Sequence = reader.ReadInt64();
                        break;
                    case LockstepMessageKind.AdvanceTicks:
                        result.FromTick = new LockstepTick(reader.ReadInt64());
                        result.Tick = new LockstepTick(reader.ReadInt64());
                        break;
                    case LockstepMessageKind.Ready:
                        break;
                    default:
                        throw new InvalidDataException("Unknown lockstep message kind");
                }
                if (stream.Position != stream.Length)
                    throw new InvalidDataException("Lockstep message contains trailing data");
                return result;
            }
        }

        private static void WriteFrame(BinaryWriter writer, LockstepFrame frame)
        {
            writer.Write(frame.Tick.Value);
            LockstepCommand[] commands = frame.Commands ?? Array.Empty<LockstepCommand>();
            writer.Write(commands.Length);
            foreach (LockstepCommand command in commands)
                WriteCommand(writer, command);
        }

        private static LockstepFrame ReadFrame(BinaryReader reader, int maximumBytes)
        {
            var frame = new LockstepFrame { Tick = new LockstepTick(reader.ReadInt64()) };
            int count = reader.ReadInt32();
            if (count < 0 || count > 4096)
                throw new InvalidDataException("Lockstep command count is invalid");
            frame.Commands = new LockstepCommand[count];
            for (int index = 0; index < count; index++)
                frame.Commands[index] = ReadCommand(reader, maximumBytes);
            return frame;
        }

        private static void WriteCommand(BinaryWriter writer, LockstepCommand command)
        {
            writer.Write(command.CommandId);
            writer.Write(command.PlayerId);
            writer.Write(command.Sequence);
            writer.Write(command.RequestedTick.Value);
            writer.Write(command.AcceptedTick.Value);
            writer.Write(command.TypeId);
            WriteBytes(writer, command.Payload);
        }

        private static LockstepCommand ReadCommand(BinaryReader reader, int maximumBytes) => new LockstepCommand
        {
            CommandId = reader.ReadInt64(),
            PlayerId = reader.ReadInt32(),
            Sequence = reader.ReadInt64(),
            RequestedTick = new LockstepTick(reader.ReadInt64()),
            AcceptedTick = new LockstepTick(reader.ReadInt64()),
            TypeId = reader.ReadUInt64(),
            Payload = ReadBytes(reader, maximumBytes),
        };

        private static void WriteBytes(BinaryWriter writer, byte[]? bytes)
        {
            bytes = bytes ?? Array.Empty<byte>();
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static void WriteUInt64Array(BinaryWriter writer, ulong[]? values)
        {
            values = values ?? Array.Empty<ulong>();
            writer.Write(values.Length);
            foreach (ulong value in values)
                writer.Write(value);
        }

        private static ulong[] ReadUInt64Array(BinaryReader reader, int maximumBytes)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > maximumBytes / sizeof(ulong))
                throw new InvalidDataException("Lockstep UInt64 array size is invalid");
            var result = new ulong[count];
            for (int index = 0; index < count; index++)
                result[index] = reader.ReadUInt64();
            return result;
        }

        private static byte[] ReadBytes(BinaryReader reader, int maximumBytes)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > maximumBytes)
                throw new InvalidDataException("Lockstep payload size is invalid");
            byte[] value = reader.ReadBytes(count);
            if (value.Length != count)
                throw new EndOfStreamException();
            return value;
        }

        private static T Required<T>(T? value) where T : class =>
            value ?? throw new InvalidDataException("Lockstep message payload is missing");
    }
}
