using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GMCore;
using NUnit.Framework;
using ODCore;
using TestGM_OD;

namespace Test;

public class GameplayDebugTests
{
    private GameplayMachine m_machine = null!;
    private GameplayDebugClient m_client = null!;

    [SetUp]
    public void SetUp()
    {
        var settings = new GameplayMachineSettings { Serializers = new List<IGMSerializer>() };
        m_machine = GameplayMachineBase.SpawnGameplayMachine(null, settings,
            TestGMModule.Instance, GameplayDebugTestModule.Instance);
        Player player = m_machine.CreateGameplayObject<Player>();
        player.Name = "Before";
        Game game = m_machine.GetGame();
        game.Player = player;
        m_machine.StartDebugServer(new GameplayDebugOptions
        {
            Port = 0,
            Access = GameplayDebugAccess.Control,
            Token = "test-token",
        });
        m_client = new GameplayDebugClient();
    }

    [TearDown]
    public void TearDown()
    {
        m_client.Dispose();
        m_machine.StopDebugServer();
    }

    [Test]
    public async Task DebugClientInspectsMutatesCreatesDeletesAndInvokes()
    {
        var changes = new ConcurrentQueue<(int ObjectID, GameplayDebugObjectChangeKind Kind)>();
        m_client.ObjectChanged += (id, kind) => changes.Enqueue((id, kind));
        Task<GameplayDebugMachineInfo> connect = m_client.ConnectAsync("127.0.0.1", m_machine.DebugServerPort, "test-token");
        GameplayDebugMachineInfo machine = await Pump(connect);
        Assert.That(machine.Role, Is.EqualTo(GameplayMachineRole.Authority));

        GameplayDebugSchema schema = await Pump(m_client.GetSchemaAsync());
        GameplayDebugClassInfo playerClass = schema.Classes.Single(item => item.Name == "Player");
        GameplayDebugFieldInfo nameField = playerClass.Fields.Single(item => item.Name == "Name");
        GameplayDebugClassInfo unitClass = schema.Classes.Single(item => item.Name == "Unit");
        GameplayDebugClassInfo monsterClass = schema.Classes.Single(item => item.Name == "Monster");
        GameplayDebugFieldInfo unitsField = schema.Classes.Single(item => item.Name == "Game").Fields
            .Single(item => item.Name == "Units");
        Assert.Multiple(() =>
        {
            Assert.That(playerClass.AssignableClassIDs, Does.Contain(unitClass.StableID));
            Assert.That(monsterClass.AssignableClassIDs, Does.Contain(unitClass.StableID));
            Assert.That(unitsField.GameplayObjectClassID, Is.EqualTo(unitClass.StableID));
        });
        IReadOnlyList<GameplayDebugObjectInfo> objects = await Pump(m_client.GetObjectsAsync());
        GameplayDebugObjectInfo playerInfo = objects.Single(item => item.ClassID == playerClass.StableID);

        GameplayDebugObjectSnapshot before = await Pump(m_client.GetObjectAsync(playerInfo.ObjectID));
        Assert.That(before.Fields.Single(item => item.FieldID == nameField.StableID).Value.Scalar, Is.EqualTo("Before"));

        await Pump(m_client.SetFieldAsync(playerInfo.ObjectID, nameField.StableID,
            GameplayDebugValueConverter.FromObject("Edited")));
        Assert.That(m_machine.GetGameplayObject<Player>(new GObjectID { ID = playerInfo.ObjectID }).Name, Is.EqualTo("Edited"));

        int createdID = await Pump(m_client.CreateObjectAsync(playerClass.StableID));
        Assert.That(m_machine.ContainsGameplayObject(new GObjectID { ID = createdID }), Is.True);
        await Pump(m_client.DeleteObjectAsync(createdID));
        Assert.That(m_machine.ContainsGameplayObject(new GObjectID { ID = createdID }), Is.False);

        GameplayDebugInterfaceInfo rename = schema.Interfaces.Single(item => item.Name == "DebugRenamePlayer");
        ODDebugInvocationResult invocation = await Pump(m_client.InvokeAsync(rename.StableID,
            new Dictionary<string, GameplayDebugValue>
            {
                ["Name"] = GameplayDebugValueConverter.FromObject("Invoked"),
            }));
        Assert.That(invocation.Succeeded, Is.True);
        Assert.That(m_machine.GetGame().Player.GetValueOrDefault().Name, Is.EqualTo("Invoked"));
        Assert.That(invocation.Output.Members.Single(item => item.Name == "PreviousName").Value.Scalar,
            Is.EqualTo("Edited"));

        GameplayDebugInterfaceInfo playerInput = schema.Interfaces.Single(item => item.Name == "PlayerInputCapture");
        GameplayDebugObjectInfo playerController = objects.Single(item =>
            item.ClassID == ODSaveHash.Compute("GMCore.StateSync.PlayerController"));
        PlayerInputCaptureParam.LastController = null;
        ODDebugInvocationResult playerInputInvocation = await Pump(m_client.InvokeAsync(
            playerInput.StableID,
            new Dictionary<string, GameplayDebugValue>(),
            playerController.ObjectID));
        Assert.Multiple(() =>
        {
            Assert.That(playerInput.InterfaceType, Is.EqualTo(GameplayInterfaceType.PlayerInput));
            Assert.That(playerInputInvocation.Succeeded, Is.True);
            Assert.That(PlayerInputCaptureParam.LastController?.ObjectID.ID, Is.EqualTo(playerController.ObjectID));
        });
        Assert.That(m_machine.GetExecutionTraceHistory(), Has.None.Matches<GameplayExecutionTraceEvent>(item =>
            item.InterfaceName.Contains("GameplayDebugCommandMarker", StringComparison.Ordinal)));
        Assert.Multiple(() =>
        {
            Assert.That(changes, Has.Some.Matches<(int ObjectID, GameplayDebugObjectChangeKind Kind)>(change =>
                change.ObjectID == playerInfo.ObjectID && change.Kind == GameplayDebugObjectChangeKind.FieldChanged));
            Assert.That(changes, Has.Some.Matches<(int ObjectID, GameplayDebugObjectChangeKind Kind)>(change =>
                change.ObjectID == createdID && change.Kind == GameplayDebugObjectChangeKind.Created));
            Assert.That(changes, Has.Some.Matches<(int ObjectID, GameplayDebugObjectChangeKind Kind)>(change =>
                change.ObjectID == createdID && change.Kind == GameplayDebugObjectChangeKind.Deleted));
        });
    }

    [Test]
    public async Task ObserveOnlyEndpointAllowsInspectionButRejectsControl()
    {
        m_machine.StopDebugServer();
        m_machine.StartDebugServer(new GameplayDebugOptions
        {
            Port = 0,
            Access = GameplayDebugAccess.ObserveOnly,
        });

        GameplayDebugMachineInfo machine = await Pump(
            m_client.ConnectAsync("127.0.0.1", m_machine.DebugServerPort));
        GameplayDebugSchema schema = await Pump(m_client.GetSchemaAsync());
        IReadOnlyList<GameplayDebugObjectInfo> objects = await Pump(m_client.GetObjectsAsync());

        Assert.Multiple(() =>
        {
            Assert.That(machine.Access, Is.EqualTo(GameplayDebugAccess.ObserveOnly));
            Assert.That(schema.Classes, Is.Not.Empty);
            Assert.That(objects, Is.Not.Empty);
        });
        InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Pump(m_client.CreateObjectAsync(schema.Classes.First().StableID)))!;
        Assert.That(exception.Message, Does.Contain("observe-only"));
    }

    [Test]
    public void LocalDebugServerDefaultsToControlAccess()
    {
        Assert.That(new GameplayDebugOptions().Access, Is.EqualTo(GameplayDebugAccess.Control));
    }

    [Test]
    public void ResourceValuesRoundTripAsCatalogReferences()
    {
        var sword = new DebugResource("Sword");
        var shield = new DebugResource("Shield");
        var resources = new List<ODDebugResourceOption>
        {
            new() { ID = new ODResourceID(Guid.NewGuid()), Name = "Items / Sword", Value = sword },
            new() { ID = new ODResourceID(Guid.NewGuid()), Name = "Items / Shield", Value = shield },
        };

        GameplayDebugValue single = GameplayDebugValueConverter.FromObject(sword, resources, null);
        GameplayDebugValue sequence = GameplayDebugValueConverter.FromObject(
            new List<DebugResource> { sword, shield, sword }, resources, null);
        GameplayDebugValue map = GameplayDebugValueConverter.FromObject(
            new Dictionary<DebugResource, int> { [shield] = 3 }, null, resources);

        Assert.Multiple(() =>
        {
            Assert.That(single.Kind, Is.EqualTo(GameplayDebugValueKind.Resource));
            Assert.That(single.Members, Is.Empty);
            Assert.That(GameplayDebugValueConverter.ConvertTo<DebugResource>(single, m_machine, resources),
                Is.SameAs(sword));
            Assert.That(GameplayDebugValueConverter.ConvertTo<List<DebugResource>>(
                sequence, m_machine, resources), Is.EqualTo(new[] { sword, shield, sword }));
            Assert.That(GameplayDebugValueConverter.ConvertTo<Dictionary<DebugResource, int>>(
                map, m_machine, null, resources)[shield], Is.EqualTo(3));
        });
    }

    [Test]
    public void DebugProtocolPreservesResourceFieldMetadata()
    {
        var source = new GameplayDebugFieldInfo
        {
            Name = "Items",
            TypeName = typeof(int).FullName!,
            KeyTypeName = typeof(DebugResource).FullName!,
            Container = GameplayDebugContainerKind.Map,
            IsResourceKeyReference = true,
            ResourceKeyOptions = new List<GameplayDebugResourceOption>
            {
                new() { ID = Guid.NewGuid().ToString("N"), Name = "Items / Sword" },
            },
            DefaultValue = new GameplayDebugValue { Kind = GameplayDebugValueKind.Map },
        };
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            GameplayDebugProtocol.WriteFieldInfo(writer, source);
        stream.Position = 0;
        using var reader = new BinaryReader(stream);

        GameplayDebugFieldInfo result = GameplayDebugProtocol.ReadFieldInfo(reader);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsResourceKeyReference, Is.True);
            Assert.That(result.ResourceKeyOptions, Has.Count.EqualTo(1));
            Assert.That(result.ResourceKeyOptions[0].ID, Is.EqualTo(source.ResourceKeyOptions[0].ID));
            Assert.That(result.ResourceKeyOptions[0].Name, Is.EqualTo("Items / Sword"));
        });
    }

    [Test]
    public void DebugValueCaptureStopsAtCircularReferences()
    {
        var first = new CyclicDebugObject { Name = "First" };
        var second = new CyclicDebugObject { Name = "Second", Next = first };
        first.Next = second;

        GameplayDebugValue result = GameplayDebugValueConverter.FromObject(first);

        GameplayDebugValue capturedSecond = result.Members.Single(item => item.Name == nameof(CyclicDebugObject.Next)).Value;
        Assert.That(capturedSecond.Members.Single(item => item.Name == nameof(CyclicDebugObject.Next)).Value.Kind,
            Is.EqualTo(GameplayDebugValueKind.Null));
    }

    [Test]
    public void ExecutionTraceCapturesResourceInputsById()
    {
        CheckableInterfaceResult<DebugResourceResult> execution = m_machine.Execute<DebugResourceResult>(
            new DebugResourceParam { Item = GameplayDebugTestModule.TraceResource });

        GameplayExecutionTraceEvent started = m_machine.GetExecutionTraceHistory().Single(item =>
            item.Stage == GameplayExecutionTraceStage.Started && item.InterfaceName == "DebugResource");
        GameplayDebugValue itemValue = started.Input.Members.Single(item => item.Name == "Item").Value;
        Assert.Multiple(() =>
        {
            Assert.That(execution.Sussceeded, Is.True);
            Assert.That(itemValue.Kind, Is.EqualTo(GameplayDebugValueKind.Resource));
            Assert.That(itemValue.Scalar, Is.EqualTo(GameplayDebugTestModule.TraceResourceID.ToString()));
            Assert.That(itemValue.Members, Is.Empty);
        });
    }

    [Test]
    public void ExecutionTraceCapturesAndSerializesFailureReasonFromResultField()
    {
        CheckableInterfaceResult execution = m_machine.Execute(new DebugFailedReplicatedParam());
        GameplayExecutionTraceEvent completed = m_machine.GetExecutionTraceHistory().Single(item =>
            item.Stage == GameplayExecutionTraceStage.Completed &&
            item.InterfaceName.Contains(nameof(DebugFailedReplicatedParam), StringComparison.Ordinal));

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            GameplayDebugProtocol.WriteTrace(writer, completed);
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        GameplayExecutionTraceEvent roundTrip = GameplayDebugProtocol.ReadTrace(reader);

        Assert.Multiple(() =>
        {
            Assert.That(execution.Sussceeded, Is.False);
            Assert.That(completed.Succeeded, Is.False);
            Assert.That(completed.Error, Is.EqualTo(DebugFailedReplicatedParam.FailureReason));
            Assert.That(roundTrip.Error, Is.EqualTo(DebugFailedReplicatedParam.FailureReason));
        });
    }

    [Test]
    public void InterfaceExecutionFailuresAreNotLoggedByDefault()
    {
        var logger = new RecordingLogger();
        var settings = new GameplayMachineSettings
        {
            Logger = logger,
            Serializers = new List<IGMSerializer>(),
        };
        GameplayMachine machine = GameplayMachineBase.SpawnGameplayMachine(null, settings,
            TestGMModule.Instance, GameplayDebugTestModule.Instance);

        CheckableInterfaceResult execution = machine.Execute(new DebugFailedReplicatedParam());

        Assert.Multiple(() =>
        {
            Assert.That(execution.Sussceeded, Is.False);
            Assert.That(logger.Messages, Has.None.Contains(DebugFailedReplicatedParam.FailureReason));
        });
    }

    [Test]
    public void InterfaceExecutionFailuresCanBeLoggedExplicitly()
    {
        var logger = new RecordingLogger();
        var settings = new GameplayMachineSettings
        {
            Logger = logger,
            Serializers = new List<IGMSerializer>(),
            LogInterfaceExecutionFailures = true,
        };
        GameplayMachine machine = GameplayMachineBase.SpawnGameplayMachine(null, settings,
            TestGMModule.Instance, GameplayDebugTestModule.Instance);

        CheckableInterfaceResult execution = machine.Execute(new DebugFailedReplicatedParam());

        Assert.Multiple(() =>
        {
            Assert.That(execution.Sussceeded, Is.False);
            Assert.That(logger.Messages, Has.Some.Contains(DebugFailedReplicatedParam.FailureReason));
        });
    }

    [Test]
    public void DebugFrameReaderTreatsCleanEofAsDisconnect()
    {
        using var stream = new MemoryStream();

        bool hasFrame = GameplayDebugProtocol.TryReadFrame(stream, out byte[] packet);

        Assert.Multiple(() =>
        {
            Assert.That(hasFrame, Is.False);
            Assert.That(packet, Is.Null);
        });
    }

    [Test]
    public void DebugFrameReaderRejectsTruncatedHeader()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2 });

        Assert.Throws<EndOfStreamException>(() => GameplayDebugProtocol.TryReadFrame(stream, out _));
    }

    [Test]
    public async Task RemoteDebugServerShutdownIsReportedAsCleanDisconnect()
    {
        var disconnected = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        m_client.Disconnected += exception => disconnected.TrySetResult(exception);
        await Pump(m_client.ConnectAsync("127.0.0.1", m_machine.DebugServerPort, "test-token"));

        m_machine.StopDebugServer();
        Task completed = await Task.WhenAny(disconnected.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Multiple(() =>
        {
            Assert.That(completed, Is.SameAs(disconnected.Task), "The debug client did not observe the server shutdown");
            Assert.That(disconnected.Task.IsCompletedSuccessfully ? disconnected.Task.Result : new Exception(), Is.Null);
            Assert.That(m_client.IsConnected, Is.False);
        });
    }

    [Test]
    public async Task DebugCollectionEditsPreserveSetAndMapItemEvents()
    {
        Monster first = m_machine.CreateGameplayObject<Monster>();
        Monster second = m_machine.CreateGameplayObject<Monster>();
        Game game = m_machine.GetGame();
        game.Units.Add(first);
        game.IDUnits.Add(7, first);
        var setChanges = new List<CollectionItemChangedParam<Unit>>();
        var mapChanges = new List<CollectionItemChangedParam<int>>();
        var setBinding = game.UnitsBinder.BindItemChanged(change => setChanges.Add(change));
        var mapBinding = game.IDUnitsBinder.BindItemChanged(change => mapChanges.Add(change));

        try
        {
            await Pump(m_client.ConnectAsync("127.0.0.1", m_machine.DebugServerPort, "test-token"));
            GameplayDebugSchema schema = await Pump(m_client.GetSchemaAsync());
            GameplayDebugClassInfo gameClass = schema.Classes.Single(item => item.Name == "Game");
            GameplayDebugObjectInfo gameObject = (await Pump(m_client.GetObjectsAsync()))
                .Single(item => item.ClassID == gameClass.StableID);

            await Pump(m_client.SetFieldAsync(gameObject.ObjectID,
                gameClass.Fields.Single(item => item.Name == "Units").StableID,
                GameplayDebugValueConverter.FromObject(new[] { (Unit)second })));
            await Pump(m_client.SetFieldAsync(gameObject.ObjectID,
                gameClass.Fields.Single(item => item.Name == "IDUnits").StableID,
                GameplayDebugValueConverter.FromObject(new Dictionary<int, Unit>
                {
                    [7] = second,
                    [9] = first,
                })));

            Assert.Multiple(() =>
            {
                Assert.That(setChanges.Select(change => change.ChangeType), Is.EqualTo(new[]
                {
                    CollectionItemChangeType.Add,
                    CollectionItemChangeType.Remove,
                }));
                Assert.That(setChanges.Select(change => change.Item.ObjectID),
                    Is.EqualTo(new[] { second.ObjectID, first.ObjectID }));
                Assert.That(mapChanges.Select(change => change.ChangeType), Is.EqualTo(new[]
                {
                    CollectionItemChangeType.Add,
                    CollectionItemChangeType.Changed,
                }));
                Assert.That(mapChanges.Select(change => change.Item), Is.EqualTo(new[] { 9, 7 }));
            });
        }
        finally
        {
            setBinding.Return();
            mapBinding.Return();
        }
    }

    [Test]
    public async Task DebugInvokeOnClientProxyUsesAuthorityRpcSemantics()
    {
        var settings = new GameplayMachineSettings { Serializers = new List<IGMSerializer>() };
        GameplayMachine authority = GameplayMachineBase.SpawnGameplayMachine(null, settings,
            TestGMModule.Instance, GameplayRpcTestModule.Instance, GameplayDebugTestModule.Instance);
        Player player = authority.CreateGameplayObject<Player>();
        Game authorityGame = authority.GetGame();
        authorityGame.Player = player;
        InMemoryGameplayNetworkTransport.CreatePair(out InMemoryGameplayNetworkTransport serverTransport,
            out InMemoryGameplayNetworkTransport clientTransport);
        authority.EnableNetworking(serverTransport);
        GameplayMachine client = GameplayMachineBase.CreateClientProxy(null, settings,
            TestGMModule.Instance, GameplayRpcTestModule.Instance, GameplayDebugTestModule.Instance);
        client.ConnectNetworking(clientTransport);
        client.StartDebugServer(new GameplayDebugOptions { Port = 0, Access = GameplayDebugAccess.Control });
        using var debugClient = new GameplayDebugClient();
        var traces = new ConcurrentQueue<GameplayExecutionTraceEvent>();
        debugClient.TraceReceived += traces.Enqueue;

        try
        {
            await PumpNetworkUntil(() => client.NetworkState == GameplayNetworkState.Ready,
                authority, client);
            await PumpNetwork(debugClient.ConnectAsync("127.0.0.1", client.DebugServerPort), client, authority);
            GameplayDebugSchema schema = await PumpNetwork(debugClient.GetSchemaAsync(), client, authority);
            GameplayDebugInterfaceInfo addUnits = schema.Interfaces.Single(item => item.Name == "AuthorityAddUnits");
            ODDebugInvocationResult result = await PumpNetwork(debugClient.InvokeAsync(addUnits.StableID,
                new Dictionary<string, GameplayDebugValue>
                {
                    ["Count"] = GameplayDebugValueConverter.FromObject(2),
                    ["Payload"] = GameplayDebugValueConverter.FromObject("debug"),
                }), client, authority);
            await PumpNetworkUntil(() => authority.GetGame().Units.Count() == 2 && client.GetGame().Units.Count() == 2,
                authority, client);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True, result.Error);
                Assert.That(authority.GetGame().Units.Count(), Is.EqualTo(2));
                Assert.That(client.GetGame().Units.Count(), Is.EqualTo(2));
                Assert.That(traces, Has.Some.Matches<GameplayExecutionTraceEvent>(item =>
                    item.Stage == GameplayExecutionTraceStage.Completed &&
                    item.InterfaceName == "AuthorityAddUnits"));
            });
        }
        finally
        {
            debugClient.Dispose();
            client.StopDebugServer();
            client.DisableNetworking();
            authority.DisableNetworking();
        }
    }

    [Test]
    public void NestedInterfacesProduceParentChildTrace()
    {
        CheckableInterfaceResult<DebugOuterResult> result = m_machine.Execute<DebugOuterResult>(new DebugOuterParam());
        Assert.That(result.Sussceeded, Is.True);
        GameplayExecutionTraceEvent[] starts = m_machine.GetExecutionTraceHistory()
            .Where(item => item.Stage == GameplayExecutionTraceStage.Started &&
                           (item.InterfaceName.Contains(nameof(DebugOuterParam)) || item.InterfaceName.Contains(nameof(DebugInnerParam))))
            .ToArray();
        Assert.That(starts, Has.Length.EqualTo(2));
        GameplayExecutionTraceEvent outer = starts.Single(item => item.InterfaceName.Contains(nameof(DebugOuterParam)));
        GameplayExecutionTraceEvent inner = starts.Single(item => item.InterfaceName.Contains(nameof(DebugInnerParam)));
        Assert.Multiple(() =>
        {
            Assert.That(outer.Depth, Is.EqualTo(0));
            Assert.That(inner.Depth, Is.EqualTo(1));
            Assert.That(inner.ParentSpanID, Is.EqualTo(outer.SpanID));
            Assert.That(inner.TraceID, Is.EqualTo(outer.TraceID));
        });
    }

    [Test]
    public void RoutineTraceSpansStepsAndPreservesNestedInterfaceHierarchy()
    {
        CheckableInterfaceResult result = m_machine.Execute(new DebugRoutineParam());
        Assert.That(result.Sussceeded, Is.True);
        while (m_machine.HasRoutine)
            m_machine.StepRoutine();

        GameplayExecutionTraceEvent routine = m_machine.GetExecutionTraceHistory().Single(item =>
            item.Stage == GameplayExecutionTraceStage.Started &&
            item.InterfaceName.Contains(nameof(DebugRoutineParam)));
        GameplayExecutionTraceEvent nested = m_machine.GetExecutionTraceHistory().Single(item =>
            item.Stage == GameplayExecutionTraceStage.Started &&
            item.InterfaceName.Contains(nameof(DebugInnerParam)));
        GameplayExecutionTraceEvent completed = m_machine.GetExecutionTraceHistory().Single(item =>
            item.Stage == GameplayExecutionTraceStage.Completed && item.SpanID == routine.SpanID);
        Assert.Multiple(() =>
        {
            Assert.That(nested.ParentSpanID, Is.EqualTo(routine.SpanID));
            Assert.That(nested.Depth, Is.EqualTo(1));
            Assert.That(completed.Succeeded, Is.True);
        });
    }

    private async Task<T> Pump<T>(Task<T> task)
    {
        DateTime timeout = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < timeout)
        {
            m_machine.UpdateDebug();
            await Task.Delay(2);
        }
        m_machine.UpdateDebug();
        if (!task.IsCompleted) Assert.Fail("OD debug request timed out");
        return await task;
    }

    private static async Task<T> PumpNetwork<T>(Task<T> task, GameplayMachine debugMachine,
        GameplayMachine authority)
    {
        DateTime timeout = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < timeout)
        {
            debugMachine.UpdateDebug();
            authority.UpdateNetwork();
            debugMachine.UpdateNetwork();
            await Task.Delay(2);
        }
        debugMachine.UpdateDebug();
        if (!task.IsCompleted) Assert.Fail("OD network debug request timed out");
        return await task;
    }

    private static async Task PumpNetworkUntil(Func<bool> condition, GameplayMachine authority,
        GameplayMachine client)
    {
        DateTime timeout = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < timeout)
        {
            authority.UpdateNetwork();
            client.UpdateNetwork();
            client.UpdateDebug();
            await Task.Delay(2);
        }
        Assert.That(condition(), Is.True, "OD network debug operation timed out");
    }
}

internal sealed class DebugResource
{
    public DebugResource(string name) => Name = name;
    public string Name { get; }
}

internal sealed class CyclicDebugObject
{
    public string Name { get; set; } = string.Empty;
    public CyclicDebugObject? Next { get; set; }
}

internal struct DebugInnerResult { }

internal struct DebugRoutineParam : ICheckableRoutine
{
    public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) => true;

    public IEnumerator DoExecute(GameplayMachine.GameplayMachineProxy machine)
    {
        yield return machine.RoutineExecute<DebugInnerResult>(new DebugInnerParam());
    }
}

internal struct DebugInnerParam : ICheckableInterface<DebugInnerResult>
{
    public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) => true;
    public void DoExecute(GameplayMachine.GameplayMachineProxy machine, ref DebugInnerResult outResult) { }
}

internal struct DebugOuterResult { }

internal struct DebugOuterParam : ICheckableInterface<DebugOuterResult>
{
    public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) => true;
    public void DoExecute(GameplayMachine.GameplayMachineProxy machine, ref DebugOuterResult outResult) =>
        machine.Execute<DebugInnerResult>(new DebugInnerParam());
}

internal struct DebugResourceResult { }

internal struct DebugResourceParam : ICheckableInterface<DebugResourceResult>
{
    public DebugResource? Item;
    public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) => true;
    public void DoExecute(GameplayMachine.GameplayMachineProxy machine, ref DebugResourceResult outResult) { }
}

internal struct DebugFailedReplicatedParam : IReplicatedInterface, IGameplayInterface
{
    public const string FailureReason = "The debug action is not currently allowed";
    public GameplayRpcMode RpcMode => GameplayRpcMode.None;
    public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) => FailureReason;
    public void DoExecute(GameplayMachine.GameplayMachineProxy machine) { }
}

internal struct DebugRenamePlayerResult
{
    public string PreviousName;
}

internal struct DebugRenamePlayerParam : ICheckableInterface<DebugRenamePlayerResult>
{
    public string Name;
    public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) =>
        string.IsNullOrWhiteSpace(Name) ? "Name is required" : true;

    public void DoExecute(GameplayMachine.GameplayMachineProxy machine, ref DebugRenamePlayerResult outResult)
    {
        Player? playerReference = machine.GetGame().Player;
        if (!playerReference.HasValue)
            throw new InvalidOperationException("Player is not configured");
        Player player = playerReference.GetValueOrDefault();
        outResult.PreviousName = player.Name ?? string.Empty;
        player.Name = Name;
    }
}

internal sealed class GameplayDebugTestModule : IODModule, IODDebugModule
{
    public static GameplayDebugTestModule Instance { get; } = new();
    public static DebugResource TraceResource { get; } = new("Trace Resource");
    public static ODResourceID TraceResourceID { get; } = new(Guid.Parse("a1000000-0000-0000-0000-000000000001"));
    public ODModuleName Name => new() { Name = "GameplayDebugTests" };
    public ulong DebugSchemaID => ODSaveHash.Compute("GameplayDebugTests:1");

    public IEnumerable<ODDebugClassMeta> GetAllODDebugClassMetas()
    {
        List<ODClassMeta> classMetas = TestGMModule.Instance.GetAllODClassMetas().ToList();
        Dictionary<ODClassName, ODClassMeta> classesByName = classMetas.ToDictionary(item => item.Name);
        ODClassMeta unitMeta = classMetas.Single(item => item.ODType.Name == "Unit");
        ulong unitClassID = ODSaveHash.Compute(unitMeta.Name.ToString());
        foreach (ODClassMeta classMeta in classMetas)
        {
            var fields = new List<ODDebugFieldMeta>();
            if (classMeta.Name.Equals(new Player().GetODClassName()))
            {
                fields.Add(new ODDebugFieldMeta
                {
                    StableID = ODSaveHash.Compute("Player.Name"),
                    StableName = "Player.Name",
                    DisplayName = "Name",
                    FieldName = new ODFieldName { NameObject = Unit.FieldNames.Name },
                    ValueType = typeof(string),
                    Container = GameplayDebugContainerKind.Single,
                    Read = (machine, id) => machine.GetGameplayObject<Player>(id).Name,
                    Write = (machine, id, value) =>
                    {
                        Player player = machine.GetGameplayObject<Player>(id);
                        player.Name = (string)value;
                    },
                });
            }
            if (classMeta.Name.Equals(new Game().GetODClassName()))
            {
                fields.Add(new ODDebugFieldMeta
                {
                    StableID = ODSaveHash.Compute("Game.Units"),
                    StableName = "Game.Units",
                    DisplayName = "Units",
                    FieldName = new ODFieldName { NameObject = Game.FieldNames.Units },
                    ValueType = typeof(Unit),
                    Container = GameplayDebugContainerKind.Set,
                    IsGameplayObjectReference = true,
                    GameplayObjectClassID = unitClassID,
                });
                fields.Add(new ODDebugFieldMeta
                {
                    StableID = ODSaveHash.Compute("Game.IDUnits"),
                    StableName = "Game.IDUnits",
                    DisplayName = "IDUnits",
                    FieldName = new ODFieldName { NameObject = Game.FieldNames.IDUnits },
                    KeyType = typeof(int),
                    ValueType = typeof(Unit),
                    Container = GameplayDebugContainerKind.Map,
                    IsGameplayObjectReference = true,
                    GameplayObjectClassID = unitClassID,
                });
            }
            yield return new ODDebugClassMeta
            {
                StableID = ODSaveHash.Compute(classMeta.Name.ToString()),
                StableName = classMeta.Name.ToString(),
                DisplayName = classMeta.ODType.Name,
                ClassName = classMeta.Name,
                AssignableClassIDs = AssignableClasses(classMeta).Select(item => ODSaveHash.Compute(item.Name.ToString())).ToList(),
                Fields = fields,
            };
        }

        IEnumerable<ODClassMeta> AssignableClasses(ODClassMeta source)
        {
            var visited = new HashSet<ODClassName>();
            return Visit(source);

            IEnumerable<ODClassMeta> Visit(ODClassMeta current)
            {
                if (!visited.Add(current.Name))
                    yield break;
                yield return current;
                foreach (ODClassName parentName in current.DirectBaseClasses ?? new List<ODClassName>())
                    if (classesByName.TryGetValue(parentName, out ODClassMeta parent))
                        foreach (ODClassMeta item in Visit(parent))
                            yield return item;
            }
        }
    }

    public IEnumerable<ODDebugInterfaceMeta> GetAllODDebugInterfaceMetas()
    {
        yield return new ODDebugInterfaceMeta
        {
            StableID = ODSaveHash.Compute("PlayerInputCapture"),
            StableName = "PlayerInputCapture",
            DisplayName = "PlayerInputCapture",
            ParamType = typeof(PlayerInputCaptureParam),
            RpcMode = GameplayRpcMode.Authority,
            InterfaceType = GameplayInterfaceType.PlayerInput,
            Inputs = Array.Empty<ODDebugInterfaceFieldMeta>(),
            Outputs = Array.Empty<ODDebugInterfaceFieldMeta>(),
            Invoke = (machine, _) =>
            {
                CheckableInterfaceResult result = machine.Execute(new PlayerInputCaptureParam());
                return result.Sussceeded
                    ? ODDebugInvocationResult.Success()
                    : ODDebugInvocationResult.Failure(result.ErrorMessage.ToString());
            },
        };
        yield return new ODDebugInterfaceMeta
        {
            StableID = ODSaveHash.Compute("DebugResource"),
            StableName = "od:test:interface:debug-resource",
            DisplayName = "DebugResource",
            ParamType = typeof(DebugResourceParam),
            ResultType = typeof(DebugResourceResult),
            Inputs = new List<ODDebugInterfaceFieldMeta>
            {
                new()
                {
                    Name = "Item",
                    DisplayName = "Item",
                    ValueType = typeof(DebugResource),
                    IsResourceReference = true,
                    ResourceOptions = new List<ODDebugResourceOption>
                    {
                        new() { ID = TraceResourceID, Name = "Items / Trace Resource", Value = TraceResource },
                    },
                },
            },
            Outputs = Array.Empty<ODDebugInterfaceFieldMeta>(),
        };
        yield return new ODDebugInterfaceMeta
        {
            StableID = ODSaveHash.Compute("DebugRenamePlayer"),
            StableName = "DebugRenamePlayer",
            DisplayName = "DebugRenamePlayer",
            ParamType = typeof(DebugRenamePlayerParam),
            ResultType = typeof(DebugRenamePlayerResult),
            Inputs = new List<ODDebugInterfaceFieldMeta>
            {
                new() { Name = "Name", DisplayName = "Name", ValueType = typeof(string) },
            },
            Outputs = new List<ODDebugInterfaceFieldMeta>
            {
                new() { Name = "PreviousName", DisplayName = "PreviousName", ValueType = typeof(string) },
            },
            Invoke = (machine, inputs) =>
            {
                var param = new DebugRenamePlayerParam
                {
                    Name = GameplayDebugValueConverter.ConvertTo<string>(inputs["Name"], machine),
                };
                CheckableInterfaceResult<DebugRenamePlayerResult> result = machine.Execute<DebugRenamePlayerResult>(param);
                return result.Sussceeded
                    ? ODDebugInvocationResult.Success(result.Result)
                    : ODDebugInvocationResult.Failure(result.ErrorMessage.ToString());
            },
        };
        yield return new ODDebugInterfaceMeta
        {
            StableID = ODSaveHash.Compute("AuthorityAddUnits"),
            StableName = "AuthorityAddUnits",
            DisplayName = "AuthorityAddUnits",
            ParamType = typeof(AuthorityAddUnitsParam),
            RpcMode = GameplayRpcMode.Authority,
            Inputs = new List<ODDebugInterfaceFieldMeta>
            {
                new() { Name = "Count", DisplayName = "Count", ValueType = typeof(int) },
                new() { Name = "Payload", DisplayName = "Payload", ValueType = typeof(string) },
            },
            Invoke = (machine, inputs) =>
            {
                CheckableInterfaceResult result = machine.Execute(new AuthorityAddUnitsParam
                {
                    Count = GameplayDebugValueConverter.ConvertTo<int>(inputs["Count"], machine),
                    Payload = GameplayDebugValueConverter.ConvertTo<string>(inputs["Payload"], machine),
                });
                return result.Sussceeded
                    ? ODDebugInvocationResult.Success(null)
                    : ODDebugInvocationResult.Failure(result.ErrorMessage.ToString());
            },
        };
    }

    public IEnumerable<ODClassMeta> GetAllODClassMetas() => Array.Empty<ODClassMeta>();
    public IEnumerable<ODStructMeta> GetAllODStructMetas() => Array.Empty<ODStructMeta>();
    public IEnumerable<ODEventMeta> GetAllODEventMetas() => Array.Empty<ODEventMeta>();
    public IEnumerable<ODClassSaveMeta> GetAllODClassSaveMetas() => Array.Empty<ODClassSaveMeta>();
    public IEnumerable<ODFieldSaveMeta> GetAllODFieldSaveMetas() => Array.Empty<ODFieldSaveMeta>();
}
