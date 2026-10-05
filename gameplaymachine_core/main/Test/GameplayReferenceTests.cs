using GMCore.Collections;

namespace Test;

public class GameplayReferenceTests
{
    private static readonly GameplayMachineSettings s_settings = new GameplayMachineSettings
    {
        Serializers = new System.Collections.Generic.List<IGMSerializer>
        {
            new TestCommon.MyCustomObjectSurrogate(),
        },
    };

    [Test]
    public void DeletingObjectClearsDirectReferenceAndRaisesChanged()
    {
        GameplayMachine machine = CreateMachine();
        Game game = machine.GetGame();
        Monster target = machine.CreateGameplayObject<Monster>();
        game.A = target;
        int changed = 0;
        var binding = game.AfterAChanged.Bind(_ => changed++);

        Assert.That(machine.DeleteGameplayObject(target), Is.True);

        binding.Return();
        Assert.Multiple(() =>
        {
            Assert.That(game.A, Is.Null);
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(machine.ContainsGameplayObject(target.ObjectID), Is.False);
        });
    }

    [Test]
    public void ReplacingReferenceRemovesOldReverseIndexEntry()
    {
        GameplayMachine machine = CreateMachine();
        Game game = machine.GetGame();
        Monster oldTarget = machine.CreateGameplayObject<Monster>();
        Monster currentTarget = machine.CreateGameplayObject<Monster>();
        game.A = oldTarget;
        game.A = currentTarget;
        int changed = 0;
        var binding = game.AfterAChanged.Bind(_ => changed++);

        Assert.That(machine.DeleteGameplayObject(oldTarget), Is.True);

        binding.Return();
        Assert.Multiple(() =>
        {
            Assert.That(game.A.HasValue, Is.True);
            Assert.That(game.A.Value.ObjectID, Is.EqualTo(currentTarget.ObjectID));
            Assert.That(changed, Is.Zero);
        });
    }

    [Test]
    public void DeletingObjectRemovesSetAndMapReferencesWithItemEvents()
    {
        GameplayMachine machine = CreateMachine();
        Game game = machine.GetGame();
        Monster target = machine.CreateGameplayObject<Monster>();
        game.Units.Add(target);
        game.IDUnits.Add(3, target);
        game.IDUnits.Add(8, target);
        var setChanges = new System.Collections.Generic.List<CollectionItemChangedParam<Unit>>();
        var mapChanges = new System.Collections.Generic.List<CollectionItemChangedParam<int>>();
        var setBinding = game.UnitsBinder.BindItemChanged(change => setChanges.Add(change));
        var mapBinding = game.IDUnitsBinder.BindItemChanged(change => mapChanges.Add(change));

        Assert.That(machine.DeleteGameplayObject(target), Is.True);

        setBinding.Return();
        mapBinding.Return();
        Assert.Multiple(() =>
        {
            Assert.That(game.Units, Is.Empty);
            Assert.That(game.IDUnits.Count, Is.Zero);
            Assert.That(setChanges.Count, Is.EqualTo(1));
            Assert.That(setChanges[0].ChangeType, Is.EqualTo(CollectionItemChangeType.Remove));
            Assert.That(setChanges[0].Item.ObjectID, Is.EqualTo(target.ObjectID));
            Assert.That(mapChanges.Select(change => change.Item), Is.EquivalentTo(new[] { 3, 8 }));
            Assert.That(mapChanges.All(change => change.ChangeType == CollectionItemChangeType.Remove), Is.True);
        });
    }

    [Test]
    public void LoadedSaveRebuildsReferenceIndex()
    {
        GameplayMachine machine = CreateMachine();
        Game game = machine.GetGame();
        Monster target = machine.CreateGameplayObject<Monster>();
        game.A = target;
        game.Units.Add(target);
        byte[] save = GameplayMachineBase.SaveGameplayMachine(machine);

        TestStatics.SetupNewTest();
        GameplayMachine loaded = GameplayMachineBase.LoadGameplayMachine(
            TestStatics.TestKey,
            s_settings,
            save,
            TestGMModule.Instance);
        Game loadedGame = loaded.GetGame();
        Monster loadedTarget = loadedGame.A.Value;

        Assert.That(loaded.DeleteGameplayObject(loadedTarget), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(loadedGame.A, Is.Null);
            Assert.That(loadedGame.Units, Is.Empty);
        });
    }

    [Test]
    public void ReferenceWalkerHandlesStructAndListValues()
    {
        GameplayMachine machine = CreateMachine();
        Monster target = machine.CreateGameplayObject<Monster>();
        Unit targetUnit = target.Cast<Unit>().Value;
        var nested = new NestedReference
        {
            Target = target,
            Targets = new System.Collections.Generic.List<Unit?> { targetUnit },
            Value = 7,
        };

        object cleaned = GameplayObjectReferenceWalker.Remove(nested, machine, target.ObjectID, out bool changed);
        var cleanedNested = (NestedReference)cleaned;
        var list = new ListCollection<NestedReference> { nested, new NestedReference { Value = 9 } };
        GameplayReferenceCollectionCleanup cleanup =
            ((IGameplayReferenceCollection)list).RemoveGameplayObjectReferences(machine, target.ObjectID);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(cleanedNested.Target, Is.Null);
            Assert.That(cleanedNested.Targets, Is.Empty);
            Assert.That(cleanedNested.Value, Is.EqualTo(7));
            Assert.That(cleanup.Changed, Is.True);
            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(list[0].Value, Is.EqualTo(9));
        });
    }

    [Test]
    public void CollectionCleanupRemovesMapKeysAndValues()
    {
        GameplayMachine machine = CreateMachine();
        Monster target = machine.CreateGameplayObject<Monster>();
        Unit targetUnit = target.Cast<Unit>().Value;
        var keyMap = new MapCollection<Unit, int> { { targetUnit, 1 } };
        var valueMap = new MapCollection<int, NestedReference>
        {
            { 4, new NestedReference { Target = target, Value = 2 } },
        };

        GameplayReferenceCollectionCleanup keyCleanup =
            ((IGameplayReferenceCollection)keyMap).RemoveGameplayObjectReferences(machine, target.ObjectID);
        GameplayReferenceCollectionCleanup valueCleanup =
            ((IGameplayReferenceCollection)valueMap).RemoveGameplayObjectReferences(machine, target.ObjectID);

        Assert.Multiple(() =>
        {
            Assert.That(keyCleanup.Changed, Is.True);
            Assert.That(valueCleanup.Changed, Is.True);
            Assert.That(keyMap, Is.Empty);
            Assert.That(valueMap, Is.Empty);
        });
    }

    [Test]
    public void ReferenceWalkerOnlyVisitsActiveSwitchStructValue()
    {
        GameplayMachine machine = CreateMachine();
        Monster hiddenTarget = machine.CreateGameplayObject<Monster>();
        Monster activeTarget = machine.CreateGameplayObject<Monster>();
        var value = new SwitchReference();
        value.Target = hiddenTarget;
        value.Number = 12;

        Assert.That(GameplayObjectReferenceWalker.Contains(value, machine, hiddenTarget.ObjectID), Is.False);

        value.Target = activeTarget;
        object cleaned = GameplayObjectReferenceWalker.Remove(
            value, machine, activeTarget.ObjectID, out bool changed);
        var cleanedValue = (SwitchReference)cleaned;

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(cleanedValue.SwitchType, Is.EqualTo(SwitchReference.SwitchTypes.Target));
            Assert.That(cleanedValue.Target, Is.Null);
        });
    }

    private static GameplayMachine CreateMachine()
    {
        TestStatics.SetupNewTest();
        return GameplayMachineBase.SpawnGameplayMachine(
            TestStatics.TestKey,
            s_settings,
            TestGMModule.Instance);
    }

    private struct NestedReference : IODStruct
    {
        public Unit? Target;
        public System.Collections.Generic.List<Unit?> Targets;
        public int Value;

        public ODStructName GetODStructName() => default;
    }

    private struct SwitchReference : IODStruct
    {
        public enum SwitchTypes
        {
            Target,
            Number,
        }

        private SwitchTypes m_switchType;
        private Unit? m_target;
        private int m_number;

        public SwitchTypes SwitchType => m_switchType;

        public Unit? Target
        {
            get => m_target;
            set
            {
                m_switchType = SwitchTypes.Target;
                m_target = value;
            }
        }

        public int Number
        {
            get => m_number;
            set
            {
                m_switchType = SwitchTypes.Number;
                m_number = value;
            }
        }

        public ODStructName GetODStructName() => default;
    }
}
