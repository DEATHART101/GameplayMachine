using System;
using System.Collections.Generic;
using GMCore;
using ODCore;
using TestGM_OD;

namespace Test;

internal struct AuthorityAddUnitsParam : IReplicatedInterface, IPlayerInputInterface
{
    public static GameplayRpcContext? LastServerContext;
    public int Count;
    public string Payload;
    public GameplayRpcMode RpcMode => GameplayRpcMode.Authority;
    public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) => Count >= 0 ? true : "Count cannot be negative";

    public void DoExecute(GameplayMachine.GameplayMachineProxy machine)
    {
        LastServerContext = machine.CurrentRpcContext;
        for (int index = 0; index < Count; index++)
            machine.GetGame().Units.Add(machine.CreateGameplayObject<Monster>());
    }
}

internal struct PlayerInputCaptureParam : IReplicatedInterface, IPlayerInputInterface
{
    public static PlayerController? LastController;
    public GameplayRpcMode RpcMode => GameplayRpcMode.Authority;

    public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) =>
        machine.TryGetExecutingPlayerController(out _) ? true : "Missing PlayerController";

    public void DoExecute(GameplayMachine.GameplayMachineProxy machine)
    {
        LastController = machine.GetExecutingPlayerController();
    }
}

internal struct InvalidMulticastPlayerInputParam : IReplicatedInterface, IPlayerInputInterface
{
    public GameplayRpcMode RpcMode => GameplayRpcMode.Multicast;
    public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) => true;
    public void DoExecute(GameplayMachine.GameplayMachineProxy machine) { }
}

internal struct MulticastCounterEvent : IMulticastEvent
{
    public static readonly ODEventName EventName = new() { NameObject = "GameplayRpcTest.MulticastCounter" };
    public int Value;
    public ODEventName GetODEventName() => EventName;
}

internal struct AuthorityOutputResult
{
    public int Value;
}

internal struct AuthorityOutputParam : ICheckableInterface<AuthorityOutputResult>, IGameplayInterface
{
    public static int ServerExecutionCount;
    public int Value;
    public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) => true;

    public void DoExecute(GameplayMachine.GameplayMachineProxy machine)
    {
        AuthorityOutputResult ignored = default;
        DoExecute(machine, ref ignored);
    }

    public void DoExecute(GameplayMachine.GameplayMachineProxy machine, ref AuthorityOutputResult outResult)
    {
        ServerExecutionCount++;
        outResult.Value = Value;
    }
}

internal struct SetLocalAttackResult
{
}

internal struct SetLocalAttackParam : ICheckableInterface<SetLocalAttackResult>
{
    public int Attack;
    public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) => true;

    public void DoExecute(GameplayMachine.GameplayMachineProxy machine, ref SetLocalAttackResult outResult)
    {
        Player player = machine.GetGame().Player.Value;
        player.Attack = Attack;
    }
}

internal struct PredictAttackParam : IPredictableInterface, IPlayerInputInterface
{
    public static GameplayPredictionMode ConfiguredMode = GameplayPredictionMode.Both;
    public static int PredictionCount;
    public static int SuccessCount;
    public static int FailureCount;
    public static int LastCallbackExpectedAttack;

    public int ExpectedAttack;
    public int ResultAttack;

    public GameplayRpcMode RpcMode => GameplayRpcMode.Predictable;
    public GameplayPredictionMode PredictionMode => ConfiguredMode;

    public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) =>
        machine.GetGame().Player.Value.Attack == ExpectedAttack
            ? true
            : "Attack value changed";

    public void DoExecute(GameplayMachine.GameplayMachineProxy machine)
    {
        Player player = machine.GetGame().Player.Value;
        player.Attack = ResultAttack;
    }

    public void OnPredictClient(GameplayMachine.GameplayMachineProxy machine)
    {
        PredictionCount++;
        Player player = machine.GetGame().Player.Value;
        player.Attack = ResultAttack;
        ExpectedAttack = -1;
        ResultAttack = -1;
    }

    public void OnSuccessClient(GameplayMachine.GameplayMachineProxy machine)
    {
        SuccessCount++;
        LastCallbackExpectedAttack = ExpectedAttack;
    }

    public void OnFailClient(GameplayMachine.GameplayMachineProxy machine)
    {
        FailureCount++;
        LastCallbackExpectedAttack = ExpectedAttack;
        Player player = machine.GetGame().Player.Value;
        player.Attack = ExpectedAttack;
    }

    public static void ResetCounters()
    {
        ConfiguredMode = GameplayPredictionMode.Both;
        PredictionCount = 0;
        SuccessCount = 0;
        FailureCount = 0;
        LastCallbackExpectedAttack = 0;
    }
}

internal struct NonReplicatedObject : IGameplayObjectOperator, IODClass
{
    public static readonly ODClassName ClassName = new()
    {
        NameObject = "GameplayRpcTest.NonReplicatedObject",
    };

    public GameplayMachine Machine { get; set; }
    public GObjectID ObjectID { get; set; }
    public ODClassName ObjectClass => Machine.GetGameplayObjectClass(ObjectID);
    public ODClassName GetODClassName() => ClassName;

    public bool IsA<T>() where T : struct, IODClass =>
        GameplayMachineBase.CanCastTo(ObjectClass, new T().GetODClassName());

    public T? Cast<T>() where T : struct, IGameplayObjectOperator, IODClass => Machine.Cast<T>(this);
    public void Return() => Delete();
    public void Delete() => Machine.DeleteGameplayObject(ObjectID);
}

internal sealed class GameplayRpcTestModule : IODModule, IODNetworkModule, IODMulticastEventNetworkModule
{
    public static GameplayRpcTestModule Instance { get; } = new();
    public ODModuleName Name => new() { Name = "GameplayRpcTest" };
    public ulong NetworkSchemaID => ODSaveHash.Compute("GameplayRpcTest:4|NonReplicatedObject:None");

    public IEnumerable<ODRpcMeta> GetAllODRpcMetas()
    {
        yield return new ODRpcMeta
        {
            StableID = ODSaveHash.Compute("GameplayRpcTest:PlayerInputCapture"),
            StableName = "GameplayRpcTest:PlayerInputCapture",
            Mode = GameplayRpcMode.Authority,
            InterfaceType = GameplayInterfaceType.PlayerInput,
            ParamType = typeof(PlayerInputCaptureParam),
            ParamWriter = (_, _) => { },
            ParamReader = _ => new PlayerInputCaptureParam(),
        };
        yield return new ODRpcMeta
        {
            StableID = ODSaveHash.Compute("GameplayRpcTest:AuthorityAddUnits"),
            StableName = "GameplayRpcTest:AuthorityAddUnits",
            Mode = GameplayRpcMode.Authority,
            InterfaceType = GameplayInterfaceType.PlayerInput,
            ParamType = typeof(AuthorityAddUnitsParam),
            ParamWriter = (writer, boxed) =>
            {
                var value = (AuthorityAddUnitsParam)boxed;
                writer.WriteVarInt32(value.Count);
                writer.WriteString(value.Payload);
            },
            ParamReader = reader => new AuthorityAddUnitsParam
            {
                Count = reader.ReadVarInt32(),
                Payload = reader.ReadString(),
            },
        };
        yield return new ODRpcMeta
        {
            StableID = ODSaveHash.Compute("GameplayRpcTest:PredictAttack"),
            StableName = "GameplayRpcTest:PredictAttack",
            Mode = GameplayRpcMode.Predictable,
            InterfaceType = GameplayInterfaceType.PlayerInput,
            ParamType = typeof(PredictAttackParam),
            ParamWriter = (writer, boxed) =>
            {
                var value = (PredictAttackParam)boxed;
                writer.WriteVarInt32(value.ExpectedAttack);
                writer.WriteVarInt32(value.ResultAttack);
            },
            ParamReader = reader => new PredictAttackParam
            {
                ExpectedAttack = reader.ReadVarInt32(),
                ResultAttack = reader.ReadVarInt32(),
            },
        };
    }

    public IEnumerable<ODMulticastEventMeta> GetAllODMulticastEventMetas()
    {
        yield return new ODMulticastEventMeta
        {
            StableID = ODSaveHash.Compute("GameplayRpcTest:MulticastCounter"),
            StableName = "GameplayRpcTest:MulticastCounter",
            EventType = typeof(MulticastCounterEvent),
            Writer = (writer, boxed) => writer.WriteVarInt32(((MulticastCounterEvent)boxed).Value),
            Reader = reader => new MulticastCounterEvent { Value = reader.ReadVarInt32() },
        };
    }

    public IEnumerable<ODClassMeta> GetAllODClassMetas()
    {
        yield return new ODClassMeta
        {
            FromModule = Name,
            Name = NonReplicatedObject.ClassName,
            ODType = typeof(NonReplicatedObject),
            ReplicationMode = GameplayObjectReplicationMode.None,
            DirectBaseClasses = new List<ODClassName>(),
            Fields = new List<ODFieldMeta>(),
        };
    }

    public IEnumerable<ODStructMeta> GetAllODStructMetas() => Array.Empty<ODStructMeta>();
    public IEnumerable<ODEventMeta> GetAllODEventMetas()
    {
        yield return new ODEventMeta
        {
            FromModule = Name,
            Name = MulticastCounterEvent.EventName,
            ODType = typeof(MulticastCounterEvent),
            Multicast = true,
        };
    }
    public IEnumerable<ODClassSaveMeta> GetAllODClassSaveMetas()
    {
        yield return new ODClassSaveMeta
        {
            StableID = ODSaveHash.Compute("GameplayRpcTest.NonReplicatedObject"),
            StableName = "GameplayRpcTest.NonReplicatedObject",
            ClassName = NonReplicatedObject.ClassName,
        };
    }

    public IEnumerable<ODFieldSaveMeta> GetAllODFieldSaveMetas() => Array.Empty<ODFieldSaveMeta>();
}
