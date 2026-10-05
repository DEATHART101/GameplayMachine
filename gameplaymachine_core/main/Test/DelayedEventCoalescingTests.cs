using ODCore;

namespace Test;

public class DelayedCollectionEventCoalescingTests
{
    private GameplayMachine machine = null!;
    private Game game;
    private Unit firstUnit;
    private Unit secondUnit;

    [SetUp]
    public void SetUp()
    {
        machine = GameplayMachineBase.SpawnGameplayMachine(
            null,
            new GameplayMachineSettings { LocalPlayerMode = GameplayLocalPlayerMode.Disabled },
            TestGMModule.Instance);
        game = machine.GetGame();
        firstUnit = machine.CreateGameplayObject<Player>();
        secondUnit = machine.CreateGameplayObject<Player>();
        machine.SetUseDelayedCallback(true);
    }

    [Test]
    public void ListRoundTripStillRaisesOneChangedCallback()
    {
        int changed = 0;
        game.IntArrayBinder.Bind(_ => changed++);

        machine.Execute(new CollectionRoundTripParam
        {
            Game = game,
            Unit = firstUnit,
            Operation = CollectionRoundTripOperation.List,
        });

        Assert.That(changed, Is.EqualTo(1));
        Assert.That(game.IntArray.Count, Is.Zero);
    }

    [Test]
    public void SetAddThenRemoveRaisesNoCallbacksWhenContentIsUnchanged()
    {
        int changed = 0;
        int itemChanged = 0;
        game.UnitsBinder.BindChanged(_ => changed++);
        game.UnitsBinder.BindItemChanged(_ => itemChanged++);

        machine.Execute(new CollectionRoundTripParam
        {
            Game = game,
            Unit = firstUnit,
            Operation = CollectionRoundTripOperation.Set,
        });

        Assert.That(changed, Is.Zero);
        Assert.That(itemChanged, Is.Zero);
        Assert.That(game.Units.Count, Is.Zero);
    }

    [Test]
    public void MapAddThenRemoveRaisesNoCallbacksWhenContentIsUnchanged()
    {
        int changed = 0;
        int itemChanged = 0;
        game.IDUnitsBinder.BindChanged(_ => changed++);
        game.IDUnitsBinder.BindItemChanged(_ => itemChanged++);

        machine.Execute(new CollectionRoundTripParam
        {
            Game = game,
            Unit = firstUnit,
            Operation = CollectionRoundTripOperation.MapAddRemove,
        });

        Assert.That(changed, Is.Zero);
        Assert.That(itemChanged, Is.Zero);
        Assert.That(game.IDUnits.Count, Is.Zero);
    }

    [Test]
    public void MapValueChangeThenRestoreRaisesNoCallbacks()
    {
        machine.Execute(new CollectionRoundTripParam
        {
            Game = game,
            Unit = firstUnit,
            Operation = CollectionRoundTripOperation.MapAddOnly,
        });
        int changed = 0;
        int itemChanged = 0;
        game.IDUnitsBinder.BindChanged(_ => changed++);
        game.IDUnitsBinder.BindItemChanged(_ => itemChanged++);

        machine.Execute(new CollectionRoundTripParam
        {
            Game = game,
            Unit = firstUnit,
            OtherUnit = secondUnit,
            Operation = CollectionRoundTripOperation.MapValue,
        });

        Assert.That(changed, Is.Zero);
        Assert.That(itemChanged, Is.Zero);
        Assert.That(game.IDUnits[1], Is.EqualTo(firstUnit));
    }

    private enum CollectionRoundTripOperation
    {
        List,
        Set,
        MapAddRemove,
        MapAddOnly,
        MapValue,
    }

    private struct CollectionRoundTripResult
    {
    }

    private struct CollectionRoundTripParam : ICheckableInterface<CollectionRoundTripResult>
    {
        public Game Game;
        public Unit Unit;
        public Unit OtherUnit;
        public CollectionRoundTripOperation Operation;

        public EventError CanExecute(GameplayMachine.GameplayMachineProxy gameplayMachine) => true;

        public void DoExecute(GameplayMachine.GameplayMachineProxy gameplayMachine,
            ref CollectionRoundTripResult outResult)
        {
            switch (Operation)
            {
                case CollectionRoundTripOperation.List:
                    Game.IntArray.Add(7);
                    Game.IntArray.Remove(7);
                    break;
                case CollectionRoundTripOperation.Set:
                    Game.Units.Add(Unit);
                    Game.Units.Remove(Unit);
                    break;
                case CollectionRoundTripOperation.MapAddRemove:
                    Game.IDUnits.Add(1, Unit);
                    Game.IDUnits.Remove(1);
                    break;
                case CollectionRoundTripOperation.MapAddOnly:
                    Game.IDUnits.Add(1, Unit);
                    break;
                case CollectionRoundTripOperation.MapValue:
                    GMCore.Collections.Map<int, Unit> map = Game.IDUnits;
                    map[1] = OtherUnit;
                    map[1] = Unit;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}

public class DelayedDrivenFieldCoalescingTests
{
    [Test]
    public void ThreeChangedDriversRaiseOneDrivenCallback()
    {
        GameplayMachine machine = GameplayMachineBase.SpawnGameplayMachine(
            null,
            new GameplayMachineSettings { LocalPlayerMode = GameplayLocalPlayerMode.Disabled },
            ThreeDriverModule.Instance);
        CommonGameplayObject target = machine.CreateGameplayObject(ThreeDriverModule.ClassName);
        machine.SetUseDelayedCallback(true);

        int callbacks = 0;
        bool callbackDuringExecution = false;
        new FieldChangeEventBinder
        {
            Machine = machine,
            ObjectID = target.ObjectID,
            FieldName = ThreeDriverModule.Driven,
        }.Bind(_ =>
        {
            callbacks++;
            callbackDuringExecution |= machine.DuringExecution;
        });

        machine.Execute(new SetThreeDriversParam { Target = target.ObjectID });

        Assert.That(callbacks, Is.EqualTo(1));
        Assert.That(callbackDuringExecution, Is.False);
    }

    private struct SetThreeDriversResult
    {
    }

    private struct SetThreeDriversParam : ICheckableInterface<SetThreeDriversResult>
    {
        public GObjectID Target;

        public EventError CanExecute(GameplayMachine.GameplayMachineProxy gameplayMachine) => true;

        public void DoExecute(GameplayMachine.GameplayMachineProxy gameplayMachine,
            ref SetThreeDriversResult outResult)
        {
            gameplayMachine.SetGameplayObjectValue(Target, ThreeDriverModule.First, 1);
            gameplayMachine.SetGameplayObjectValue(Target, ThreeDriverModule.Second, 2);
            gameplayMachine.SetGameplayObjectValue(Target, ThreeDriverModule.Third, 3);
        }
    }

    private sealed class ThreeDriverModule : IODModule
    {
        public static readonly ThreeDriverModule Instance = new();
        public static readonly ODClassName ClassName = new() { NameObject = "Test.Delayed.ThreeDriver" };
        public static readonly ODFieldName First = new() { NameObject = "Test.Delayed.ThreeDriver.First" };
        public static readonly ODFieldName Second = new() { NameObject = "Test.Delayed.ThreeDriver.Second" };
        public static readonly ODFieldName Third = new() { NameObject = "Test.Delayed.ThreeDriver.Third" };
        public static readonly ODFieldName Driven = new() { NameObject = "Test.Delayed.ThreeDriver.Driven" };

        private static readonly ODModuleName ModuleName = new() { Name = "Test.Delayed.ThreeDriver" };

        public ODModuleName Name => ModuleName;

        public IEnumerable<ODClassMeta> GetAllODClassMetas()
        {
            yield return new ODClassMeta
            {
                FromModule = ModuleName,
                Name = ClassName,
                ODType = typeof(CommonGameplayObject),
                ReplicationMode = GameplayObjectReplicationMode.None,
                RelevancyMode = GameplayObjectRelevancyMode.Always,
                PartitionMode = GameplayObjectPartitionMode.Persistent,
                DirectBaseClasses = new List<ODClassName>(),
                Fields = new List<ODFieldMeta>
                {
                    StoredField(First),
                    StoredField(Second),
                    StoredField(Third),
                    new()
                    {
                        FromClass = ClassName,
                        Name = Driven,
                        FieldType = typeof(int),
                        ReplicationMode = ODFieldReplicationMode.None,
                        IsDriven = true,
                        DrivenBys = new List<ODFieldName> { First, Second, Third },
                        DrivingOfs = new List<ODFieldName>(),
                    },
                },
            };
        }

        public IEnumerable<ODStructMeta> GetAllODStructMetas() => Array.Empty<ODStructMeta>();

        public IEnumerable<ODEventMeta> GetAllODEventMetas() => Array.Empty<ODEventMeta>();

        public IEnumerable<ODClassSaveMeta> GetAllODClassSaveMetas()
        {
            yield return new ODClassSaveMeta
            {
                StableID = ODSaveHash.Compute("Test.Delayed.ThreeDriver"),
                StableName = "Test.Delayed.ThreeDriver",
                ClassName = ClassName,
            };
        }

        public IEnumerable<ODFieldSaveMeta> GetAllODFieldSaveMetas()
        {
            yield return SaveField(First);
            yield return SaveField(Second);
            yield return SaveField(Third);
        }

        private static ODFieldMeta StoredField(ODFieldName field) => new()
        {
            FromClass = ClassName,
            Name = field,
            DefaultObject = 0,
            FieldType = typeof(int),
            ReplicationMode = ODFieldReplicationMode.None,
            DrivenBys = new List<ODFieldName>(),
            DrivingOfs = new List<ODFieldName> { Driven },
        };

        private static ODFieldSaveMeta SaveField(ODFieldName field)
        {
            string stableName = field.NameObject.ToString()!;
            return new ODFieldSaveMeta
            {
                StableID = ODSaveHash.Compute(stableName),
                StableName = stableName,
                FieldName = field,
                Writer = static (writer, value) => writer.WriteInt32((int)value),
                Reader = static reader => reader.ReadInt32(),
            };
        }
    }
}
