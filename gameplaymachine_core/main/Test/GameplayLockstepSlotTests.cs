using System.Collections.Concurrent;
using GMCore;
using XLockstep;

namespace Test;

[TestFixture]
public sealed class GameplayLockstepSlotTests
{
    [Test]
    public void RelayRejectsNonexistentRequestedSlot()
    {
        using var transport = new RelayTestTransport();
        using var relay = new GameplayLockstepRelayServer(transport,
            new GameplayLockstepOptions { ExpectedPlayerCount = 2 });
        relay.Start();

        var connection = new GameplayConnectionID { Value = 10 };
        transport.EnqueueConnected(connection);
        transport.EnqueueData(connection, CreateHelloPacket(connection, requestedSlot: 2));
        relay.Update();

        Assert.That(transport.Disconnections, Has.Some.Matches<(GameplayConnectionID, string)>(
            item => item.Item1.Equals(connection) && item.Item2.Contains("does not exist")));
        Assert.That(relay.ConnectedPlayerCount, Is.Zero);
    }

    [Test]
    public void RelayRejectsOccupiedRequestedSlot()
    {
        using var transport = new RelayTestTransport();
        using var relay = new GameplayLockstepRelayServer(transport,
            new GameplayLockstepOptions { ExpectedPlayerCount = 2 });
        relay.Start();

        var first = new GameplayConnectionID { Value = 11 };
        var second = new GameplayConnectionID { Value = 12 };
        transport.EnqueueConnected(first);
        transport.EnqueueData(first, CreateHelloPacket(first, requestedSlot: 0));
        transport.EnqueueConnected(second);
        transport.EnqueueData(second, CreateHelloPacket(second, requestedSlot: 0));
        relay.Update();

        Assert.That(transport.Disconnections, Has.Some.Matches<(GameplayConnectionID, string)>(
            item => item.Item1.Equals(second) && item.Item2.Contains("already occupied")));
        Assert.That(relay.ConnectedPlayerCount, Is.EqualTo(1));
    }

    private static byte[] CreateHelloPacket(GameplayConnectionID connection, int requestedSlot)
    {
        using var transport = new RelayTestTransport();
        var channel = new GameplayNetworkMessageChannel(transport, new GameplayLockstepOptions());
        channel.Send(connection, LockstepProtocol.Write(new LockstepMessage
        {
            Kind = LockstepMessageKind.Hello,
            SchemaId = 1,
            ResourceCatalogId = 2,
            InterfaceTypeIds = Array.Empty<ulong>(),
            Data = GameplayLockstepConnectionRequestCodec.Write(requestedSlot, Array.Empty<byte>()),
        }));
        return transport.Sent.Single().Data;
    }

    private sealed class RelayTestTransport : IGameplayNetworkTransport
    {
        private readonly ConcurrentQueue<GameplayNetworkTransportEvent> events = new();

        public List<(GameplayConnectionID Connection, byte[] Data)> Sent { get; } = new();
        public List<(GameplayConnectionID Connection, string Reason)> Disconnections { get; } = new();

        public void StartServer() { }
        public void Connect() { }
        public void Stop() { }
        public void Dispose() { }

        public bool TryPollEvent(out GameplayNetworkTransportEvent networkEvent) =>
            events.TryDequeue(out networkEvent);

        public void Send(GameplayConnectionID connection, byte[] data,
            GameplayNetworkDelivery delivery = GameplayNetworkDelivery.ReliableOrdered) =>
            Sent.Add((connection, data));

        public void Disconnect(GameplayConnectionID connection, string reason = null) =>
            Disconnections.Add((connection, reason ?? string.Empty));

        public void EnqueueConnected(GameplayConnectionID connection) =>
            events.Enqueue(new GameplayNetworkTransportEvent
            {
                Type = GameplayNetworkTransportEventType.Connected,
                Connection = connection,
            });

        public void EnqueueData(GameplayConnectionID connection, byte[] data) =>
            events.Enqueue(new GameplayNetworkTransportEvent
            {
                Type = GameplayNetworkTransportEventType.Data,
                Connection = connection,
                Data = data,
            });
    }
}
