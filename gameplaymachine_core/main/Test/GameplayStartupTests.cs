using System;
using System.Collections.Generic;
using System.Linq;
using XLockstep;

namespace Test;

public sealed class GameplayStartupTests
{
    private static readonly ODModuleName ModuleName = new() { Name = "Tests.Startup.Module" };
    private static readonly ODClassName RootClass = new() { NameObject = "Tests.Startup.Root" };
    private static readonly ulong LockstepInputID = ODSaveHash.Compute("Tests.Startup.LockstepInput");
    private static readonly List<string> ExecutionOrder = new();

    [Test]
    public void SpawnCreatesRootThenInitialSceneBeforeAuthorityPlayerJoins()
    {
        var scene = new TrackingScene();
        var settings = new GameplayMachineSettings
        {
            GameplayMachineFactory = () => new TrackingMachine(),
            RootGameplayObjectClass = RootClass,
            InitialScene = scene,
        };

        var machine = (TrackingMachine)GameplayMachineBase.SpawnGameplayMachine(
            null, settings, StartupModule.Instance);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(machine.HasRoot, Is.True);
                Assert.That(machine.GetRoot().ObjectClass, Is.EqualTo(RootClass));
                Assert.That(scene.SawRoot, Is.True);
                Assert.That(machine.Scenes.Count(), Is.EqualTo(1));
                Assert.That(machine.JoinSawRootAndScene, Is.True);
                Assert.That(machine.GetPlayerControllers().Count(), Is.EqualTo(1));
            });

            Assert.That(() => machine.CreateGameplayObject(RootClass),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => machine.DeleteGameplayObject(machine.GetRoot().ObjectID),
                Throws.TypeOf<InvalidOperationException>());
        }
        finally
        {
            GameplayMachineBase.RemoveGameplayMachine(machine.MachineKey);
        }
    }

    [Test]
    public void StateSyncTicksAtConfiguredFixedInterval()
    {
        var settings = new GameplayMachineSettings
        {
            GameplayMachineFactory = () => new TrackingMachine(),
            TickIntervalMilliseconds = 20,
        };
        var machine = (TrackingMachine)GameplayMachineBase.SpawnGameplayMachine(
            null, settings, StartupModule.Instance);
        try
        {
            machine.Update(0.019);
            Assert.That(machine.Ticks, Is.Zero);
            machine.Update(0.001);
            Assert.That(machine.Ticks, Is.EqualTo(1));
            machine.Update(0.045);
            Assert.That(machine.Ticks, Is.EqualTo(3));
            Assert.That(machine.TickCount, Is.EqualTo(3));
        }
        finally
        {
            GameplayMachineBase.RemoveGameplayMachine(machine.MachineKey);
        }
    }

    [Test]
    public void StateSyncNoneTickModeDoesNotTick()
    {
        var settings = new GameplayMachineSettings
        {
            GameplayMachineFactory = () => new TrackingMachine(),
            TickMode = GameplayTickMode.None,
            TickIntervalMilliseconds = 20,
        };
        var machine = (TrackingMachine)GameplayMachineBase.SpawnGameplayMachine(
            null, settings, StartupModule.Instance);
        try
        {
            machine.Update(1d);
            Assert.Multiple(() =>
            {
                Assert.That(machine.TickMode, Is.EqualTo(GameplayTickMode.None));
                Assert.That(machine.Ticks, Is.Zero);
                Assert.That(machine.TickCount, Is.Zero);
            });
        }
        finally
        {
            GameplayMachineBase.RemoveGameplayMachine(machine.MachineKey);
        }
    }

    [Test]
    public void DedicatedAuthorityDoesNotCreateLocalPlayer()
    {
        var settings = new GameplayMachineSettings
        {
            LocalPlayerMode = GameplayLocalPlayerMode.Disabled,
        };
        GameplayMachine machine = GameplayMachineBase.SpawnGameplayMachine(
            null, settings, StartupModule.Instance);
        try
        {
            Assert.That(machine.LocalPlayerController, Is.Null);
            Assert.That(machine.GetPlayerControllers(), Is.Empty);
        }
        finally
        {
            GameplayMachineBase.RemoveGameplayMachine(machine.MachineKey);
        }
    }

    [Test]
    public void LockstepFrameExecutesPlayerInputBeforeTick()
    {
        ExecutionOrder.Clear();
        var settings = new GameplayMachineSettings
        {
            GameplayMachineFactory = () => new TrackingMachine(),
            SynchronizationMode = GameplaySynchronizationMode.Lockstep,
        };
        var machine = (TrackingMachine)GameplayMachineBase.SpawnGameplayMachine(
            null, settings, StartupModule.Instance);
        try
        {
            Assert.That(machine.JoinedSlots, Is.Empty,
                "Lockstep OnPlayerJoin must wait until slots are assigned");
            machine.InitializeLockstepPlayerControllers(2, 1);
            Assert.Multiple(() =>
            {
                Assert.That(machine.JoinedSlots, Is.EqualTo(new[] { 0, 1 }));
                Assert.That(machine.GetLocalLockstepPlayerController().SlotID, Is.EqualTo(1));
                Assert.That(machine.GetPlayerControllerBySlot(0).SlotID, Is.Zero);
                Assert.That(machine.GetPlayerControllerBySlot(1).SlotID, Is.EqualTo(1));
            });
            ODRpcMeta rpc = StartupModule.Instance.GetAllODRpcMetas().Single();
            machine.ApplyLockstepFrame(new LockstepFrame
            {
                Tick = new LockstepTick(1),
                Commands =
                [
                    new LockstepCommand
                    {
                        PlayerId = 0,
                        TypeId = LockstepInputID,
                        Payload = GameplayRpcCodec.Write(
                            machine, machine.Serializers, rpc.ParamWriter, new LockstepInputParam()),
                    },
                ],
            }, new Dictionary<ulong, ODRpcMeta> { [LockstepInputID] = rpc });

            Assert.That(ExecutionOrder, Is.EqualTo(new[] { "game-start", "input", "tick" }));
            Assert.That(machine.TickCount, Is.EqualTo(1));
            machine.Update(1d);
            Assert.That(machine.TickCount, Is.EqualTo(1),
                "Lockstep ticks are driven by complete input frames, not wall-clock updates");
        }
        finally
        {
            GameplayMachineBase.RemoveGameplayMachine(machine.MachineKey);
        }
    }

    [Test]
    public void LockstepUsesDedicatedPlayerControllerType()
    {
        var settings = new GameplayMachineSettings
        {
            SynchronizationMode = GameplaySynchronizationMode.Lockstep,
        };
        GameplayMachine machine = GameplayMachineBase.SpawnGameplayMachine(
            null, settings, StartupModule.Instance);
        try
        {
            LockstepPlayerController controller = machine.GetLocalLockstepPlayerController();
            ODDebugClassMeta debugMeta = machine.Modules.OfType<IODDebugModule>()
                .SelectMany(module => module.GetAllODDebugClassMetas())
                .Single(meta => meta.ClassName.Equals(controller.ObjectClass));

            Assert.Multiple(() =>
            {
                Assert.That(typeof(PlayerController).Name, Is.EqualTo("PlayerController"));
                Assert.That(typeof(PlayerController).Namespace, Is.EqualTo("GMCore.StateSync"));
                Assert.That(typeof(LockstepPlayerController).Name, Is.EqualTo("PlayerController"));
                Assert.That(typeof(LockstepPlayerController).Namespace, Is.EqualTo("GMCore.Lockstep"));
                Assert.That(controller.ObjectClass, Is.EqualTo(LockstepPlayerController.ClassName));
                Assert.That(machine.LocalPlayerController, Is.Null);
                Assert.That(() => machine.GetPlayerControllers(), Throws.InvalidOperationException);
                Assert.That(typeof(LockstepPlayerController).GetProperty("Pawn"), Is.Null);
                Assert.That(typeof(LockstepPlayerController).GetProperty("ViewRange"), Is.Null);
                Assert.That(typeof(PlayerController).GetProperty("SlotID"), Is.Null);
                Assert.That(debugMeta.Fields.Select(field => field.DisplayName),
                    Is.EqualTo(new[] { "SlotID" }));
            });
        }
        finally
        {
            GameplayMachineBase.RemoveGameplayMachine(machine.MachineKey);
        }
    }

    private sealed class TrackingMachine : GameplayMachine
    {
        public bool JoinSawRootAndScene { get; private set; }
        public int Ticks { get; private set; }
        public List<int> JoinedSlots { get; } = new();

        protected override void OnPlayerJoin(PlayerController playerController)
        {
            JoinSawRootAndScene = HasRoot && Scenes.Any();
        }

        protected override void OnPlayerJoin(LockstepPlayerController playerController)
        {
            JoinedSlots.Add(playerController.SlotID);
        }

        protected override void OnGameStart() => ExecutionOrder.Add("game-start");

        protected override void OnTick()
        {
            Ticks++;
            ExecutionOrder.Add("tick");
        }
    }

    private struct LockstepInputParam : IReplicatedInterface, IPlayerInputInterface
    {
        public GameplayRpcMode RpcMode => GameplayRpcMode.Lockstep;
        public ODCore.EventError CanExecute(GameplayMachine.GameplayMachineProxy machine) => true;
        public void DoExecute(GameplayMachine.GameplayMachineProxy machine) => ExecutionOrder.Add("input");
    }

    private sealed class TrackingScene : IODScene
    {
        public bool SawRoot { get; private set; }

        public void Create(GameplayMachine.GameplayMachineProxy machine, GameplaySceneInstance instance)
        {
            SawRoot = machine.HasRoot;
        }
    }

    private sealed class StartupModule : IODModule, IODNetworkModule
    {
        public static readonly StartupModule Instance = new();
        public ODModuleName Name => ModuleName;

        public IEnumerable<ODClassMeta> GetAllODClassMetas()
        {
            yield return new ODClassMeta
            {
                FromModule = ModuleName,
                Name = RootClass,
                ODType = typeof(CommonGameplayObject),
                ReplicationMode = GameplayObjectReplicationMode.AllClients,
                RelevancyMode = GameplayObjectRelevancyMode.Always,
                PartitionMode = GameplayObjectPartitionMode.Persistent,
                IsEngineManaged = true,
                DirectBaseClasses = new List<ODClassName>(),
                Fields = new List<ODFieldMeta>(),
            };
        }

        public IEnumerable<ODStructMeta> GetAllODStructMetas() => Array.Empty<ODStructMeta>();
        public IEnumerable<ODEventMeta> GetAllODEventMetas() => Array.Empty<ODEventMeta>();
        public IEnumerable<ODClassSaveMeta> GetAllODClassSaveMetas()
        {
            yield return new ODClassSaveMeta
            {
                StableID = ODSaveHash.Compute("Tests.Startup.Root"),
                StableName = "Tests.Startup.Root",
                ClassName = RootClass,
            };
        }
        public IEnumerable<ODFieldSaveMeta> GetAllODFieldSaveMetas() => Array.Empty<ODFieldSaveMeta>();

        public ulong NetworkSchemaID => ODSaveHash.Compute("Tests.Startup.Network:1");

        public IEnumerable<ODRpcMeta> GetAllODRpcMetas()
        {
            yield return new ODRpcMeta
            {
                StableID = LockstepInputID,
                StableName = "Tests.Startup.LockstepInput",
                Mode = GameplayRpcMode.Lockstep,
                InterfaceType = GameplayInterfaceType.PlayerInput,
                ParamType = typeof(LockstepInputParam),
                ParamWriter = (_, _) => { },
                ParamReader = _ => new LockstepInputParam(),
            };
        }
    }
}
