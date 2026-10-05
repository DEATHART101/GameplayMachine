using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace GMCore
{
    public sealed class GameplayDebugClient : IDisposable
    {
        private sealed class PendingRequest
        {
            public Func<BinaryReader, object> Read;
            public TaskCompletionSource<object> Completion;
        }

        private readonly ConcurrentDictionary<long, PendingRequest> m_pending =
            new ConcurrentDictionary<long, PendingRequest>();
        private readonly object m_sendLock = new object();
        private TcpClient m_client;
        private Thread m_receiveThread;
        private volatile bool m_running;
        private long m_nextRequestID;

        public bool IsConnected => m_running && m_client?.Connected == true;
        public event Action<GameplayExecutionTraceEvent> TraceReceived;
        public event Action<int, GameplayDebugObjectChangeKind> ObjectChanged;
        public event Action<Exception> Disconnected;

        public async Task<GameplayDebugMachineInfo> ConnectAsync(string host, int port, string token = null)
        {
            if (m_running) throw new InvalidOperationException("The OD debug client is already connected");
            m_client = new TcpClient { NoDelay = true };
            await m_client.ConnectAsync(host, port).ConfigureAwait(false);
            m_running = true;
            m_receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "OD Debug Client Receive" };
            m_receiveThread.Start();
            return await RequestAsync(GameplayDebugMessageType.Hello,
                writer => GameplayDebugProtocol.WriteNullableString(writer, token),
                GameplayDebugProtocol.ReadMachineInfo).ConfigureAwait(false);
        }

        public Task<GameplayDebugMachineInfo> GetMachineInfoAsync() =>
            RequestAsync(GameplayDebugMessageType.MachineInfo, null, GameplayDebugProtocol.ReadMachineInfo);

        public Task<GameplayDebugSchema> GetSchemaAsync() =>
            RequestAsync(GameplayDebugMessageType.Schema, null, ReadSchema);

        public Task<IReadOnlyList<GameplayDebugObjectInfo>> GetObjectsAsync() =>
            RequestAsync<IReadOnlyList<GameplayDebugObjectInfo>>(GameplayDebugMessageType.Objects, null, reader =>
            {
                int count = GameplayDebugProtocol.ReadCount(reader);
                var result = new List<GameplayDebugObjectInfo>(count);
                for (int i = 0; i < count; i++) result.Add(GameplayDebugProtocol.ReadObjectInfo(reader));
                return result;
            });

        public Task<GameplayDebugObjectSnapshot> GetObjectAsync(int objectID) =>
            RequestAsync(GameplayDebugMessageType.ObjectSnapshot, writer => writer.Write(objectID), reader =>
            {
                var result = new GameplayDebugObjectSnapshot { Object = GameplayDebugProtocol.ReadObjectInfo(reader) };
                int count = GameplayDebugProtocol.ReadCount(reader);
                for (int i = 0; i < count; i++)
                    result.Fields.Add(new GameplayDebugFieldValue
                    {
                        FieldID = reader.ReadUInt64(),
                        Value = GameplayDebugProtocol.ReadValue(reader),
                    });
                return result;
            });

        public Task<bool> SetFieldAsync(int objectID, ulong fieldID, GameplayDebugValue value) =>
            RequestAsync(GameplayDebugMessageType.SetField, writer =>
            {
                writer.Write(objectID);
                writer.Write(fieldID);
                GameplayDebugProtocol.WriteValue(writer, value);
            }, reader => reader.ReadBoolean());

        public Task<int> CreateObjectAsync(ulong classID) =>
            RequestAsync(GameplayDebugMessageType.CreateObject, writer => writer.Write(classID), reader => reader.ReadInt32());

        public Task<bool> DeleteObjectAsync(int objectID) =>
            RequestAsync(GameplayDebugMessageType.DeleteObject, writer => writer.Write(objectID), reader => reader.ReadBoolean());

        public Task<ODDebugInvocationResult> InvokeAsync(ulong interfaceID,
            IReadOnlyDictionary<string, GameplayDebugValue> inputs,
            int playerControllerID = 0)
        {
            return RequestAsync(GameplayDebugMessageType.InvokeInterface, writer =>
            {
                writer.Write(interfaceID);
                writer.Write(playerControllerID);
                writer.Write(inputs?.Count ?? 0);
                if (inputs != null)
                    foreach (KeyValuePair<string, GameplayDebugValue> input in inputs)
                    {
                        writer.Write(input.Key);
                        GameplayDebugProtocol.WriteValue(writer, input.Value);
                    }
            }, reader => new ODDebugInvocationResult
            {
                Succeeded = reader.ReadBoolean(),
                Error = GameplayDebugProtocol.ReadNullableString(reader),
                Output = GameplayDebugProtocol.ReadValue(reader),
            });
        }

        private Task<T> RequestAsync<T>(GameplayDebugMessageType type, Action<BinaryWriter> write,
            Func<BinaryReader, T> read)
        {
            if (!m_running) throw new InvalidOperationException("The OD debug client is not connected");
            long requestID = Interlocked.Increment(ref m_nextRequestID);
            var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_pending[requestID] = new PendingRequest
            {
                Completion = completion,
                Read = reader => read(reader),
            };
            try
            {
                byte[] packet = GameplayDebugProtocol.Packet(type, requestID, write);
                lock (m_sendLock) GameplayDebugProtocol.WriteFrame(m_client.GetStream(), packet);
            }
            catch
            {
                m_pending.TryRemove(requestID, out _);
                throw;
            }
            return AwaitResult<T>(completion.Task);
        }

        private static async Task<T> AwaitResult<T>(Task<object> task) => (T)await task.ConfigureAwait(false);

        private void ReceiveLoop()
        {
            Exception disconnectReason = null;
            bool notifyDisconnected = false;
            try
            {
                NetworkStream stream = m_client.GetStream();
                while (m_running)
                {
                    if (!GameplayDebugProtocol.TryReadFrame(stream, out byte[] packet))
                    {
                        if (m_running)
                            notifyDisconnected = true;
                        break;
                    }
                    GameplayDebugProtocol.ReadPacket(packet, ProcessPacket);
                }
            }
            catch (Exception exception)
            {
                if (m_running)
                {
                    disconnectReason = exception;
                    notifyDisconnected = true;
                }
            }
            finally
            {
                m_running = false;
                foreach (KeyValuePair<long, PendingRequest> pending in m_pending)
                    if (m_pending.TryRemove(pending.Key, out PendingRequest removed))
                        removed.Completion.TrySetException(disconnectReason ?? new EndOfStreamException("OD debug connection closed"));
                if (notifyDisconnected)
                    Disconnected?.Invoke(disconnectReason);
            }
        }

        private void ProcessPacket(GameplayDebugMessageType type, long requestID, BinaryReader reader)
        {
            if (type == GameplayDebugMessageType.Trace)
            {
                GameplayExecutionTraceEvent trace = GameplayDebugProtocol.ReadTrace(reader);
                TraceReceived?.Invoke(trace);
                return;
            }
            if (type == GameplayDebugMessageType.ObjectChanged)
            {
                int objectID = reader.ReadInt32();
                GameplayDebugObjectChangeKind changeKind = (GameplayDebugObjectChangeKind)reader.ReadByte();
                ObjectChanged?.Invoke(objectID, changeKind);
                return;
            }
            if (type != GameplayDebugMessageType.Response || !m_pending.TryRemove(requestID, out PendingRequest pending))
                throw new InvalidDataException("Unexpected OD debug response");
            bool success = reader.ReadBoolean();
            string error = GameplayDebugProtocol.ReadNullableString(reader);
            if (!success)
            {
                pending.Completion.TrySetException(new InvalidOperationException(error ?? "OD debug request failed"));
                return;
            }
            try { pending.Completion.TrySetResult(pending.Read(reader)); }
            catch (Exception exception) { pending.Completion.TrySetException(exception); }
        }

        private static GameplayDebugSchema ReadSchema(BinaryReader reader)
        {
            var schema = new GameplayDebugSchema();
            int classCount = GameplayDebugProtocol.ReadCount(reader);
            for (int i = 0; i < classCount; i++)
            {
                var item = new GameplayDebugClassInfo
                {
                    StableID = reader.ReadUInt64(),
                    Name = reader.ReadString(),
                    IsEngineManaged = reader.ReadBoolean(),
                };
                int assignableCount = GameplayDebugProtocol.ReadCount(reader);
                for (int assignable = 0; assignable < assignableCount; assignable++)
                    item.AssignableClassIDs.Add(reader.ReadUInt64());
                int fieldCount = GameplayDebugProtocol.ReadCount(reader);
                for (int field = 0; field < fieldCount; field++) item.Fields.Add(GameplayDebugProtocol.ReadFieldInfo(reader));
                schema.Classes.Add(item);
            }
            int interfaceCount = GameplayDebugProtocol.ReadCount(reader);
            for (int i = 0; i < interfaceCount; i++)
            {
                var item = new GameplayDebugInterfaceInfo
                {
                    StableID = reader.ReadUInt64(),
                    Name = reader.ReadString(),
                    RpcMode = (GameplayRpcMode)reader.ReadByte(),
                    InterfaceType = (GameplayInterfaceType)reader.ReadByte(),
                    IsRoutine = reader.ReadBoolean(),
                };
                ReadInterfaceFields(reader, item.Inputs);
                ReadInterfaceFields(reader, item.Outputs);
                schema.Interfaces.Add(item);
            }
            return schema;
        }

        private static void ReadInterfaceFields(BinaryReader reader, List<GameplayDebugFieldInfo> target)
        {
            int count = GameplayDebugProtocol.ReadCount(reader);
            for (int i = 0; i < count; i++) target.Add(GameplayDebugProtocol.ReadFieldInfo(reader));
        }

        public void Dispose()
        {
            m_running = false;
            try { m_client?.Close(); } catch { }
            if (m_receiveThread != null && Thread.CurrentThread != m_receiveThread)
                m_receiveThread.Join(1000);
        }
    }
}
