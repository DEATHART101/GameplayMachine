using System.Linq;
using ODCore;

namespace Test;

internal struct SpatialPawn : IGameplayObjectOperator, IODClass, IEquatable<SpatialPawn>
{
    public static readonly ODClassName ClassName = new() { NameObject = "Test.SpatialPawn" };
    public GameplayMachine Machine { get; set; }
    public GObjectID ObjectID { get; set; }
    public ODClassName ObjectClass => Machine.GetGameplayObjectClass(ObjectID);
    public ODClassName GetODClassName() => ClassName;
    public Transform Transform => Machine.GetGameplayObjectValue<Transform>(ObjectID, Actor.Fields.Transform);
    public bool IsA<T>() where T : struct, IODClass =>
        GameplayMachineBase.CanCastTo(ObjectClass, new T().GetODClassName());
    public T? Cast<T>() where T : struct, IGameplayObjectOperator, IODClass => Machine.Cast<T>(this);
    public void Delete() => Machine.DeleteGameplayObject(ObjectID);
    public void Return() => Delete();
    public bool Equals(SpatialPawn other) => ReferenceEquals(Machine, other.Machine) && ObjectID.Equals(other.ObjectID);
    public override bool Equals(object? obj) => obj is SpatialPawn other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Machine, ObjectID);
    public static implicit operator Pawn(SpatialPawn value) => new() { Machine = value.Machine, ObjectID = value.ObjectID };
}

internal struct RelevantActor : IGameplayObjectOperator, IODClass, IEquatable<RelevantActor>
{
    public static readonly ODClassName ClassName = new() { NameObject = "Test.RelevantActor" };
    public GameplayMachine Machine { get; set; }
    public GObjectID ObjectID { get; set; }
    public ODClassName ObjectClass => Machine.GetGameplayObjectClass(ObjectID);
    public ODClassName GetODClassName() => ClassName;
    public bool IsA<T>() where T : struct, IODClass =>
        GameplayMachineBase.CanCastTo(ObjectClass, new T().GetODClassName());
    public T? Cast<T>() where T : struct, IGameplayObjectOperator, IODClass => Machine.Cast<T>(this);
    public void Delete() => Machine.DeleteGameplayObject(ObjectID);
    public void Return() => Delete();
    public bool Equals(RelevantActor other) => ReferenceEquals(Machine, other.Machine) && ObjectID.Equals(other.ObjectID);
    public override bool Equals(object? obj) => obj is RelevantActor other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Machine, ObjectID);
}

internal struct InterestTestResult { }

internal struct MoveSpatialPawnParam : ICheckableInterface<InterestTestResult>
{
    public SpatialPawn Pawn;
    public Position Position;
    public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) => true;
    public void DoExecute(GameplayMachine.GameplayMachineProxy machine, ref InterestTestResult result)
    {
        machine.SetGameplayObjectValue(Pawn.ObjectID, Actor.Fields.Transform,
            new Transform(Position, Rotation.Identity, Float3.One));
    }
}

internal struct SetSpatialPawnTransformParam : ICheckableInterface<InterestTestResult>
{
    public SpatialPawn Pawn;
    public Transform Transform;
    public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) => true;
    public void DoExecute(GameplayMachine.GameplayMachineProxy machine, ref InterestTestResult result) =>
        machine.SetGameplayObjectValue(Pawn.ObjectID, Actor.Fields.Transform, Transform);
}

internal struct MoveRelevantActorParam : ICheckableInterface<InterestTestResult>
{
    public RelevantActor Actor;
    public Position Position;
    public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) => true;
    public void DoExecute(GameplayMachine.GameplayMachineProxy machine, ref InterestTestResult result)
    {
        machine.SetGameplayObjectValue(Actor.ObjectID, GMCore.Actor.Fields.Transform,
            new Transform(Position, Rotation.Identity, Float3.One));
    }
}

internal struct SetTestViewRangeParam : ICheckableInterface<InterestTestResult>
{
    public PlayerController PlayerController;
    public float Range;
    public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) => true;
    public void DoExecute(GameplayMachine.GameplayMachineProxy machine, ref InterestTestResult result) => PlayerController.ViewRange = Range;
}

internal sealed class TestInterestModule : IODModule
{
    public static readonly TestInterestModule Instance = new();
    public ODModuleName Name => new() { Name = "Test.Interest" };

    public IEnumerable<ODClassMeta> GetAllODClassMetas()
    {
        yield return new ODClassMeta
        {
            FromModule = Name,
            Name = SpatialPawn.ClassName,
            ODType = typeof(SpatialPawn),
            ReplicationMode = GameplayObjectReplicationMode.AllClients,
            RelevancyMode = GameplayObjectRelevancyMode.Spatial,
            PartitionMode = GameplayObjectPartitionMode.Spatial,
            DirectBaseClasses = new List<ODClassName> { Pawn.ClassName },
            Fields = new List<ODFieldMeta>(),
        };
        yield return new ODClassMeta
        {
            FromModule = Name,
            Name = RelevantActor.ClassName,
            ODType = typeof(RelevantActor),
            ReplicationMode = GameplayObjectReplicationMode.AllClients,
            RelevancyMode = GameplayObjectRelevancyMode.Spatial,
            PartitionMode = GameplayObjectPartitionMode.Manual,
            DirectBaseClasses = new List<ODClassName> { GMCore.Actor.ClassName },
            Fields = new List<ODFieldMeta>(),
        };
    }

    public IEnumerable<ODStructMeta> GetAllODStructMetas() => Array.Empty<ODStructMeta>();
    public IEnumerable<ODEventMeta> GetAllODEventMetas() => Array.Empty<ODEventMeta>();
    public IEnumerable<ODClassSaveMeta> GetAllODClassSaveMetas()
    {
        yield return new ODClassSaveMeta
        {
            StableID = ODSaveHash.Compute("Test.SpatialPawn"),
            StableName = "Test.SpatialPawn",
            ClassName = SpatialPawn.ClassName,
        };
        yield return new ODClassSaveMeta
        {
            StableID = ODSaveHash.Compute("Test.RelevantActor"),
            StableName = "Test.RelevantActor",
            ClassName = RelevantActor.ClassName,
        };
    }
    public IEnumerable<ODFieldSaveMeta> GetAllODFieldSaveMetas() => Array.Empty<ODFieldSaveMeta>();
}

public class GameplaySpatialRelevancyTests
{
    private GameplayMachine authority = null!;
    private GameplayMachine client = null!;
    private PlayerController remotePlayer;
    private PartitionID nearPartition;
    private PartitionID farPartition;

    [SetUp]
    public void SetUp()
    {
        var settings = new GameplayMachineSettings
        {
            Serializers = new List<IGMSerializer> { new TestCommon.MyCustomObjectSurrogate() },
            Interest = new GameplayInterestSettings
            {
                CellSize = 8,
                PartitionResolver = new GameplayGridPartitionResolver(16, includeY: false),
                LoadMargin = 0,
                UnloadDelaySeconds = 0,
                MaxLoadsPerUpdate = 8,
            },
        };
        authority = GameplayMachineBase.SpawnGameplayMachine(null, settings,
            TestGMModule.Instance, TestInterestModule.Instance);
        authority.GetGame();
        nearPartition = authority.CreatePartition(PartitionKind.Chunk, "near", coordinate: new GameplayPartitionCoordinate(0, 0));
        farPartition = authority.CreatePartition(PartitionKind.Chunk, "far", coordinate: new GameplayPartitionCoordinate(6, 0));

        InMemoryGameplayNetworkTransport.CreatePair(out var server, out var proxy);
        authority.EnableNetworking(server);
        client = GameplayMachineBase.CreateClientProxy(null, settings,
            TestGMModule.Instance, TestInterestModule.Instance);
        client.ConnectNetworking(proxy);
        PumpUntilReady();
        remotePlayer = authority.GetPlayerControllers()
            .Single(player => !player.ObjectID.Equals(authority.GetLocalPlayerController().ObjectID));
    }

    [TearDown]
    public void TearDown()
    {
        client?.DisableNetworking();
        authority?.DisableNetworking();
    }

    [Test]
    public void ActorTransformDrivesPartitionLoadingAndPerClientRelevancy()
    {
        SpatialPawn near = CreateAt(new Position(2, 0, 2));
        SpatialPawn far = CreateAt(new Position(100, 0, 2));
        SpatialPawn viewer = CreateAt(new Position(1, 0, 1));
        authority.Execute(new SetTestViewRangeParam { PlayerController = remotePlayer, Range = 20 });
        authority.Execute(new PossessParam { PlayerController = remotePlayer, Pawn = viewer });
        Pump(4);

        Assert.Multiple(() =>
        {
            Assert.That(authority.GetGameplayObjectPartition(near.ObjectID), Is.EqualTo(nearPartition));
            Assert.That(authority.GetPartition(nearPartition).IsLoaded, Is.True);
            Assert.That(authority.GetPartition(farPartition).IsLoaded, Is.False);
            Assert.That(client.ContainsGameplayObject(near.ObjectID), Is.True);
            Assert.That(client.ContainsGameplayObject(far.ObjectID), Is.False);
        });

        authority.Execute(new MoveSpatialPawnParam { Pawn = viewer, Position = new Position(100, 0, 2) });
        Pump(5);

        Assert.Multiple(() =>
        {
            Assert.That(authority.GetPartition(nearPartition).IsLoaded, Is.False);
            Assert.That(authority.GetPartition(farPartition).IsLoaded, Is.True);
            Assert.That(client.ContainsGameplayObject(near.ObjectID), Is.False);
            Assert.That(client.ContainsGameplayObject(far.ObjectID), Is.True);
        });
    }

    [Test]
    public void SpatialObjectAlwaysRemainsVisibleToItsNetworkOwner()
    {
        RelevantActor owned = authority.CreateGameplayObject<RelevantActor>();
        authority.Execute(new MoveRelevantActorParam { Actor = owned, Position = new Position(100, 0, 2) });
        SpatialPawn viewer = CreateAt(new Position(1, 0, 1));
        authority.Execute(new SetTestViewRangeParam { PlayerController = remotePlayer, Range = 20 });
        authority.Execute(new PossessParam { PlayerController = remotePlayer, Pawn = viewer });
        authority.SetNetworkOwner(owned, remotePlayer);
        Pump(4);

        Assert.That(client.ContainsGameplayObject(owned.ObjectID), Is.True);

        authority.ClearNetworkOwner(owned);
        Pump(3);

        Assert.That(client.ContainsGameplayObject(owned.ObjectID), Is.False);
    }

    [Test]
    public void TransformPatchPreservesUnchangedComponents()
    {
        SpatialPawn viewer = CreateAt(new Position(1, 0, 1));
        authority.Execute(new SetTestViewRangeParam { PlayerController = remotePlayer, Range = 20 });
        authority.Execute(new PossessParam { PlayerController = remotePlayer, Pawn = viewer });
        SpatialPawn target = CreateAt(new Position(2, 0, 2));
        var rotation = new Rotation(0.1f, 0.2f, 0.3f, 0.9f);
        var scale = new Float3(2, 3, 4);
        authority.Execute(new SetSpatialPawnTransformParam
        {
            Pawn = target,
            Transform = new Transform(new Position(2, 0, 2), rotation, scale),
        });
        Pump(4);

        authority.Execute(new SetSpatialPawnTransformParam
        {
            Pawn = target,
            Transform = new Transform(new Position(4, 0, 5), rotation, scale),
        });
        Pump(3);

        Transform replicated = client.GetGameplayObject<SpatialPawn>(target.ObjectID).Transform;
        Assert.Multiple(() =>
        {
            Assert.That(replicated.Position, Is.EqualTo(new Position(4, 0, 5)));
            Assert.That(replicated.Rotation, Is.EqualTo(rotation));
            Assert.That(replicated.Scale, Is.EqualTo(scale));
        });
    }

    [Test]
    public void GridResolverSupportsTwoAndThreeDimensionalCoordinates()
    {
        var twoDimensional = new GameplayGridPartitionResolver(16, includeY: false);
        var threeDimensional = new GameplayGridPartitionResolver(new Float3(16, 8, 16), includeY: true);
        PartitionID elevated = authority.CreatePartition(PartitionKind.Chunk, "elevated",
            coordinate: new GameplayPartitionCoordinate(default, 0, 2, 0));
        var results = new HashSet<PartitionID>();

        twoDimensional.Query(authority, new Position(1, 100, 1), 1, results);
        Assert.That(results, Does.Contain(nearPartition).And.Contain(elevated));

        results.Clear();
        threeDimensional.Query(authority, new Position(1, 17, 1), 1, results);
        Assert.That(results, Does.Contain(elevated).And.Not.Contain(nearPartition));
    }

    [Test]
    public void InterestProvisionerCreatesMissingPartitionsBeforeResolution()
    {
        int calls = 0;
        var settings = new GameplayMachineSettings
        {
            Serializers = new List<IGMSerializer> { new TestCommon.MyCustomObjectSurrogate() },
            Interest = new GameplayInterestSettings
            {
                CellSize = 8,
                PartitionResolver = new GameplayGridPartitionResolver(16, includeY: false),
                ProvisionPartitions = (machine, center, range) =>
                {
                    calls++;
                    var coordinate = new GameplayPartitionCoordinate(
                        (int)MathF.Floor(center.X / 16),
                        (int)MathF.Floor(center.Z / 16));
                    if (!machine.TryGetPartitionAtCoordinate(coordinate, out _))
                        machine.CreatePartition(PartitionKind.Chunk, $"generated:{coordinate.X}:{coordinate.Z}",
                            coordinate: coordinate);
                },
                LoadMargin = 0,
                UnloadDelaySeconds = 0,
                MaxLoadsPerUpdate = 8,
            },
        };
        GameplayMachine machine = GameplayMachineBase.SpawnGameplayMachine(null, settings,
            TestGMModule.Instance, TestInterestModule.Instance);
        SpatialPawn viewer = machine.CreateGameplayObject<SpatialPawn>();
        machine.Execute(new MoveSpatialPawnParam { Pawn = viewer, Position = new Position(35, 0, -17) });
        PlayerController player = machine.GetLocalPlayerController();
        machine.Execute(new SetTestViewRangeParam { PlayerController = player, Range = 8 });
        machine.Execute(new PossessParam { PlayerController = player, Pawn = viewer });

        machine.UpdateNetwork();

        var expected = new GameplayPartitionCoordinate(2, -2);
        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.GreaterThan(0));
            Assert.That(machine.TryGetPartitionAtCoordinate(expected, out PartitionID partition), Is.True);
            Assert.That(machine.IsPartitionInInterest(player, partition), Is.True);
        });
    }

    private SpatialPawn CreateAt(Position position)
    {
        SpatialPawn result = authority.CreateGameplayObject<SpatialPawn>();
        authority.Execute(new MoveSpatialPawnParam { Pawn = result, Position = position });
        return result;
    }

    private void PumpUntilReady()
    {
        for (int i = 0; i < 10 && client.NetworkState != GameplayNetworkState.Ready; i++)
            Pump(1);
        Assert.That(client.NetworkState, Is.EqualTo(GameplayNetworkState.Ready));
    }

    private void Pump(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            client.UpdateNetwork();
            authority.UpdateNetwork();
            client.UpdateNetwork();
        }
    }
}
