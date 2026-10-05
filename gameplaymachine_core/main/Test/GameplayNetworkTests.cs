using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using TestGM_OD.Interfaces;

namespace Test;

internal struct DeleteGameplayObjectOut
{
}

internal struct DeleteGameplayObjectParam : ICheckableInterface<DeleteGameplayObjectOut>
{
    public GObjectID ObjectID;

    public ODCore.EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) => true;

    public void DoExecute(
        GameplayMachine.GameplayMachineProxy machine,
        ref DeleteGameplayObjectOut outResult)
    {
        machine.DeleteGameplayObject(ObjectID);
    }
}

internal struct SetNetworkOwnerOut
{
}

internal struct SetNetworkOwnerParam : ICheckableInterface<SetNetworkOwnerOut>
{
    public CommonGameplayObject Object;
    public CommonGameplayObject Owner;

    public ODCore.EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) => true;

    public void DoExecute(
        GameplayMachine.GameplayMachineProxy machine,
        ref SetNetworkOwnerOut outResult)
    {
        machine.SetNetworkOwner(Object, Owner);
    }
}

internal struct ApplyReplicatedBatchOut
{
}

internal struct ApplyReplicatedBatchParam : ICheckableInterface<ApplyReplicatedBatchOut>
{
    public Unit Unit;
    public GameAttribute Health;
    public GameAttribute Attack;

    public ODCore.EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) => true;

    public void DoExecute(
        GameplayMachine.GameplayMachineProxy machine,
        ref ApplyReplicatedBatchOut outResult)
    {
        Unit.Health = Health;
        Unit.Attack = Attack;
    }
}

internal struct ApplyReplicatedCollectionRoundTripOut
{
}

internal struct ApplyReplicatedCollectionRoundTripParam : ICheckableInterface<ApplyReplicatedCollectionRoundTripOut>
{
    public Game Game;
    public Unit FirstUnit;
    public Unit SecondUnit;

    public ODCore.EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) => true;

    public void DoExecute(
        GameplayMachine.GameplayMachineProxy machine,
        ref ApplyReplicatedCollectionRoundTripOut outResult)
    {
        Game.Units.Add(FirstUnit);
        Game.Units.Remove(FirstUnit);
        GMCore.Collections.Map<int, Unit> unitsByID = Game.IDUnits;
        unitsByID[1] = SecondUnit;
        unitsByID[1] = FirstUnit;
    }
}

internal sealed class PlayerLifecycleGameplayMachine : GameplayMachine
{
    public bool AuthorityStarted { get; private set; }
    public readonly List<PlayerController> JoinedPlayers = new();
    public readonly List<PlayerController> DisconnectedPlayers = new();

    protected override void OnAuthorityStarted()
    {
        AuthorityStarted = true;
    }

    protected override void OnPlayerJoin(PlayerController playerController)
    {
        JoinedPlayers.Add(playerController);
    }

    protected override void OnPlayerDisconnected(PlayerController playerController)
    {
        DisconnectedPlayers.Add(playerController);
    }
}

internal sealed class MutatingPlayerLifecycleGameplayMachine : GameplayMachine
{
    public int JoinedPlayerCount { get; private set; }

    protected override void OnPlayerJoin(PlayerController playerController)
    {
        CreateGameplayObject<Monster>();
        JoinedPlayerCount++;
    }
}

public class GameplayNetworkTests
{
    private GameplayMachineSettings m_settings;
    private GameplayMachine m_authority;
    private GameplayMachine m_client;
    private InMemoryGameplayNetworkTransport m_serverTransport;
    private InMemoryGameplayNetworkTransport m_clientTransport;

    [SetUp]
    public void SetUp()
    {
        m_settings = new GameplayMachineSettings
        {
            Serializers = new List<IGMSerializer>
            {
                new TestCommon.MyCustomObjectSurrogate(),
            },
        };
        m_authority = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance, GameplayRpcTestModule.Instance);
        Game game = m_authority.GetGame();
        Player player = m_authority.CreateGameplayObject<Player>();
        player.Name = "Authority Player";
        player.Attack = 7;
        player.PlayerSkill = 23;
        game.Player = player;

        Monster monster = m_authority.CreateGameplayObject<Monster>();
        monster.Name = "Initial Monster";
        monster.Health = 30;
        game.A = monster;

        InMemoryGameplayNetworkTransport.CreatePair(out m_serverTransport, out m_clientTransport);
        m_authority.EnableNetworking(m_serverTransport);
        m_client = GameplayMachineBase.CreateClientProxy(null, m_settings, TestGMModule.Instance, GameplayRpcTestModule.Instance);
        m_client.ConnectNetworking(m_clientTransport);
        PumpUntilReady();
        PredictAttackParam.ResetCounters();
    }

    [TearDown]
    public void TearDown()
    {
        m_client?.DisableNetworking();
        m_authority?.DisableNetworking();
    }

    [Test]
    public void InitialSnapshotAndAuthorityChangesReplicate()
    {
        Assert.That(m_authority.Role, Is.EqualTo(GameplayMachineRole.Authority));
        Assert.That(m_client.Role, Is.EqualTo(GameplayMachineRole.ClientProxy));
        Assert.That(m_client.GetRoot().ObjectID, Is.EqualTo(m_authority.GetRoot().ObjectID));
        Assert.That(m_client.GetGame().Player.Value.Name, Is.EqualTo("Authority Player"));
        Assert.That(m_client.GetGame().A.Value.Health, Is.EqualTo((GameAttribute)30));

        Player authorityPlayer = m_authority.GetGame().Player.Value;
        authorityPlayer.Attack = 19;
        m_authority.GetGame().Units.Add(m_authority.CreateGameplayObject<Monster>());
        Pump(2);

        Assert.That(m_client.GetGame().Player.Value.Attack, Is.EqualTo((GameAttribute)19));
        Assert.That(m_client.GetGame().Units.Count(), Is.EqualTo(1));
        Assert.That(m_client.NetworkRevision, Is.EqualTo(m_authority.NetworkRevision));
    }

    [Test]
    public void DelayedClientCoalescesDrivenCallbacksWithinOneReplicationFrame()
    {
        m_client.SetUseDelayedCallback(true);
        Player authorityPlayer = m_authority.GetGame().Player.Value;
        Player clientPlayer = m_client.GetGame().Player.Value;
        int healthChanged = 0;
        int attackChanged = 0;
        int statusChanged = 0;
        clientPlayer.AfterHealthChanged.Bind(_ => healthChanged++);
        clientPlayer.AfterAttackChanged.Bind(_ => attackChanged++);
        clientPlayer.AfterUnitStatusChanged.Bind(_ => statusChanged++);

        m_authority.Execute(new ApplyReplicatedBatchParam
        {
            Unit = authorityPlayer,
            Health = 100,
            Attack = 19,
        });
        Pump(2);

        Assert.Multiple(() =>
        {
            Assert.That(clientPlayer.Health, Is.EqualTo((GameAttribute)100));
            Assert.That(clientPlayer.Attack, Is.EqualTo((GameAttribute)19));
            Assert.That(healthChanged, Is.EqualTo(1));
            Assert.That(attackChanged, Is.EqualTo(1));
            Assert.That(statusChanged, Is.EqualTo(1));
        });
    }

    [Test]
    public void DelayedClientSuppressesNetZeroCollectionDeltasWithinOneReplicationFrame()
    {
        Player authorityPlayer = m_authority.GetGame().Player.Value;
        Monster authorityMonster = m_authority.GetGame().A.Value;
        m_authority.GetGame().IDUnits.Add(1, authorityPlayer);
        Pump(2);
        m_client.SetUseDelayedCallback(true);

        Game clientGame = m_client.GetGame();
        int setChanged = 0;
        int setItemChanged = 0;
        int mapChanged = 0;
        int mapItemChanged = 0;
        clientGame.UnitsBinder.BindChanged(_ => setChanged++);
        clientGame.UnitsBinder.BindItemChanged(_ => setItemChanged++);
        clientGame.IDUnitsBinder.BindChanged(_ => mapChanged++);
        clientGame.IDUnitsBinder.BindItemChanged(_ => mapItemChanged++);

        m_authority.Execute(new ApplyReplicatedCollectionRoundTripParam
        {
            Game = m_authority.GetGame(),
            FirstUnit = authorityPlayer,
            SecondUnit = authorityMonster,
        });
        Pump(2);

        Assert.Multiple(() =>
        {
            Assert.That(clientGame.Units.Count(), Is.Zero);
            Assert.That(clientGame.IDUnits[1].ObjectID, Is.EqualTo(authorityPlayer.ObjectID));
            Assert.That(setChanged, Is.Zero);
            Assert.That(setItemChanged, Is.Zero);
            Assert.That(mapChanged, Is.Zero);
            Assert.That(mapItemChanged, Is.Zero);
        });
    }

    [Test]
    public void InitialSnapshotAppliesWhenClientRequiresInterfaceMutations()
    {
        GameplayMachineSettings settings = m_settings;
        GameplayMachine authority = GameplayMachineBase.SpawnGameplayMachine(
            null, settings, TestGMModule.Instance, GameplayRpcTestModule.Instance);
        Player player = authority.CreateGameplayObject<Player>();
        player.Name = "Strict snapshot player";
        Game game = authority.GetGame();
        game.Player = player;

        InMemoryGameplayNetworkTransport.CreatePair(
            out InMemoryGameplayNetworkTransport serverTransport,
            out InMemoryGameplayNetworkTransport clientTransport);
        GameplayMachine client = GameplayMachineBase.CreateClientProxy(
            null, settings, TestGMModule.Instance, GameplayRpcTestModule.Instance);

        try
        {
            client.SetModifyDataInsideExecutionOnly(true);
            authority.EnableNetworking(serverTransport);
            client.ConnectNetworking(clientTransport);
            for (int i = 0; i < 20 && client.NetworkState != GameplayNetworkState.Ready; i++)
            {
                authority.UpdateNetwork();
                client.UpdateNetwork();
            }

            Assert.That(client.NetworkState, Is.EqualTo(GameplayNetworkState.Ready));
            Assert.That(client.GetGame().Player.Value.Name, Is.EqualTo("Strict snapshot player"));
        }
        finally
        {
            client.DisableNetworking();
            authority.DisableNetworking();
        }
    }

    [Test]
    public void PlayerJoinMayInitializeGameplayObjectsWhenStrictMutationsAreEnabled()
    {
        GameplayMachineSettings settings = m_settings;
        settings.GameplayMachineFactory = () => new MutatingPlayerLifecycleGameplayMachine();
        var authority = (MutatingPlayerLifecycleGameplayMachine)GameplayMachineBase.SpawnGameplayMachine(
            null, settings, TestGMModule.Instance, GameplayRpcTestModule.Instance);
        authority.SetModifyDataInsideExecutionOnly(true);

        InMemoryGameplayNetworkTransport.CreatePair(
            out InMemoryGameplayNetworkTransport serverTransport,
            out InMemoryGameplayNetworkTransport clientTransport);
        GameplayMachine client = GameplayMachineBase.CreateClientProxy(
            null, settings, TestGMModule.Instance, GameplayRpcTestModule.Instance);

        try
        {
            authority.EnableNetworking(serverTransport);
            client.ConnectNetworking(clientTransport);
            for (int i = 0; i < 20 && client.NetworkState != GameplayNetworkState.Ready; i++)
            {
                authority.UpdateNetwork();
                client.UpdateNetwork();
            }

            Assert.That(client.NetworkState, Is.EqualTo(GameplayNetworkState.Ready));
            Assert.That(authority.JoinedPlayerCount, Is.EqualTo(2));
        }
        finally
        {
            client.DisableNetworking();
            authority.DisableNetworking();
        }
    }

    [Test]
    public void PlayerInputUsesLocalControllerOnAuthorityAndCallingControllerForRpc()
    {
        PlayerInputCaptureParam.LastController = null;
        m_authority.Execute(new PlayerInputCaptureParam());
        Assert.That(PlayerInputCaptureParam.LastController?.ObjectID,
            Is.EqualTo(m_authority.GetLocalPlayerController().ObjectID));

        PlayerInputCaptureParam.LastController = null;
        m_client.Execute(new PlayerInputCaptureParam());
        Pump(2);

        Assert.That(PlayerInputCaptureParam.LastController?.ObjectID,
            Is.EqualTo(m_client.GetLocalPlayerController().ObjectID));
    }

    [Test]
    public void PlayerInputRejectsUnsupportedTransportMode()
    {
        CheckableInterfaceResult result = m_authority.Execute(new InvalidMulticastPlayerInputParam());

        Assert.That(result.Sussceeded, Is.False);
        Assert.That(result.ErrorMessage.ToString(), Does.Contain("Authority or Predictable"));
    }

    [Test]
    public void BuiltInPlayerInputUpdatesCallingPlayersPawnAndViewRange()
    {
        Pawn authorityPawn = m_authority.CreateGameplayObject<Pawn>();
        PumpUntil(() => m_client.ContainsGameplayObject(authorityPawn.ObjectID));

        PlayerController clientController = m_client.GetLocalPlayerController();
        PlayerController authorityController = m_authority.GetPlayerControllers()
            .Single(item => item.ObjectID.Equals(clientController.ObjectID));
        Pawn clientPawn = m_client.GetGameplayObject<Pawn>(authorityPawn.ObjectID);
        CheckableInterfaceResult result = m_authority.Execute(new PossessParam
        {
            PlayerController = authorityController,
            Pawn = authorityPawn,
        });
        Assert.That(result.Sussceeded, Is.True, result.ErrorMessage.Error);
        PumpUntil(() =>
            m_authority.GetNetworkOwner(authorityPawn)?.ObjectID.Equals(clientController.ObjectID) == true &&
            clientController.Pawn?.ObjectID.Equals(clientPawn.ObjectID) == true);

        var expected = new Transform(
            new Position(12, 3, -8),
            new Rotation(0, 0, 0, 1),
            new Float3(4, 5, 6));
        result = m_client.Execute(new PushPlayerTransformParam { Transform = expected });
        Assert.That(result.Sussceeded, Is.True, result.ErrorMessage.Error);
        PumpUntil(() => authorityPawn.Transform.Position == expected.Position);
        Assert.That(m_client.NetworkState, Is.EqualTo(GameplayNetworkState.Ready));

        result = m_client.Execute(new PushViewRangeParam { Range = 96 });
        Assert.That(result.Sussceeded, Is.True, result.ErrorMessage.Error);
        PumpUntil(() => m_authority.ContainsGameplayObject(clientController.ObjectID) &&
                        m_authority.GetGameplayObject<PlayerController>(clientController.ObjectID).ViewRange == 96);
        Pump(2);

        Transform actual = authorityPawn.Transform;
        Assert.Multiple(() =>
        {
            Assert.That(actual.Position, Is.EqualTo(expected.Position));
            Assert.That(actual.Rotation, Is.EqualTo(expected.Rotation));
            Assert.That(actual.Scale, Is.EqualTo(Float3.One));
            Assert.That(m_client.GetGameplayObject<Pawn>(authorityPawn.ObjectID).Transform, Is.EqualTo(actual));
            Assert.That(m_client.GetGameplayObject<PlayerController>(clientController.ObjectID).ViewRange,
                Is.EqualTo(96));
        });
    }

    [Test]
    public void BuiltInPlayerInputRejectsMissingPawnAndInvalidValues()
    {
        CheckableInterfaceResult result = m_authority.Execute(new PushPlayerTransformParam
        {
            Transform = Transform.Identity,
        });
        Assert.That(result.Sussceeded, Is.False);
        Assert.That(result.ErrorMessage.Error, Does.Contain("does not possess"));

        Assert.Multiple(() =>
        {
            Assert.That(m_authority.CanExecute(new PushViewRangeParam { Range = 0 }).Error,
                Does.Contain("greater than zero"));
            Assert.That(m_authority.CanExecute(new PushViewRangeParam { Range = float.NaN }).Error,
                Does.Contain("finite"));
        });
    }

    [Test]
    public void NoneReplicationKeepsCreatedObjectsOnAuthority()
    {
        NonReplicatedObject authorityOnly = m_authority.CreateGameplayObject<NonReplicatedObject>();

        Pump(2);
        Assert.Multiple(() =>
        {
            Assert.That(m_authority.ContainsGameplayObject(authorityOnly.ObjectID), Is.True);
            Assert.That(m_authority.GetGameplayObjectReplicationMode(authorityOnly),
                Is.EqualTo(GameplayObjectReplicationMode.None));
            Assert.That(m_client.ContainsGameplayObject(authorityOnly.ObjectID), Is.False);
        });
    }

    [Test]
    public void ReferenceCleanupReplicatesBeforeTargetDeletionEvent()
    {
        Monster target = m_authority.GetGame().A.Value;
        var clientEvents = new List<string>();
        var fieldBinding = m_client.GetGame().AfterAChanged.Bind(_ =>
        {
            Assert.That(m_client.GetGame().A, Is.Null);
            clientEvents.Add("field");
        });
        var deleteBinding = m_client.BindMachineEvent<AfterGameplayObjectDeletedParam>(deleted =>
        {
            if (deleted.ObjectID.Equals(target.ObjectID))
                clientEvents.Add("deleted");
        });

        m_authority.Execute(new DeleteGameplayObjectParam { ObjectID = target.ObjectID });
        Pump(2);

        fieldBinding.Return();
        deleteBinding.Return();
        Assert.Multiple(() =>
        {
            Assert.That(m_authority.GetGame().A, Is.Null);
            Assert.That(m_client.GetGame().A, Is.Null);
            Assert.That(m_client.ContainsGameplayObject(target.ObjectID), Is.False);
            Assert.That(clientEvents, Is.EqualTo(new[] { "field", "deleted" }));
        });
    }

    [Test]
    public void CollectionReferenceCleanupReplicatesAsRemoveDeltas()
    {
        Monster target = m_authority.GetGame().A.Value;
        m_authority.GetGame().Units.Add(target);
        m_authority.GetGame().IDUnits.Add(11, target);
        Pump(2);
        var changes = new List<string>();
        var setBinding = m_client.GetGame().UnitsBinder.BindItemChanged(change =>
            changes.Add($"set:{change.ChangeType}"));
        var mapBinding = m_client.GetGame().IDUnitsBinder.BindItemChanged(change =>
            changes.Add($"map:{change.ChangeType}:{change.Item}"));
        var deleteBinding = m_client.BindMachineEvent<AfterGameplayObjectDeletedParam>(deleted =>
        {
            if (deleted.ObjectID.Equals(target.ObjectID))
                changes.Add("deleted");
        });

        m_authority.Execute(new DeleteGameplayObjectParam { ObjectID = target.ObjectID });
        Pump(2);

        setBinding.Return();
        mapBinding.Return();
        deleteBinding.Return();
        Assert.Multiple(() =>
        {
            Assert.That(m_client.GetGame().Units, Is.Empty);
            Assert.That(m_client.GetGame().IDUnits.Count, Is.Zero);
            Assert.That(changes, Does.Contain("set:Remove"));
            Assert.That(changes, Does.Contain("map:Remove:11"));
            Assert.That(changes.Last(), Is.EqualTo("deleted"));
        });
    }

    [Test]
    public void SetItemChangedEventsReplicateToClient()
    {
        var changes = new List<CollectionItemChangedParam<Unit>>();
        int changedCount = 0;
        var itemBinding = m_client.GetGame().UnitsBinder.BindItemChanged(change => changes.Add(change));
        var changedBinding = m_client.GetGame().UnitsBinder.BindChanged(_ => changedCount++);
        Monster monster = m_authority.CreateGameplayObject<Monster>();

        m_authority.GetGame().Units.Add(monster);
        Pump(2);
        m_authority.GetGame().Units.Remove(monster);
        Pump(2);

        itemBinding.Return();
        changedBinding.Return();
        Assert.Multiple(() =>
        {
            Assert.That(changes.Select(change => change.ChangeType), Is.EqualTo(new[]
            {
                CollectionItemChangeType.Add,
                CollectionItemChangeType.Remove,
            }));
            Assert.That(changes.All(change => change.Item.ObjectID.Equals(monster.ObjectID)), Is.True);
            Assert.That(changedCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void MapItemChangedEventsReplicateToClient()
    {
        var changes = new List<CollectionItemChangedParam<int>>();
        int changedCount = 0;
        var itemBinding = m_client.GetGame().IDUnitsBinder.BindItemChanged(change => changes.Add(change));
        var changedBinding = m_client.GetGame().IDUnitsBinder.BindChanged(_ => changedCount++);
        Monster first = m_authority.CreateGameplayObject<Monster>();
        Monster second = m_authority.CreateGameplayObject<Monster>();

        GMCore.Collections.Map<int, Unit> authorityUnits = m_authority.GetGame().IDUnits;
        authorityUnits.Add(7, first);
        Pump(2);
        authorityUnits[7] = second;
        Pump(2);
        authorityUnits.Remove(7);
        Pump(2);

        itemBinding.Return();
        changedBinding.Return();
        Assert.Multiple(() =>
        {
            Assert.That(changes.Select(change => change.ChangeType), Is.EqualTo(new[]
            {
                CollectionItemChangeType.Add,
                CollectionItemChangeType.Changed,
                CollectionItemChangeType.Remove,
            }));
            Assert.That(changes.All(change => change.Item == 7), Is.True);
            Assert.That(changedCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void InitialSnapshotBroadcastsCreatedEventsAfterObjectsAreLoaded()
    {
        m_client.DisableNetworking();
        m_authority.DisableNetworking();

        InMemoryGameplayNetworkTransport.CreatePair(out m_serverTransport, out m_clientTransport);
        m_authority.EnableNetworking(m_serverTransport);
        m_client = GameplayMachineBase.CreateClientProxy(
            null, m_settings, TestGMModule.Instance, GameplayRpcTestModule.Instance);
        var created = new List<(GObjectID ID, string? PlayerName)>();
        var binding = m_client.BindMachineEvent<AfterGameplayObjectCreatedParam>(parameter =>
        {
            string playerName = parameter.ObjectID.Equals(m_authority.GetGame().Player.Value.ObjectID)
                ? m_client.GetGameplayObject<Player>(parameter.ObjectID).Name
                : null;
            created.Add((parameter.ObjectID, playerName));
        });

        m_client.ConnectNetworking(m_clientTransport);
        PumpUntilReady();
        binding.Return();

        GObjectID[] clientIDs = m_client.GetAllGameplayObjects()
            .Select(gameplayObject => gameplayObject.ObjectID)
            .OrderBy(id => id.ID)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(created.Select(item => item.ID).OrderBy(id => id.ID), Is.EqualTo(clientIDs));
            Assert.That(m_client.GetPlayerControllers().Select(item => item.ObjectID),
                Is.EqualTo(new[] { m_client.GetLocalPlayerController().ObjectID }));
            Assert.That(clientIDs, Does.Not.Contain(m_authority.GetLocalPlayerController().ObjectID));
            Assert.That(created.Single(item => item.ID.Equals(m_authority.GetGame().Player.Value.ObjectID)).PlayerName,
                Is.EqualTo("Authority Player"));
            Assert.That(m_client.GetRoot().ObjectID, Is.EqualTo(m_authority.GetRoot().ObjectID));
        });
    }

    [Test]
    public void AuthoritySceneCreationReplicatesAndClientCannotCreateScenes()
    {
        var machineRoot = m_authority.GetRoot().ObjectID;
        GameplaySceneInstance scene = m_authority.CreateScene(new NetworkTestScene());
        Pump(3);

        Monster authorityMonster = scene.Get<Monster>(NetworkTestScene.MonsterId);
        Monster clientMonster = m_client.GetGameplayObject<Monster>(authorityMonster.ObjectID);
        Assert.Multiple(() =>
        {
            Assert.That(clientMonster.ObjectID, Is.EqualTo(authorityMonster.ObjectID));
            Assert.That(clientMonster.Health, Is.EqualTo((GameAttribute)77));
            Assert.That(m_authority.GetRoot().ObjectID, Is.EqualTo(machineRoot));
            Assert.That(m_client.GetRoot().ObjectID, Is.EqualTo(machineRoot));
            Assert.That(m_client.GetScene(scene.ID).RootObjectID, Is.EqualTo(clientMonster.ObjectID));
            Assert.That(() => m_client.CreateScene(new NetworkTestScene()), Throws.InvalidOperationException);
        });
    }

    [Test]
    public void RootChangesReplicateAfterInitialSnapshot()
    {
        Game replacement = m_authority.CreateGameplayObject<Game>();
        m_authority.SetRoot(replacement);

        Pump(3);

        Assert.That(m_client.GetRoot().ObjectID, Is.EqualTo(replacement.ObjectID));
        Assert.That(m_client.GetGame().ObjectID, Is.EqualTo(replacement.ObjectID));
    }

    [Test]
    public void FragmentedSnapshotAndRpcMessagesAreReassembled()
    {
        m_client.DisableNetworking();
        m_authority.DisableNetworking();

        InMemoryGameplayNetworkTransport.CreatePair(out m_serverTransport, out m_clientTransport);
        GameplayNetworkOptions options = new GameplayNetworkOptions
        {
            FragmentPayloadBytes = 64,
        };
        m_authority.EnableNetworking(m_serverTransport, options);
        m_client = GameplayMachineBase.CreateClientProxy(null, m_settings, TestGMModule.Instance, GameplayRpcTestModule.Instance);
        m_client.ConnectNetworking(m_clientTransport, options);
        PumpUntilReady();

        Assert.That(m_client.GetGame().Player.Value.Name, Is.EqualTo("Authority Player"));
        CheckableInterfaceResult result = m_client.Execute(new AuthorityAddUnitsParam
        {
            Count = 4,
            Payload = new string('x', 256),
        });
        Pump(4);

        Assert.That(result.Sussceeded, Is.True, result.ErrorMessage.Error);
        Assert.That(m_client.GetGame().Units.Count(), Is.EqualTo(4));
    }

    [Test]
    public void ResourceCatalogMismatchRejectsConnectionBeforeSnapshot()
    {
        m_client.DisableNetworking();
        m_authority.DisableNetworking();

        var logger = new RecordingLogger();
        m_settings.Logger = logger;
        var authorityCatalog = new ResourceCatalogTestModule(11);
        var clientCatalog = new ResourceCatalogTestModule(22);
        m_authority = GameplayMachineBase.SpawnGameplayMachine(
            null, m_settings, TestGMModule.Instance, GameplayRpcTestModule.Instance, authorityCatalog);
        InMemoryGameplayNetworkTransport.CreatePair(out m_serverTransport, out m_clientTransport);
        m_authority.EnableNetworking(m_serverTransport);
        m_client = GameplayMachineBase.CreateClientProxy(
            null, m_settings, TestGMModule.Instance, GameplayRpcTestModule.Instance, clientCatalog);
        string disconnectReason = null;
        var stateBinding = m_client.BindMachineEvent<GameplayNetworkStateChangedParam>(parameter =>
        {
            if (parameter.NewState == GameplayNetworkState.Disabled)
                disconnectReason = parameter.Reason;
        });
        m_client.ConnectNetworking(m_clientTransport);

        Pump(4);
        stateBinding.Return();

        Assert.Multiple(() =>
        {
            Assert.That(m_authority.ResourceCatalogID, Is.EqualTo(11));
            Assert.That(m_client.ResourceCatalogID, Is.EqualTo(22));
            Assert.That(m_client.NetworkState, Is.EqualTo(GameplayNetworkState.Disabled));
            Assert.That(disconnectReason,
                Is.EqualTo("OD resource catalog mismatch. Authority=11, Client=22"));
            Assert.That(logger.Messages,
                Has.Some.Contains("Rejected gameplay network connection"));
            Assert.That(logger.Messages,
                Has.Some.Contains("OD resource catalog mismatch. Authority=11, Client=22"));
        });
    }

    [Test]
    public void DefaultValuesAndDeletedObjectsReplicate()
    {
        Player authorityPlayer = m_authority.GetGame().Player.Value;
        authorityPlayer.Name = null;

        Monster authorityMonster = m_authority.GetGame().A.Value;
        GObjectID monsterID = authorityMonster.ObjectID;
        authorityMonster.Delete();
        Pump(3);

        Assert.That(m_client.GetGame().Player.Value.Name, Is.Null);
        Assert.That(m_client.GetAllGameplayObjects().Any(item => item.ObjectID.Equals(monsterID)), Is.False);
    }

    [Test]
    public void FieldsMarkedNoneAreNotReplicated()
    {
        Player authorityPlayer = m_authority.GetGame().Player.Value;
        Player clientPlayer = m_client.GetGame().Player.Value;
        Assert.That(authorityPlayer.PlayerSkill, Is.EqualTo((GameAttribute)23));
        Assert.That(clientPlayer.PlayerSkill, Is.EqualTo(default(GameAttribute)));

        authorityPlayer.PlayerSkill = 41;
        authorityPlayer.Attack = 17;
        Pump(2);

        Assert.That(clientPlayer.PlayerSkill, Is.EqualTo(default(GameAttribute)));
        Assert.That(clientPlayer.Attack, Is.EqualTo((GameAttribute)17));
    }

    [Test]
    public void ReplicationFrameUsesCompactFieldToken()
    {
        Player player = m_authority.GetGame().Player.Value;
        var dirty = new Dictionary<GObjectID, Dictionary<ODFieldName, GameplayReplicationDirtyField>>
        {
            [player.ObjectID] = new Dictionary<ODFieldName, GameplayReplicationDirtyField>
            {
                [new ODFieldName { NameObject = Unit.FieldNames.Attack }] =
                    new GameplayReplicationDirtyField { SendFullValue = true },
            },
        };

        byte[] frame = GameplayReplicationFormat.WriteFrame(
            m_authority,
            1,
            Array.Empty<GObjectID>(),
            Array.Empty<GObjectID>(),
            dirty,
            rootChanged: false);

        Assert.That(frame.Length, Is.EqualTo(30));
    }

    [Test]
    public void ClientProxyExecutesNoneLocallyButRejectsDirectMutation()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            Player player = m_client.GetGame().Player.Value;
            player.Attack = 100;
        });

        CheckableInterfaceResult<CreateMonstersParamOut> result = m_client.Execute(
            new CreateMonstersParam { Count = 1 });
        Assert.That(result.Sussceeded, Is.True, result.ErrorMessage.Error);
        Assert.That(m_client.GetGame().Units.Count(), Is.EqualTo(1));
        Assert.That(m_authority.GetGame().Units.Count(), Is.Zero);
    }

    [Test]
    public void ClientLocalObjectsUseNegativeIDsWithoutCollidingWithAuthorityObjects()
    {
        CheckableInterfaceResult<CreateMonstersParamOut> localResult = m_client.Execute(
            new CreateMonstersParam { Count = 2 });
        Unit[] localMonsters = m_client.GetGame().Units.ToArray();

        Monster authorityFirst = m_authority.CreateGameplayObject<Monster>();
        Monster authoritySecond = m_authority.CreateGameplayObject<Monster>();
        m_authority.GetGame().Units.Add(authorityFirst);
        m_authority.GetGame().Units.Add(authoritySecond);
        Pump(3);

        Assert.Multiple(() =>
        {
            Assert.That(localResult.Sussceeded, Is.True, localResult.ErrorMessage.Error);
            Assert.That(localMonsters.Select(item => item.ObjectID.ID), Is.EqualTo(new[] { -1, -2 }));
            Assert.That(authorityFirst.ObjectID.ID, Is.GreaterThan(0));
            Assert.That(authoritySecond.ObjectID.ID, Is.GreaterThan(0));
            Assert.That(m_client.NetworkState, Is.EqualTo(GameplayNetworkState.Ready));
            Assert.That(m_client.GetGame().Units.Count(), Is.EqualTo(4));
            Assert.That(m_client.GetGame().Units.Select(item => item.ObjectID),
                Does.Contain(authorityFirst.ObjectID).And.Contain(authoritySecond.ObjectID));
            Assert.That(m_client.GetAllGameplayObjects().Count(item => item.ObjectID.ID < 0), Is.EqualTo(2));
            Assert.That(m_client.GetAllGameplayObjects().Select(item => item.ObjectID).Distinct().Count(),
                Is.EqualTo(m_client.GetAllGameplayObjects().Count()));
        });
    }

    [Test]
    public void ClientLocalObjectIDsCannotCrossSerializationBoundary()
    {
        CheckableInterfaceResult<CreateMonstersParamOut> result = m_client.Execute(
            new CreateMonstersParam { Count = 1 });
        Unit localMonster = m_client.GetGame().Units.Single();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            GameplayMachineBase.SaveGameplayMachine(m_client));

        Assert.That(result.Sussceeded, Is.True, result.ErrorMessage.Error);
        Assert.That(localMonster.ObjectID.ID, Is.LessThan(0));
        Assert.That(exception.Message, Does.Contain("Client-local GameplayObject ID"));
    }

    [Test]
    public void AuthorityRpcExecutesOnAuthorityAndReplicatesResultingState()
    {
        CheckableInterfaceResult result = m_client.Execute(new AuthorityAddUnitsParam { Count = 3 });

        Pump(3);

        Assert.That(result.Sussceeded, Is.True, result.ErrorMessage.Error);
        Assert.That(m_authority.GetGame().Units.Count(), Is.EqualTo(3));
        Assert.That(m_client.GetGame().Units.Count(), Is.EqualTo(3));
    }

    [Test]
    public void GameplayInterfaceOutputExecutesOnlyOnAuthority()
    {
        AuthorityOutputParam.ServerExecutionCount = 0;
        CheckableInterfaceResult<AuthorityOutputResult> authorityResult =
            m_authority.Execute(new AuthorityOutputParam { Value = 17 });

        CheckableInterfaceResult<AuthorityOutputResult> clientResult =
            m_client.Execute(new AuthorityOutputParam { Value = 29 });
        Assert.Multiple(() =>
        {
            Assert.That(authorityResult.Sussceeded, Is.True, authorityResult.ErrorMessage.Error);
            Assert.That(authorityResult.Result.Value, Is.EqualTo(17));
            Assert.That(clientResult.Sussceeded, Is.False);
            Assert.That(clientResult.ErrorMessage.ToString(), Does.Contain("Authority"));
            Assert.That(AuthorityOutputParam.ServerExecutionCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void MulticastEventExecutesOnAuthorityAndEveryClient()
    {
        int authorityValue = 0;
        int clientValue = 0;
        var authorityBinding = m_authority.BindMachineEvent<MulticastCounterEvent>(value => authorityValue += value.Value);
        var clientBinding = m_client.BindMachineEvent<MulticastCounterEvent>(value => clientValue += value.Value);

        m_authority.BroadCastMachineEvent(new MulticastCounterEvent { Value = 7 });
        Pump(1);

        Assert.Multiple(() =>
        {
            Assert.That(authorityValue, Is.EqualTo(7));
            Assert.That(clientValue, Is.EqualTo(7));
            Assert.Throws<InvalidOperationException>(() =>
                m_client.BroadCastMachineEvent(new MulticastCounterEvent { Value = 1 }));
        });
        authorityBinding.Return();
        clientBinding.Return();
    }

    [Test]
    public void PredictableInterfacePredictsImmediatelyAndConfirmsWithInvocationSnapshot()
    {
        PredictAttackParam param = new PredictAttackParam
        {
            ExpectedAttack = 7,
            ResultAttack = 8,
        };

        CheckableInterfaceResult result = m_client.Execute(param);

        Assert.Multiple(() =>
        {
            Assert.That(result.Sussceeded, Is.True, result.ErrorMessage.Error);
            Assert.That(m_client.GetGame().Player.Value.Attack, Is.EqualTo((GameAttribute)8));
            Assert.That(m_authority.GetGame().Player.Value.Attack, Is.EqualTo((GameAttribute)7));
            Assert.That(PredictAttackParam.PredictionCount, Is.EqualTo(1));
            Assert.That(PredictAttackParam.SuccessCount, Is.Zero);
            Assert.That(PredictAttackParam.FailureCount, Is.Zero);
        });

        param.ExpectedAttack = 999;
        param.ResultAttack = 1000;
        Pump(2);

        Assert.Multiple(() =>
        {
            Assert.That(m_authority.GetGame().Player.Value.Attack, Is.EqualTo((GameAttribute)8));
            Assert.That(m_client.GetGame().Player.Value.Attack, Is.EqualTo((GameAttribute)8));
            Assert.That(PredictAttackParam.SuccessCount, Is.EqualTo(1));
            Assert.That(PredictAttackParam.FailureCount, Is.Zero);
            Assert.That(PredictAttackParam.LastCallbackExpectedAttack, Is.EqualTo(7));
        });
    }

    [Test]
    public void PredictableInterfaceRollsBackWhenAuthorityRejectsIt()
    {
        CheckableInterfaceResult<SetLocalAttackResult> localResult =
            m_client.Execute(new SetLocalAttackParam { Attack = 20 });
        PredictAttackParam param = new PredictAttackParam
        {
            ExpectedAttack = 20,
            ResultAttack = 21,
        };

        CheckableInterfaceResult result = m_client.Execute(param);

        Assert.Multiple(() =>
        {
            Assert.That(localResult.Sussceeded, Is.True, localResult.ErrorMessage.Error);
            Assert.That(result.Sussceeded, Is.True, result.ErrorMessage.Error);
            Assert.That(m_client.GetGame().Player.Value.Attack, Is.EqualTo((GameAttribute)21));
            Assert.That(PredictAttackParam.PredictionCount, Is.EqualTo(1));
        });

        param.ExpectedAttack = 999;
        Pump(2);

        Assert.Multiple(() =>
        {
            Assert.That(m_authority.GetGame().Player.Value.Attack, Is.EqualTo((GameAttribute)7));
            Assert.That(m_client.GetGame().Player.Value.Attack, Is.EqualTo((GameAttribute)20));
            Assert.That(PredictAttackParam.SuccessCount, Is.Zero);
            Assert.That(PredictAttackParam.FailureCount, Is.EqualTo(1));
            Assert.That(PredictAttackParam.LastCallbackExpectedAttack, Is.EqualTo(20));
        });
    }

    [Test]
    public void PredictableInterfaceExecutesNormallyOnAuthorityWithoutClientCallbacks()
    {
        CheckableInterfaceResult result = m_authority.Execute(new PredictAttackParam
        {
            ExpectedAttack = 7,
            ResultAttack = 8,
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Sussceeded, Is.True, result.ErrorMessage.Error);
            Assert.That(m_authority.GetGame().Player.Value.Attack, Is.EqualTo((GameAttribute)8));
            Assert.That(PredictAttackParam.PredictionCount, Is.Zero);
            Assert.That(PredictAttackParam.SuccessCount, Is.Zero);
            Assert.That(PredictAttackParam.FailureCount, Is.Zero);
        });
    }

    [Test]
    public void DisablingNetworkingKeepsAuthorityMachineAlive()
    {
        GObjectID playerID = m_authority.GetGame().Player.Value.ObjectID;
        m_authority.DisableNetworking();

        Assert.That(m_authority.Role, Is.EqualTo(GameplayMachineRole.Authority));
        Assert.That(m_authority.IsNetworkingEnabled, Is.False);
        Player player = m_authority.GetGame().Player.Value;
        Assert.That(player.ObjectID, Is.EqualTo(playerID));
        player.Attack = 41;
        Assert.That(m_authority.GetGame().Player.Value.Attack, Is.EqualTo((GameAttribute)41));
    }

    private void PumpUntilReady()
    {
        for (int i = 0; i < 10 && m_client.NetworkState != GameplayNetworkState.Ready; i++)
        {
            Pump(1);
        }
        Assert.That(m_client.NetworkState, Is.EqualTo(GameplayNetworkState.Ready));
    }

    [TestCase(GameplayPredictionMode.OnlySuccess, true, 1, 0)]
    [TestCase(GameplayPredictionMode.OnlyFailure, false, 0, 1)]
    public void PredictionModeOnlyReturnsTheSelectedOutcome(
        GameplayPredictionMode mode,
        bool authorityAccepts,
        int expectedSuccessCallbacks,
        int expectedFailureCallbacks)
    {
        PredictAttackParam.ConfiguredMode = mode;
        int expectedAttack = 7;
        if (!authorityAccepts)
        {
            m_client.Execute(new SetLocalAttackParam { Attack = 20 });
            expectedAttack = 20;
        }

        CheckableInterfaceResult result = m_client.Execute(new PredictAttackParam
        {
            ExpectedAttack = expectedAttack,
            ResultAttack = expectedAttack + 1,
        });
        Pump(3);

        Assert.Multiple(() =>
        {
            Assert.That(result.Sussceeded, Is.True, result.ErrorMessage.Error);
            Assert.That(PredictAttackParam.PredictionCount, Is.EqualTo(1));
            Assert.That(PredictAttackParam.SuccessCount, Is.EqualTo(expectedSuccessCallbacks));
            Assert.That(PredictAttackParam.FailureCount, Is.EqualTo(expectedFailureCallbacks));
        });
    }

    private void PumpUntil(Func<bool> condition)
    {
        for (int i = 0; i < 20 && !condition(); i++)
            Pump(1);
        Assert.That(condition(), Is.True);
    }

    private void Pump(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            m_client.UpdateNetwork();
            m_authority.UpdateNetwork();
            m_client.UpdateNetwork();
        }
    }
}

internal sealed class NetworkTestScene : IODScene
{
    public static readonly Guid MonsterId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public void Create(GameplayMachine.GameplayMachineProxy machine, GameplaySceneInstance instance)
    {
        Monster monster = machine.CreateGameplayObject<Monster>();
        instance.Register(MonsterId, monster);
        monster.Name = "Scene Monster";
        monster.Health = 77;
        machine.GetGame().Units.Add(monster);
        instance.SetRoot(MonsterId);
    }
}

internal sealed class ResourceCatalogTestModule : IODModule, IODResourceCatalogNetworkModule
{
    public ResourceCatalogTestModule(ulong resourceCatalogID)
    {
        ResourceCatalogID = resourceCatalogID;
    }

    public ODModuleName Name => new() { Name = "ResourceCatalogTest" };
    public ulong ResourceCatalogID { get; }
    public IEnumerable<ODClassMeta> GetAllODClassMetas() => Array.Empty<ODClassMeta>();
    public IEnumerable<ODStructMeta> GetAllODStructMetas() => Array.Empty<ODStructMeta>();
    public IEnumerable<ODEventMeta> GetAllODEventMetas() => Array.Empty<ODEventMeta>();
    public IEnumerable<ODClassSaveMeta> GetAllODClassSaveMetas() => Array.Empty<ODClassSaveMeta>();
    public IEnumerable<ODFieldSaveMeta> GetAllODFieldSaveMetas() => Array.Empty<ODFieldSaveMeta>();
}

internal sealed class RecordingLogger : XLogger.Logger
{
    public List<string> Messages { get; } = new();

    protected override void OnLog(string builtContent) => Messages.Add(builtContent);
}

public class TcpGameplayNetworkTests
{
    [Test]
    public void TcpTransportSynchronizesAuthorityAndClientProxy()
    {
        GameplayMachineSettings settings = new GameplayMachineSettings
        {
            Serializers = new List<IGMSerializer>
            {
                new TestCommon.MyCustomObjectSurrogate(),
            },
        };
        GameplayMachine authority = GameplayMachineBase.SpawnGameplayMachine(null, settings, TestGMModule.Instance, GameplayRpcTestModule.Instance);
        Game game = authority.GetGame();
        Player player = authority.CreateGameplayObject<Player>();
        player.Name = "TCP Player";
        game.Player = player;

        int port = ReservePort();
        TcpGameplayNetworkTransport server = TcpGameplayNetworkTransport.CreateServer(
            new IPEndPoint(IPAddress.Loopback, port));
        TcpGameplayNetworkTransport clientTransport = TcpGameplayNetworkTransport.CreateClient("127.0.0.1", port);
        GameplayMachine client = GameplayMachineBase.CreateClientProxy(null, settings, TestGMModule.Instance, GameplayRpcTestModule.Instance);

        try
        {
            authority.EnableNetworking(server);
            client.ConnectNetworking(clientTransport);
            for (int i = 0; i < 400 && client.NetworkState != GameplayNetworkState.Ready; i++)
            {
                authority.UpdateNetwork();
                client.UpdateNetwork();
                Thread.Sleep(5);
            }

            Assert.That(client.NetworkState, Is.EqualTo(GameplayNetworkState.Ready));
            Assert.That(client.GetGame().Player.Value.Name, Is.EqualTo("TCP Player"));

            CheckableInterfaceResult result = client.Execute(new AuthorityAddUnitsParam { Count = 2 });
            for (int i = 0; i < 400 && client.GetGame().Units.Count() != 2; i++)
            {
                authority.UpdateNetwork();
                client.UpdateNetwork();
                Thread.Sleep(5);
            }

            Assert.That(result.Sussceeded, Is.True, result.ErrorMessage.Error);
            Assert.That(client.GetGame().Units.Count(), Is.EqualTo(2));
        }
        finally
        {
            client.DisableNetworking();
            authority.DisableNetworking();
        }
    }

    private static int ReservePort()
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

public class LiteNetLibGameplayNetworkTests
{
    [Test]
    public void OwnerReplicationFiltersFieldsPerConnection()
    {
        var settings = new GameplayMachineSettings();
        GameplayMachine authority = GameplayMachineBase.SpawnGameplayMachine(
            null, settings, TestGMModule.Instance, GameplayRpcTestModule.Instance);
        Game game = authority.GetGame();
        Player firstPlayer = authority.CreateGameplayObject<Player>();
        Monster fieldTarget = authority.CreateGameplayObject<Monster>();
        fieldTarget.Name = "Owner Field Target";
        game.Player = firstPlayer;
        game.B = fieldTarget;

        int port = ReserveUdpPort();
        LiteNetLibGameplayNetworkTransport server = LiteNetLibGameplayNetworkTransport.CreateServer(
            new IPEndPoint(IPAddress.Loopback, port));
        LiteNetLibGameplayNetworkTransport firstTransport =
            LiteNetLibGameplayNetworkTransport.CreateClient("127.0.0.1", port);
        LiteNetLibGameplayNetworkTransport secondTransport =
            LiteNetLibGameplayNetworkTransport.CreateClient("127.0.0.1", port);
        GameplayMachine firstClient = GameplayMachineBase.CreateClientProxy(
            null, settings, TestGMModule.Instance, GameplayRpcTestModule.Instance);
        GameplayMachine secondClient = GameplayMachineBase.CreateClientProxy(
            null, settings, TestGMModule.Instance, GameplayRpcTestModule.Instance);
        PlayerController? firstController = null;
        PlayerController? secondController = null;
        var serverOptions = new GameplayNetworkOptions
        {
            InitializePlayerController = (machine, _, controller, data) =>
            {
                if (data.Single() == 1)
                {
                    firstController = controller;
                    machine.SetNetworkOwner(game, controller);
                }
                else
                {
                    secondController = controller;
                }
            },
        };

        try
        {
            authority.EnableNetworking(server, serverOptions);
            firstClient.ConnectNetworking(firstTransport,
                new GameplayNetworkOptions { ConnectionData = new byte[] { 1 } });
            secondClient.ConnectNetworking(secondTransport,
                new GameplayNetworkOptions { ConnectionData = new byte[] { 2 } });
            PumpUntil(() => firstClient.NetworkState == GameplayNetworkState.Ready &&
                            secondClient.NetworkState == GameplayNetworkState.Ready,
                authority, firstClient, secondClient);

            Assert.Multiple(() =>
            {
                Assert.That(firstClient.LocalPlayerController?.ObjectID, Is.EqualTo(firstController?.ObjectID));
                Assert.That(secondClient.LocalPlayerController?.ObjectID, Is.EqualTo(secondController?.ObjectID));
                Assert.That(firstClient.ContainsGameplayObject(fieldTarget.ObjectID), Is.True);
                Assert.That(secondClient.ContainsGameplayObject(fieldTarget.ObjectID), Is.True);
                Assert.That(firstClient.GetGame().B?.ObjectID, Is.EqualTo(fieldTarget.ObjectID));
                Assert.That(secondClient.GetGame().B, Is.Null);
            });

            authority.Execute(new SetNetworkOwnerParam
            {
                Object = Common(authority, game.ObjectID),
                Owner = Common(authority, secondController!.Value.ObjectID),
            });
            PumpUntil(() => firstClient.GetGame().B is null &&
                            secondClient.GetGame().B?.ObjectID.Equals(fieldTarget.ObjectID) == true,
                authority, firstClient, secondClient);

            Assert.That(firstClient.GetGame().B, Is.Null);
            AuthorityAddUnitsParam.LastServerContext = null;
            firstClient.Execute(new AuthorityAddUnitsParam { Count = 0, Payload = string.Empty });
            PumpUntil(() => AuthorityAddUnitsParam.LastServerContext.HasValue,
                authority, firstClient, secondClient);
            Assert.That(AuthorityAddUnitsParam.LastServerContext?.PlayerController?.ObjectID,
                Is.EqualTo(firstController?.ObjectID));
        }
        finally
        {
            firstClient.DisableNetworking();
            secondClient.DisableNetworking();
            authority.DisableNetworking();
        }
    }

    [Test]
    public void UdpTransportSynchronizesAuthorityAndClientProxy()
    {
        GameplayMachineSettings settings = new GameplayMachineSettings
        {
            Serializers = new List<IGMSerializer>
            {
                new TestCommon.MyCustomObjectSurrogate(),
            },
        };
        GameplayMachine authority = GameplayMachineBase.SpawnGameplayMachine(null, settings, TestGMModule.Instance, GameplayRpcTestModule.Instance);
        Game game = authority.GetGame();
        Player player = authority.CreateGameplayObject<Player>();
        player.Name = "UDP Player";
        game.Player = player;

        int port = ReserveUdpPort();
        LiteNetLibGameplayNetworkTransport server = LiteNetLibGameplayNetworkTransport.CreateServer(
            new IPEndPoint(IPAddress.Loopback, port));
        LiteNetLibGameplayNetworkTransport clientTransport =
            LiteNetLibGameplayNetworkTransport.CreateClient("127.0.0.1", port);
        GameplayMachine client = GameplayMachineBase.CreateClientProxy(null, settings, TestGMModule.Instance, GameplayRpcTestModule.Instance);

        try
        {
            authority.EnableNetworking(server);
            client.ConnectNetworking(clientTransport);
            PumpUntil(() => client.NetworkState == GameplayNetworkState.Ready, authority, client);

            Assert.That(client.GetGame().Player.Value.Name, Is.EqualTo("UDP Player"));

            CheckableInterfaceResult result = client.Execute(new AuthorityAddUnitsParam { Count = 2 });
            PumpUntil(() => client.GetGame().Units.Count() == 2, authority, client);

            Assert.That(result.Sussceeded, Is.True, result.ErrorMessage.Error);
            Assert.That(client.GetGame().Units.Count(), Is.EqualTo(2));
        }
        finally
        {
            client.DisableNetworking();
            authority.DisableNetworking();
        }
    }

    [Test]
    public void PlayerLifecycleCallbacksRunOnConfiguredGameplayMachine()
    {
        var authoritySettings = new GameplayMachineSettings
        {
            GameplayMachineFactory = () => new PlayerLifecycleGameplayMachine(),
        };
        var authority = (PlayerLifecycleGameplayMachine)GameplayMachineBase.SpawnGameplayMachine(
            null, authoritySettings, TestGMModule.Instance, GameplayRpcTestModule.Instance);
        PlayerController authorityPlayer = authority.GetLocalPlayerController();
        Assert.That(authority.AuthorityStarted, Is.True);
        Assert.That(authority.JoinedPlayers.Select(item => item.ObjectID),
            Is.EqualTo(new[] { authorityPlayer.ObjectID }));
        InMemoryGameplayNetworkTransport.CreatePair(out InMemoryGameplayNetworkTransport server,
            out InMemoryGameplayNetworkTransport clientTransport);
        GameplayMachine client = GameplayMachineBase.CreateClientProxy(
            null, new GameplayMachineSettings(), TestGMModule.Instance, GameplayRpcTestModule.Instance);

        try
        {
            authority.EnableNetworking(server);
            client.ConnectNetworking(clientTransport);
            PumpUntil(() => client.NetworkState == GameplayNetworkState.Ready &&
                            authority.JoinedPlayers.Count == 2, authority, client);

            PlayerController remotePlayer = authority.JoinedPlayers[1];
            Assert.That(client.GetLocalPlayerController().ObjectID, Is.EqualTo(remotePlayer.ObjectID));
            Assert.That(authority.GetPlayerControllers().Count(), Is.EqualTo(2));

            client.DisableNetworking();
            PumpUntil(() => authority.DisconnectedPlayers.Count == 1, authority);

            Assert.That(authority.DisconnectedPlayers.Single().ObjectID, Is.EqualTo(remotePlayer.ObjectID));
            Assert.That(authority.GetPlayerControllers().Select(item => item.ObjectID),
                Is.EqualTo(new[] { authorityPlayer.ObjectID }));
        }
        finally
        {
            client.DisableNetworking();
            authority.DisableNetworking();
        }
    }

    [Test]
    public void DedicatedServerLogsRemotePlayerJoinAndDisconnect()
    {
        string logDirectory = Path.Combine(Path.GetTempPath(), "gameplaymachine-tests", Guid.NewGuid().ToString("N"));
        var messages = new List<string>();
        var logger = new GameplayLogger(new GameplayLoggerOptions
        {
            DirectoryPath = logDirectory,
            FilePrefix = "dedicated-lifecycle",
            ConsoleOutput = (_, line) => messages.Add(line),
        });

        try
        {
            var authoritySettings = new GameplayMachineSettings
            {
                GameplayMachineFactory = () => new PlayerLifecycleGameplayMachine(),
                LocalPlayerMode = GameplayLocalPlayerMode.Disabled,
                Logger = logger,
            };
            var authority = (PlayerLifecycleGameplayMachine)GameplayMachineBase.SpawnGameplayMachine(
                null, authoritySettings, TestGMModule.Instance, GameplayRpcTestModule.Instance);
            Assert.That(authority.JoinedPlayers, Is.Empty);
            InMemoryGameplayNetworkTransport.CreatePair(out InMemoryGameplayNetworkTransport server,
                out InMemoryGameplayNetworkTransport clientTransport);
            GameplayMachine client = GameplayMachineBase.CreateClientProxy(
                null, new GameplayMachineSettings(), TestGMModule.Instance, GameplayRpcTestModule.Instance);

            try
            {
                authority.EnableNetworking(server);
                client.ConnectNetworking(clientTransport);
                PumpUntil(() => client.NetworkState == GameplayNetworkState.Ready &&
                                authority.JoinedPlayers.Count == 1, authority, client);

                PlayerController remotePlayer = authority.JoinedPlayers.Single();
                Assert.That(messages, Has.Some.Contains(
                    $"Player joined. PlayerController={remotePlayer.ObjectID.ID}"));

                client.DisableNetworking();
                PumpUntil(() => authority.DisconnectedPlayers.Count == 1, authority);

                Assert.That(messages, Has.Some.Contains(
                    $"Player disconnected. PlayerController={remotePlayer.ObjectID.ID}"));
            }
            finally
            {
                client.DisableNetworking();
                authority.DisableNetworking();
            }
        }
        finally
        {
            logger.Dispose();
            if (Directory.Exists(logDirectory))
                Directory.Delete(logDirectory, true);
        }
    }

    [Test]
    public void PlayerControllerIsEngineManagedAndPossessionReplicatesToOwner()
    {
        GameplayMachine authority = GameplayMachineBase.SpawnGameplayMachine(
            null, new GameplayMachineSettings(), TestGMModule.Instance, GameplayRpcTestModule.Instance);
        Assert.Throws<InvalidOperationException>(() => authority.CreateGameplayObject<PlayerController>());
        Pawn pawn = authority.CreateGameplayObject<Pawn>();
        InMemoryGameplayNetworkTransport.CreatePair(out InMemoryGameplayNetworkTransport server,
            out InMemoryGameplayNetworkTransport clientTransport);
        GameplayMachine client = GameplayMachineBase.CreateClientProxy(
            null, new GameplayMachineSettings(), TestGMModule.Instance, GameplayRpcTestModule.Instance);

        try
        {
            authority.EnableNetworking(server);
            client.ConnectNetworking(clientTransport);
            PumpUntil(() => client.NetworkState == GameplayNetworkState.Ready, authority, client);

            PlayerController clientController = client.GetLocalPlayerController();
            Assert.Multiple(() =>
            {
                Assert.That(authority.GetPlayerControllers().Count(), Is.EqualTo(2));
                Assert.That(client.GetPlayerControllers().Select(item => item.ObjectID),
                    Is.EqualTo(new[] { clientController.ObjectID }));
            });
            Pawn clientPawn = client.GetGameplayObject<Pawn>(pawn.ObjectID);
            PlayerController authorityController = authority.GetPlayerControllers()
                .Single(item => item.ObjectID.Equals(clientController.ObjectID));
            CheckableInterfaceResult result = authority.Execute(new PossessParam
            {
                PlayerController = authorityController,
                Pawn = pawn,
            });
            Assert.That(result.Sussceeded, Is.True, result.ErrorMessage.Error);

            PumpUntil(() => clientController.Pawn?.ObjectID.Equals(pawn.ObjectID) == true &&
                            clientPawn.PlayerController?.ObjectID.Equals(clientController.ObjectID) == true,
                authority, client);

            Assert.Multiple(() =>
            {
                Assert.That(authorityController.Pawn?.ObjectID, Is.EqualTo(pawn.ObjectID));
                Assert.That(pawn.PlayerController?.ObjectID, Is.EqualTo(authorityController.ObjectID));
                Assert.That(authority.GetNetworkOwnerID(pawn), Is.EqualTo(authorityController.ObjectID));
            });

            result = authority.Execute(new UnPossessParam { PlayerController = authorityController });
            Assert.That(result.Sussceeded, Is.True, result.ErrorMessage.Error);
            PumpUntil(() => !clientController.Pawn.HasValue && !clientPawn.PlayerController.HasValue,
                authority, client);
            Assert.That(authority.GetNetworkOwner(pawn), Is.Null);
        }
        finally
        {
            client.DisableNetworking();
            authority.DisableNetworking();
        }
    }

    [Test]
    public void UdpCatalogMismatchReturnsDetailedDisconnectReason()
    {
        var logger = new RecordingLogger();
        var settings = new GameplayMachineSettings { Logger = logger };
        GameplayMachine authority = GameplayMachineBase.SpawnGameplayMachine(
            null, settings, TestGMModule.Instance, GameplayRpcTestModule.Instance,
            new ResourceCatalogTestModule(101));
        GameplayMachine client = GameplayMachineBase.CreateClientProxy(
            null, settings, TestGMModule.Instance, GameplayRpcTestModule.Instance,
            new ResourceCatalogTestModule(202));
        string disconnectReason = null;
        var stateBinding = client.BindMachineEvent<GameplayNetworkStateChangedParam>(parameter =>
        {
            if (parameter.NewState == GameplayNetworkState.Disabled)
                disconnectReason = parameter.Reason;
        });

        int port = ReserveUdpPort();
        LiteNetLibGameplayNetworkTransport server = LiteNetLibGameplayNetworkTransport.CreateServer(
            new IPEndPoint(IPAddress.Loopback, port));
        LiteNetLibGameplayNetworkTransport clientTransport =
            LiteNetLibGameplayNetworkTransport.CreateClient("127.0.0.1", port);

        try
        {
            authority.EnableNetworking(server);
            client.ConnectNetworking(clientTransport);
            PumpUntil(() => disconnectReason != null, authority, client);

            Assert.Multiple(() =>
            {
                Assert.That(client.NetworkState, Is.EqualTo(GameplayNetworkState.Disabled));
                Assert.That(disconnectReason,
                    Is.EqualTo("OD resource catalog mismatch. Authority=101, Client=202"));
                Assert.That(logger.Messages,
                    Has.Some.Contains("OD resource catalog mismatch. Authority=101, Client=202"));
            });
        }
        finally
        {
            stateBinding.Return();
            client.DisableNetworking();
            authority.DisableNetworking();
        }
    }

    private static void PumpUntil(Func<bool> condition, GameplayMachine authority, params GameplayMachine[] clients)
    {
        var failures = new List<string>();
        var diagnostics = clients.Select(client => client.BindMachineEvent<GameplayNetworkStateChangedParam>(change =>
        {
            if (!string.IsNullOrEmpty(change.Reason)) failures.Add(change.Reason);
        })).ToArray();
        for (int i = 0; i < 800 && !condition(); i++)
        {
            authority.UpdateNetwork();
            foreach (GameplayMachine client in clients)
                client.UpdateNetwork();
            Thread.Sleep(5);
        }
        foreach (var handle in diagnostics) handle.Return();
        Assert.That(condition(), Is.True, string.Join("; ", failures) + " States: " +
            string.Join(", ", clients.Select(client => $"{client.NetworkState}, revision={client.NetworkRevision}")));
    }

    private static CommonGameplayObject Common(GameplayMachine machine, GObjectID objectID) => new()
    {
        Machine = machine,
        ObjectID = objectID,
    };

    private static int ReserveUdpPort()
    {
        using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint).Port;
    }

}
