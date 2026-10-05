using System;
using System.Linq;

namespace Test;

public class GameplayPartitionTests
{
    private GameplayMachine authority = null!;
    private GameplayMachine? client;
    private readonly GameplayMachineSettings settings = new()
    {
        Serializers = new List<IGMSerializer> { new TestCommon.MyCustomObjectSurrogate() },
    };

    [SetUp] public void Setup()
    {
        authority = GameplayMachineBase.SpawnGameplayMachine(null, settings, TestGMModule.Instance);
        authority.GetGame();
    }
    [TearDown] public void Cleanup()
    {
        if (client != null) GameplayMachineBase.RemoveGameplayMachine(client.MachineKey);
        GameplayMachineBase.RemoveGameplayMachine(authority.MachineKey);
        client = null;
    }

    private sealed class Room : IODScene
    {
        public void Create(GameplayMachine.GameplayMachineProxy machine, GameplaySceneInstance instance)
        {
            var monster = machine.CreateGameplayObject<Monster>();
            monster.Name = "Room root";
            var id = Guid.Parse("10000000-0000-0000-0000-000000000001");
            instance.Register(id, monster);
            instance.SetRoot(id);
        }
    }

    [Test] public void SceneRootsAreIndependentAndMovedObjectsSurviveSceneDestruction()
    {
        var root = authority.GetRoot();
        var first = authority.CreateScene(new Room());
        var second = authority.CreateScene(new Room());
        Monster carried;
        using (authority.UsePartition(first.MainPartitionID)) carried = authority.CreateGameplayObject<Monster>();
        var id = carried.ObjectID;
        authority.MoveGameplayObject(id, authority.PersistentPartitionID);
        authority.DestroyScene(first.ID);
        Assert.Multiple(() =>
        {
            Assert.That(authority.GetRoot().ObjectID, Is.EqualTo(root.ObjectID));
            Assert.That(authority.ContainsGameplayObject(id), Is.True);
            Assert.That(first.Root, Is.Null);
            Assert.That(second.Root, Is.Not.Null);
            Assert.That(authority.Scenes.Count(), Is.EqualTo(1));
        });
    }

    [Test] public void UnloadReloadPreservesReferenceIdentityAndNotifiesWithoutMutation()
    {
        var room = authority.CreateScene(new Room());
        var target = room.Root!.Value.Cast<Monster>()!.Value;
        var game = authority.GetGame();
        game.A = target;
        game.Units.Add(target);
        game.IDUnits.Add(7, target);
        int fields = 0, sets = 0, maps = 0, items = 0;
        var handles = new[] { game.AfterAChanged.Bind(_ => fields++), game.UnitsBinder.BindChanged(_ => sets++),
            game.IDUnitsBinder.BindChanged(_ => maps++), game.UnitsBinder.BindItemChanged(_ => items++) };
        byte[] before = authority.SavePartition(authority.PersistentPartitionID);
        authority.UnloadPartition(room.MainPartitionID);
        Assert.That(game.A, Is.Null);
        Assert.That(authority.ExistsGameplayObject(target.ObjectID), Is.True);
        byte[] during = authority.SavePartition(authority.PersistentPartitionID);
        authority.LoadPartition(room.MainPartitionID);
        Assert.Multiple(() =>
        {
            Assert.That(game.A!.Value.ObjectID, Is.EqualTo(target.ObjectID));
            Assert.That(fields, Is.EqualTo(2));
            Assert.That(sets, Is.EqualTo(2));
            Assert.That(maps, Is.EqualTo(2));
            Assert.That(items, Is.Zero);
            Assert.That(during, Is.EqualTo(before));
        });
        foreach (var handle in handles) handle.Return();
    }

    [Test] public void SaveRestoresUnloadedPartitionAndSceneRoot()
    {
        var room = authority.CreateScene(new Room());
        var id = room.RootObjectID;
        var game = authority.GetGame();
        game.A = room.Root!.Value.Cast<Monster>();
        authority.UnloadPartition(room.MainPartitionID);
        byte[] save = GameplayMachineBase.SaveGameplayMachine(authority);
        client = GameplayMachineBase.LoadGameplayMachine(new MachineKey { ID = 912345 }, settings, save, TestGMModule.Instance);
        Assert.That(client.GetGame().A, Is.Null);
        Assert.That(client.GetPartition(room.MainPartitionID).IsLoaded, Is.False);
        client.LoadPartition(room.MainPartitionID);
        Assert.That(client.GetScene(room.ID).Root!.Value.ObjectID, Is.EqualTo(id));
        Assert.That(client.GetGame().A!.Value.ObjectID, Is.EqualTo(id));
    }

    [Test] public void DeletingTargetCleansReferencesInArchivedOwners()
    {
        var room = authority.CreateScene(new Room());
        var player = authority.CreateGameplayObject<Player>();
        var game = authority.GetGame();
        var root = authority.CreateGameplayObject<Game>();
        authority.SetRoot(root);
        game.Player = player;
        authority.MoveGameplayObject(game.ObjectID, room.MainPartitionID);
        authority.UnloadPartition(room.MainPartitionID);
        byte[] save = GameplayMachineBase.SaveGameplayMachine(authority);
        client = GameplayMachineBase.LoadGameplayMachine(new MachineKey { ID = 912345 }, settings, save, TestGMModule.Instance);
        client.DeleteGameplayObject(player.ObjectID);
        client.LoadPartition(room.MainPartitionID);
        var restored = new Game { Machine = client, ObjectID = game.ObjectID };
        Assert.That(restored.Player, Is.Null);
    }

    [Test] public void FollowOwnerMovesTogetherAndRejectsCycles()
    {
        var room = authority.CreateScene(new Room());
        var owner = authority.CreateGameplayObject<Monster>();
        var item = authority.CreateGameplayObject<Monster>();
        authority.SetPartitionOwner(item.ObjectID, owner.ObjectID);
        authority.MoveGameplayObject(owner.ObjectID, room.MainPartitionID);
        Assert.That(authority.GetGameplayObjectPartition(item.ObjectID), Is.EqualTo(room.MainPartitionID));
        Assert.Throws<InvalidOperationException>(() => authority.SetPartitionOwner(owner.ObjectID, item.ObjectID));
        Assert.Throws<InvalidOperationException>(() => authority.UnloadPartition(authority.PersistentPartitionID));
        Assert.Throws<InvalidOperationException>(() => authority.MoveGameplayObject(authority.GetRoot().ObjectID, room.MainPartitionID));
    }

    private void Connect()
    {
        InMemoryGameplayNetworkTransport.CreatePair(out var server, out var proxy);
        authority.EnableNetworking(server);
        client = GameplayMachineBase.CreateClientProxy(null, settings, TestGMModule.Instance);
        client.ConnectNetworking(proxy);
        Pump();
        Assert.That(client.NetworkState, Is.EqualTo(GameplayNetworkState.Ready));
    }

    [Test] public void InvalidFollowOwnerMoveLeavesEntireGroupInPlace()
    {
        var room = authority.CreateScene(new Room());
        Monster owner;
        using (authority.UsePartition(room.MainPartitionID)) owner = authority.CreateGameplayObject<Monster>();
        authority.SetPartitionOwner(room.RootObjectID, owner.ObjectID);
        Assert.Throws<InvalidOperationException>(() => authority.MoveGameplayObject(owner.ObjectID, authority.PersistentPartitionID));
        Assert.That(authority.GetGameplayObjectPartition(owner.ObjectID), Is.EqualTo(room.MainPartitionID));
        Assert.That(authority.GetGameplayObjectPartition(room.RootObjectID), Is.EqualTo(room.MainPartitionID));
    }

    private void Pump() { for (int i = 0; i < 8; i++) { authority.UpdateNetwork(); client!.UpdateNetwork(); } }

    private sealed class MemoryStore : IGameplayPartitionStore
    {
        public readonly Dictionary<string, byte[]> Files = new();
        public readonly List<string> Reads = new();
        public readonly List<string> Writes = new();
        public byte[] Read(string key) { Reads.Add(key); return Files[key]; }
        public void Write(string key, byte[] bytes) { Writes.Add(key); Files[key] = (byte[])bytes.Clone(); }
    }

    [Test] public void PartitionStoreLoadsOnlyRequestedPayloadsAndKeepsArchivedReferences()
    {
        var room = authority.CreateScene(new Room());
        var other = authority.CreateScene(new Room());
        var game = authority.GetGame();
        game.A = room.Root!.Value.Cast<Monster>();
        var store = new MemoryStore();
        authority.SavePartitionedWorld(store);
        client = GameplayMachineBase.LoadPartitionedGameplayMachine(new MachineKey { ID = 912345 }, settings, store, false, TestGMModule.Instance);
        Assert.That(store.Reads.Count, Is.EqualTo(2), "Only manifest and Persistent payload should be read");
        Assert.That(client.GetGame().A, Is.Null);
        client.LoadPartition(room.MainPartitionID);
        Assert.That(store.Reads.Count, Is.EqualTo(3));
        Assert.That(client.GetGame().A!.Value.ObjectID, Is.EqualTo(room.RootObjectID));
        Assert.That(client.GetPartition(other.MainPartitionID).IsLoaded, Is.False);
        byte[] snapshot = GameplayMachineBase.SaveGameplayMachine(client);
        Assert.That(snapshot.Length, Is.GreaterThan(0));
    }

    [Test] public void NetworkMovementDoesNotDestroyAndRecreateVisibleObject()
    {
        var room = authority.CreateScene(new Room());
        var obj = authority.CreateGameplayObject<Monster>();
        Connect();
        int creations = 0, deletions = 0;
        var a = client!.BindMachineEvent<AfterGameplayObjectCreatedParam>(_ => creations++);
        var b = client.BindMachineEvent<AfterGameplayObjectDeletedParam>(_ => deletions++);
        authority.MoveGameplayObject(obj.ObjectID, room.MainPartitionID);
        Pump();
        Assert.That(client.GetGameplayObjectPartition(obj.ObjectID), Is.EqualTo(room.MainPartitionID));
        Assert.That(creations + deletions, Is.Zero);
        a.Return(); b.Return();
    }

    [Test] public void TransientObjectsDoNotEnterSave()
    {
        var transient = authority.CreatePartition(PartitionKind.Transient, "effects");
        Monster effect;
        using (authority.UsePartition(transient)) effect = authority.CreateGameplayObject<Monster>();
        effect.Name = "Temporary";
        authority.UnloadPartition(transient);
        authority.LoadPartition(transient);
        Assert.That(new Monster { Machine = authority, ObjectID = effect.ObjectID }.Name, Is.EqualTo("Temporary"));
        authority.UnloadPartition(transient);
        byte[] save = GameplayMachineBase.SaveGameplayMachine(authority);
        client = GameplayMachineBase.LoadGameplayMachine(new MachineKey { ID = 912345 }, settings, save, TestGMModule.Instance);
        Assert.That(client.ExistsGameplayObject(effect.ObjectID), Is.False);
        Assert.That(client.GetPartition(transient).ObjectIDs, Is.Empty);
        Assert.That(client.GetPartition(transient).IsLoaded, Is.True);
    }

    [Test] public void CheckpointWritesOnlyChangedPartitionAndUnloadDoesNotDirtyOwner()
    {
        var room = authority.CreateScene(new Room());
        var game = authority.GetGame();
        game.A = room.Root!.Value.Cast<Monster>();
        var store = new MemoryStore();
        authority.SavePartitionedWorld(store);
        store.Writes.Clear();
        authority.UnloadPartition(room.MainPartitionID);
        authority.SavePartitionedWorld(store);
        Assert.That(store.Writes, Is.EqualTo(new[] { "world.gmw" }));
        store.Writes.Clear();
        authority.LoadPartition(room.MainPartitionID);
        var monster = room.Root!.Value.Cast<Monster>()!.Value;
        monster.Name = "Changed";
        authority.SavePartitionedWorld(store);
        Assert.That(store.Writes.Count, Is.EqualTo(2));
        Assert.That(store.Writes[0], Does.EndWith($".{room.MainPartitionID.Value}.gmp"));
    }

    [Test] public void UnloadedCreationScopeRejectsNewObjectsWithoutAllocatingThem()
    {
        var room = authority.CreateScene(new Room());
        using (authority.UsePartition(room.MainPartitionID))
        {
            authority.UnloadPartition(room.MainPartitionID);
            Assert.Throws<InvalidOperationException>(() => authority.CreateGameplayObject<Monster>());
        }
    }

    [Test] public void NetworkInterestUnloadReloadAndMovementPreserveIDsAndCallbacks()
    {
        var room = authority.CreateScene(new Room());
        var target = room.Root!.Value.Cast<Monster>()!.Value;
        var game = authority.GetGame();
        game.A = target;
        Connect();
        int changes = 0;
        var binding = client!.GetGame().AfterAChanged.Bind(_ => changes++);
        var player = authority.GetPlayerControllers().Single(p => !p.ObjectID.Equals(authority.LocalPlayerController!.Value.ObjectID));
        authority.SetPartitionInterest(player, Array.Empty<PartitionID>());
        Pump();
        Assert.That(client.GetGame().A, Is.Null);
        Assert.That(changes, Is.EqualTo(1));
        authority.SetPartitionInterest(player, new[] { room.MainPartitionID });
        Pump();
        Assert.That(client.GetGame().A!.Value.ObjectID, Is.EqualTo(target.ObjectID));
        Assert.That(changes, Is.EqualTo(2));
        authority.UnloadPartition(room.MainPartitionID);
        Pump();
        Assert.That(client.GetGame().A, Is.Null);
        authority.LoadPartition(room.MainPartitionID);
        Pump();
        Assert.That(client.GetGame().A!.Value.ObjectID, Is.EqualTo(target.ObjectID));
        Assert.That(changes, Is.EqualTo(4));
        binding.Return();
    }
}
