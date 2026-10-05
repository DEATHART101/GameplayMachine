using ODCore;
using System.Collections;
using System.Runtime;
using TestGM_OD.Interfaces;

namespace Test;

public static class TestStatics
{
    private static MachineKey s_testKey;

    public static MachineKey TestKey
    {
        get
        {
            return s_testKey;
        }
    }

    public static void SetupNewTest()
    {
        s_testKey.ID++;
    }
}

public class SaveTest
{
    private GameplayMachine m_gm;

    private GameAttribute AttackValue = 10;

    private byte[] m_saveFile;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {
        Serializers = new List<IGMSerializer>()
        {
            new TestCommon.MyCustomObjectSurrogate(),
        },
    };

    [SetUp]
    public void Setup()
    {
        TestStatics.SetupNewTest();

        m_gm = GameplayMachineBase.SpawnGameplayMachine(TestStatics.TestKey, m_settings, TestGMModule.Instance);
        Game game = m_gm.GetGame();

        Player player = m_gm.CreateGameplayObject<Player>();
        game.Player = player;
        player.Attack = AttackValue;

        Monster monster = m_gm.CreateGameplayObject<Monster>();
        monster.Health = 42;
        game.A = monster;
    }

    [Test]
    public void PlainMachineCanHostEveryODClass()
    {
        Assert.That(m_gm.GetType(), Is.EqualTo(typeof(GameplayMachine)));

        foreach (ODClassMeta classMeta in TestGMModule.Instance.GetAllODClassMetas())
        {
            CommonGameplayObject gameplayObject = m_gm.CreateGameplayObject(classMeta.Name);
            Assert.That(gameplayObject.ObjectClass, Is.EqualTo(classMeta.Name));
        }
    }

    [Test]
    public void Test1()
    {
        m_saveFile = GameplayMachineBase.SaveGameplayMachine(m_gm);

        TestStatics.SetupNewTest();
        GameplayMachine loadGM = GameplayMachineBase.LoadGameplayMachine(TestStatics.TestKey, m_settings, m_saveFile, TestGMModule.Instance);

        if (loadGM.GetGame().Player.Value.Attack.Equals(AttackValue))
        {
            Assert.Pass();
        }
        else
        {
            Assert.Fail("Player's Attack value doesn't matche after load");
        }
    }
}

public class SaveTest2
{
    private GameplayMachine m_gm;

    private byte[] m_saveFile;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {
        Serializers = new List<IGMSerializer>()
        {
            new TestCommon.MyCustomObjectSurrogate(),
        },
    };

    [SetUp]
    public void Setup()
    {
        TestStatics.SetupNewTest();

        m_gm = GameplayMachineBase.SpawnGameplayMachine(TestStatics.TestKey, m_settings, TestGMModule.Instance);
        Game game = m_gm.GetGame();

        game.NotSerializableInt = new TestCommon.NotSerializableInt();
        game.NotSerializableInt.Value = 4;
    }

    [Test]
    public void Test1()
    {
        GameplayMachine loadGM = null;

        try
        {
            m_saveFile = GameplayMachineBase.SaveGameplayMachine(m_gm);

            TestStatics.SetupNewTest();
            loadGM = GameplayMachineBase.LoadGameplayMachine(TestStatics.TestKey, m_settings, m_saveFile, TestGMModule.Instance);
        }
        catch (Exception ex)
        {
            Assert.Fail();
        }

        Assert.IsTrue(loadGM != null);
        Assert.IsTrue(loadGM.GetGame().NotSerializableInt.Value == 4);

        Assert.Pass();
    }
}

public class SaveTest3
{
    private GameplayMachine m_gm;

    private GameAttribute AttackValue = 10;

    private byte[] m_saveFile;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        TestStatics.SetupNewTest();

        m_gm = GameplayMachineBase.SpawnGameplayMachine(TestStatics.TestKey, m_settings, TestGMModule.Instance);
        Game game = m_gm.GetGame();

        game.NotSerializableInt = new TestCommon.NotSerializableInt();
    }

    [Test]
    public void Test1()
    {
        try
        {
            m_saveFile = GameplayMachineBase.SaveGameplayMachine(m_gm);
        }
        catch (Exception ex)
        {
            Assert.Pass();
        }

        Assert.Fail();
    }
}

public class Create_Delete_ObjectTest
{
    private GameplayMachine m_gm;
    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    private bool m_createdFlag;
    private bool m_deleteFlag;

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);
    }

    private void OnAfterGameplayObjectCreated(GMCore.AfterGameplayObjectCreatedParam createdParam)
    {
        m_createdFlag = true;
    }

    private void OnAfterGameplayObjectDeleted(GMCore.AfterGameplayObjectDeletedParam createdParam)
    {
        m_deleteFlag = true;
    }

    [Test]
    public void Test1()
    {
        m_gm.BindMachineEvent<GMCore.AfterGameplayObjectCreatedParam>(OnAfterGameplayObjectCreated);
        m_gm.BindMachineEvent<GMCore.AfterGameplayObjectDeletedParam>(OnAfterGameplayObjectDeleted);

        Player player = m_gm.CreateGameplayObject<Player>();
        bool deleteSuccess = m_gm.DeleteGameplayObject(player);

        if (deleteSuccess && m_createdFlag && m_deleteFlag)
        {
            Assert.Pass();
        }
        else
        {
            Assert.Fail($"CreateFlag {m_createdFlag}, DeleteFlag {m_deleteFlag}, DeleteSuccess {deleteSuccess}");
        }
    }

    [Test]
    public void Test2()
    {
        Player player = m_gm.CreateGameplayObject<Player>();
        bool deleteSuccess = m_gm.DeleteGameplayObject(player);

        try
        {
            player.Health = 10;

            Assert.Fail($"Player should have been deleted");
        }
        catch (Exception ex)
        {

        }

        Assert.Pass();
    }

    [Test]
    public void Test3()
    {
        Player player = m_gm.CreateGameplayObject<Player>();
        player.Return();

        try
        {
            player.Health = 10;

            Assert.Fail($"Player should have been deleted");
        }
        catch (Exception ex)
        {

        }

        Assert.Pass();
    }
}

public class Delayed_Create_Delete_ObjectTest
{
    private GameplayMachine m_gm;
    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    private bool m_createdFlag;
    private bool m_deleteFlag;

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);
    }

    private void OnAfterGameplayObjectCreated(GMCore.AfterGameplayObjectCreatedParam createdParam)
    {
        m_createdFlag = true;
    }

    private void OnAfterGameplayObjectDeleted(GMCore.AfterGameplayObjectDeletedParam createdParam)
    {
        m_deleteFlag = true;
    }

    [Test]
    public void Test1()
    {
        m_gm.BindMachineEvent<GMCore.AfterGameplayObjectCreatedParam>(OnAfterGameplayObjectCreated);
        m_gm.BindMachineEvent<GMCore.AfterGameplayObjectDeletedParam>(OnAfterGameplayObjectDeleted);

        m_gm.SetUseDelayedCallback(false);
        m_gm.Execute(new CreateDeleteParam()
        {

        });
        Assert.IsTrue(m_createdFlag);
        Assert.IsTrue(m_deleteFlag);

        m_createdFlag = false;
        m_deleteFlag = false;
        m_gm.SetUseDelayedCallback(true);
        m_gm.Execute(new CreateDeleteParam()
        {

        });
        Assert.IsFalse(m_createdFlag);
        Assert.IsFalse(m_deleteFlag);

        Assert.Pass();
    }
}

public class ReflectionTest
{
    private GameplayMachine m_gm;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);
    }

    [Test]
    public void Test1()
    {
        var meta = GameplayMachineBase.GetODClassMeta(GameplayMachine.GetODClassName<Player>());

        var val = new GameAttribute() { Value = 50 };
        var field = Player.GetODFieldName(Player.FieldNames.PlayerSkill);

        CommonGameplayObject obj = m_gm.CreateGameplayObject(meta.Name);
        m_gm.SetGameplayObjectValue(obj.ObjectID, field, val);

        var reflectionGet = m_gm.GetGameplayObjectValue<GameAttribute>(obj.ObjectID, field);
        if (!reflectionGet.Equals(val))
        {
            Assert.Fail($"Value check failed, current: {reflectionGet}, correct: {val}, relfection get value failed");
        }

        Player? _player = obj.Cast<Player>();
        if (_player == null)
        {
            Assert.Fail($"Cast failed, refelection creation failed");
        }

        Player player = _player.Value;
        if (!player.PlayerSkill.Equals(val))
        {
            Assert.Fail($"Value check failed, current: {player.PlayerSkill}, correct: {val}, relfection set value failed");
        }

        Assert.Pass();
    }
}

public class InheritTest
{
    private GameplayMachine m_gm;

    private Player m_player;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);
        m_player = m_gm.CreateGameplayObject<Player>();
    }

    [Test]
    public void Test1()
    {
        if (!m_player.IsA<Player>())
        {
            Assert.Fail($"Player should be a Player");
        }

        if (!m_player.IsA<Unit>())
        {
            Assert.Fail($"Player should be a Unit");
        }

        if (m_player.IsA<Monster>())
        {
            Assert.Fail($"Player should not be a Unit");
        }

        Unit basePlayer = m_player;
        if (!basePlayer.IsA<Player>())
        {
            Assert.Fail($"Player should be a Player");
        }

        if (!basePlayer.IsA<Unit>())
        {
            Assert.Fail($"Player should be a Unit");
        }

        if (basePlayer.IsA<Monster>())
        {
            Assert.Fail($"Player should not be a Unit");
        }

        Assert.Pass();
    }
}


public class GameEventTest
{
    private GameplayMachine m_gm;

    private Player m_playerUnit;
    private Monster m_monsterUnitA;
    private Unit m_unitA;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);
        m_playerUnit = m_gm.CreateGameplayObject<Player>();
        m_playerUnit.Health = 100;
        m_playerUnit.Attack = 10;
        m_playerUnit.PlayerSkill = 20;
        m_playerUnit.Name = "Player";

        m_monsterUnitA = m_gm.CreateGameplayObject<Monster>();
        m_monsterUnitA.Health = 80;
        m_monsterUnitA.Attack = 5;
        m_monsterUnitA.MonsterSkill = 15;
        m_monsterUnitA.Name = "MonsterA";

        m_unitA = m_gm.CreateGameplayObject<Unit>();
        m_unitA.Health = 10;
        m_unitA.Attack = 1;
        m_unitA.Name = "UnitA";
    }

    [Test]
    public void Test1()
    {
        m_gm.Execute(new DamageUnitParam()
        {
            A = m_playerUnit,
            B = m_monsterUnitA,
            Damage = 100,
        });

        GameAttribute afterHealth = 80 + 15 - 20;
        if (m_monsterUnitA.Health != afterHealth)
        {
            Assert.Fail($"{m_monsterUnitA.Name}'s health doesn't match, current: {m_monsterUnitA.Health}, correct: {afterHealth}");
        }

        Assert.Pass();
    }

    [Test]
    public void Test2()
    {
        m_gm.Execute(new DamageUnitParam()
        {
            A = m_unitA,
            B = m_unitA,
            Damage = 100,
        });

        GameAttribute afterHealth = 10 - 1;
        if (!m_unitA.Health.Equals(afterHealth))
        {
            Assert.Fail($"{m_unitA.Name}'s health doesn't match, current: {m_unitA.Health}, correct: {afterHealth}");
        }

        Assert.Pass();
    }

    [Test]
    public void Test3()
    {
        m_gm.Execute(new DamageUnitParam()
        {
            A = m_unitA,
            B = m_monsterUnitA,
            Damage = 100,
        });

        GameAttribute afterHealth = 80 - 1;
        if (!m_monsterUnitA.Health.Equals(afterHealth))
        {
            Assert.Fail($"{m_monsterUnitA.Name}'s health doesn't match, current: {m_monsterUnitA.Health}, correct: {afterHealth}");
        }

        Assert.Pass();
    }
}

public class RoutineTest
{
    #region Define

    public struct RoundRoutineParam : ICheckableRoutine
    {
        public Unit A;
        public Unit B;

        public EventError CanExecute(GameplayMachine.GameplayMachineProxy machine)
        {
            return true;
        }

        public IEnumerator DoExecute(GameplayMachine.GameplayMachineProxy machine)
        {
            yield return machine.RoutineExecute(new DamageUnitParam()
            {
                A = A,
                B = B,
            });
            yield return machine.RoutineExecute(new DamageUnitParam()
            {
                A = B,
                B = A,
            });
        }
    }

    #endregion

    private GameplayMachine m_gm;
    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    private Unit m_playerUnit;
    private Unit m_monsterUnit;

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);

        m_playerUnit = m_gm.CreateGameplayObject<Unit>();
        m_playerUnit.Health = 100;
        m_playerUnit.Attack = 10;
        m_playerUnit.Name = "UnitA";

        m_monsterUnit = m_gm.CreateGameplayObject<Unit>();
        m_monsterUnit.Health = 80;
        m_monsterUnit.Attack = 5;
        m_monsterUnit.Name = "UnitB";
    }

    [Test]
    public void Test1()
    {
        m_gm.Execute(new RoundRoutineParam()
        {
            A = m_playerUnit,
            B = m_monsterUnit,
        });

        int stepCount = 0;
        while (m_gm.StepRoutine())
        {
            stepCount++;
        }

        // 3 Interfaces / Routine were finished
        // 1: DamageUnitParam
        // 2: DamageUnitParam
        // 3: RoundRoutine
        if (stepCount != 3)
        {
            Assert.Fail($"Step count mismatch, Correct: {3}, Current: {stepCount}");
        }

        GameAttribute aAfterHealth = 100 - 5;
        GameAttribute bAfterHealth = 80 - 10;

        if (!m_playerUnit.Health.Equals(aAfterHealth) || !m_monsterUnit.Health.Equals(bAfterHealth))
        {
            Assert.Fail($"Health values don't match, Correct: {aAfterHealth}, {bAfterHealth}, Current: {m_playerUnit.Health}, {m_monsterUnit.Health}");
        }

        Assert.Pass();
    }

    [Test]
    public void Test2()
    {
        m_gm.Execute(new RoundRoutineParam()
        {
            A = m_playerUnit,
            B = m_monsterUnit,
        }, true);

        GameAttribute aAfterHealth = 100 - 5;
        GameAttribute bAfterHealth = 80 - 10;

        if (!m_playerUnit.Health.Equals(aAfterHealth) || !m_monsterUnit.Health.Equals(bAfterHealth))
        {
            Assert.Fail($"Health values don't match, Correct: {aAfterHealth}, {bAfterHealth}, Current: {m_playerUnit.Health}, {m_monsterUnit.Health}");
        }

        Assert.Pass();
    }
}

public class CacheModeTest
{
    private GameplayMachine m_gm;

    private GameAttribute AttackValue = 10;

    private byte[] m_saveFile;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        TestStatics.SetupNewTest();

        m_gm = GameplayMachineBase.SpawnGameplayMachine(TestStatics.TestKey, m_settings, TestGMModule.Instance);

        Game game = m_gm.GetGame();

        Player player = m_gm.CreateGameplayObject<Player>();
        game.Player = player;
        player.Attack = AttackValue;

        Monster monster = m_gm.CreateGameplayObject<Monster>();
        monster.Health = 42;

        game.A = monster;
    }

    [Test]
    public void Test1()
    {
        GameAttribute changeValue = 6;

        if (!m_gm.GetGame().Player.Value.Attack.Equals(AttackValue))
        {
            Assert.Fail("Player's Attack value doesn't match");
        }

        m_gm.EnterCacheMode();
        Player player = m_gm.GetGame().Player.Value;
        player.Attack = changeValue;

        if (m_gm.GetGame().Player.Value.Attack.Equals(AttackValue))
        {
            Assert.Fail("Player's Attack value doesn't match");
        }

        if (!m_gm.GetGame().Player.Value.Attack.Equals(changeValue))
        {
            Assert.Fail("Player's Attack value doesn't match");
        }

        m_saveFile = GameplayMachineBase.SaveGameplayMachine(m_gm);
        GameplayMachine loadGM = GameplayMachineBase.LoadGameplayMachine(TestStatics.TestKey, m_settings, m_saveFile, TestGMModule.Instance);

        if (!loadGM.GetGame().Player.Value.Attack.Equals(AttackValue))
        {
            Assert.Fail("Player's Attack value doesn't match after load");
        }

        Assert.Pass();
    }

    [Test]
    public void Test2()
    {
        GameAttribute changeValue = 6;

        m_gm.EnterCacheMode();
        Player player = m_gm.GetGame().Player.Value;
        player.Attack = changeValue;
        m_gm.LeaveCacheMode();

        m_saveFile = GameplayMachineBase.SaveGameplayMachine(m_gm);
        GameplayMachine loadGM = GameplayMachineBase.LoadGameplayMachine(TestStatics.TestKey, m_settings, m_saveFile, TestGMModule.Instance);

        if (!loadGM.GetGame().Player.Value.Attack.Equals(changeValue))
        {
            Assert.Fail("Player's Attack value doesn't match after load");
        }

        Assert.Pass();
    }

    [Test]
    public void Test3()
    {
        GameAttribute changeValue = 6;

        Game game = m_gm.GetGame();

        m_gm.DeleteGameplayObject(m_gm.GetGame().Player.Value);
        game.Player = null;

        m_gm.EnterCacheMode();
        Player player = m_gm.CreateGameplayObject<Player>();
        player.Attack = changeValue;
        game.Player = player;

        if (!game.Player.Value.Attack.Equals(changeValue))
        {
            Assert.Fail("Player's Attack value doesn't match after load");
        }

        m_saveFile = GameplayMachineBase.SaveGameplayMachine(m_gm);
        GameplayMachine loadGM = GameplayMachineBase.LoadGameplayMachine(TestStatics.TestKey, m_settings, m_saveFile, TestGMModule.Instance);

        if (loadGM.GetGame().Player != null)
        {
            Assert.Fail("Player should not exist");
        }

        Assert.Pass();
    }

    [Test]
    public void Test4()
    {
        GameAttribute changeValue = 6;

        Game game = m_gm.GetGame();

        m_gm.DeleteGameplayObject(m_gm.GetGame().Player.Value);
        game.Player = null;

        m_gm.EnterCacheMode();
        Player player = m_gm.CreateGameplayObject<Player>();
        player.Attack = changeValue;
        m_gm.DeleteGameplayObject(player);
        try
        {
            player.Attack = changeValue;
            var attackVal = player.Attack;

            Assert.Fail($"Player should be delete already, set or get should cause exception");
        }
        catch (Exception ex)
        {

        }

        Assert.Pass();
    }
}

public class BasicTest
{
    private GameplayMachine m_gm;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);
    }

    [Test]
    public void Test1()
    {
        try
        {
            Game game = m_gm.CreateGameplayObject<Game>();
            m_gm.SetGameplayObjectValue(game.ObjectID, new ODFieldName() { NameObject = Game.FieldNames.Units }, new GMCore.Collections.SetCollection<Unit>());

            Assert.Fail();
        }
        catch (Exception ex)
        {

        }

        Assert.Pass();
    }
}

public class CallBackTest
{
    private GameplayMachine m_gm;

    private Player m_player;
    private GameAttribute m_afterHealth;

    private int m_triggerTimes;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);

        m_player = m_gm.CreateGameplayObject<Player>();
        m_player.Health = 100;
        m_player.PlayerSkill = 10;
    }

    [Test]
    public void Test1()
    {
        m_player.AfterHealthChanged.Bind(delegate (object context)
        {
            m_afterHealth = m_player.Health;
            Assert.IsTrue(m_gm.DuringExecution);
        });
        m_gm.Execute(new DamageUnitParam()
        {
            A = m_player,
            B = m_player,
            Damage = 10,
        });
        Assert.IsTrue(m_afterHealth == 90);

        Assert.Pass();
    }

    [Test]
    public void Test2()
    {
        m_triggerTimes = 0;
        m_player.AfterHealthChanged.Bind(delegate (object context)
        {
            m_triggerTimes++;
        });
        GameAttribute curHealth = m_player.Health;
        m_gm.Execute(new SetUnitHealthsParam()
        {
            Unit = m_player,
            Healths = new List<GameAttribute>() { 90, 80, 3, 5 },
        });
        Assert.IsTrue(m_triggerTimes == 4);

        m_triggerTimes = 0;
        m_gm.Execute(new SetUnitHealthsParam()
        {
            Unit = m_player,
            Healths = new List<GameAttribute>() { 90, 80, 3, 5 },
        });
        Assert.IsTrue(m_triggerTimes == 4);

        Assert.Pass();
    }

    [Test]
    public void Test3()
    {
        m_triggerTimes = 0;
        m_player.AfterAttackChanged.Bind(delegate (object context)
        {
            m_triggerTimes++;
        });
        GameAttribute curAttack = m_player.Attack;
        m_player.Attack = curAttack;

        Assert.IsTrue(m_triggerTimes == 0);

        Assert.Pass();
    }
}

public class DelayedCallBackTest
{
    private GameplayMachine m_gm;

    private Player m_player;
    private GameAttribute m_afterHealth;

    private int m_triggerTimes;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);

        m_player = m_gm.CreateGameplayObject<Player>();
        m_player.Health = 100;
        m_player.PlayerSkill = 10;

        m_gm.SetUseDelayedCallback(true);
    }

    [Test]
    public void Test1()
    {
        m_player.AfterHealthChanged.Bind(delegate (object context)
        {
            m_afterHealth = m_player.Health;
            Assert.IsFalse(m_gm.DuringExecution);
        });
        m_gm.Execute(new DamageUnitParam()
        {
            A = m_player,
            B = m_player,
            Damage = 10,
        });
        Assert.IsTrue(m_afterHealth == 90);

        Assert.Pass();
    }

    [Test]
    public void Test2()
    {
        m_triggerTimes = 0;
        m_player.AfterHealthChanged.Bind(delegate (object context)
        {
            m_triggerTimes++;
            Assert.IsFalse(m_gm.DuringExecution);
        });
        GameAttribute curHealth = m_player.Health;
        m_gm.Execute(new SetUnitHealthsParam()
        {
            Unit = m_player,
            Healths = new List<GameAttribute>() { 90, 80, 3, curHealth },
        });
        Assert.IsTrue(m_triggerTimes == 0);

        m_triggerTimes = 0;
        m_gm.Execute(new SetUnitHealthsParam()
        {
            Unit = m_player,
            Healths = new List<GameAttribute>() { 90, 80, 3, curHealth + 1 },
        });
        Assert.IsTrue(m_triggerTimes == 1);

        Assert.Pass();
    }

    [Test]
    public void Test3()
    {
        m_triggerTimes = 0;
        m_player.AfterHealthChanged.Bind(delegate (object context)
        {
            m_triggerTimes++;
            Assert.IsFalse(m_gm.DuringExecution);
        });

        GameAttribute curHealth = m_player.Health;
        m_gm.Execute(new SetUnitHealthsParam()
        {
            Unit = m_player,
            Healths = new List<GameAttribute>() { 90, 80, 3, curHealth },
        });
        Assert.IsTrue(m_triggerTimes == 0);

        curHealth = m_player.Health;
        m_triggerTimes = 0;
        m_gm.Execute(new SetUnitHealthsParam()
        {
            Unit = m_player,
            Healths = new List<GameAttribute>() { 90, 80, 0, curHealth + 1 },
        });
        Assert.IsTrue(m_triggerTimes == 0); // Becuase its deleted

        Assert.Pass();
    }
}

public class MachineEventTest
{
    private GameplayMachine m_gm;

    private int m_listValue;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);
    }

    [Test]
    public void Test1()
    {
        int value = 5;
        var handle = m_gm.BindMachineEvent(delegate (TestEventParam context)
        {
            m_listValue = context.TestValue;
            Assert.IsTrue(m_gm.DuringExecution);
        });
        m_gm.Execute(new TriggerEventParam()
        {
            TriggerValue = value,
        });
        Assert.IsTrue(m_listValue == value);

        handle.Return();
        m_gm.Execute(new TriggerEventParam()
        {
            TriggerValue = 6,
        });
        Assert.IsTrue(m_listValue == value);

        Assert.Pass();
    }
}

public class DelayedMachineEventTest
{
    private GameplayMachine m_gm;

    private int m_listValue;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);
        m_gm.SetUseDelayedCallback(true);
    }

    [Test]
    public void Test1()
    {
        int value = 5;
        var handle = m_gm.BindMachineEvent(delegate (TestEventParam context)
        {
            m_listValue = context.TestValue;
            Assert.IsFalse(m_gm.DuringExecution);
        });
        m_gm.Execute(new TriggerEventParam()
        {
            TriggerValue = value,
        });
        Assert.IsTrue(m_listValue == value);

        handle.Return();
        m_gm.Execute(new TriggerEventParam()
        {
            TriggerValue = 6,
        });
        Assert.IsTrue(m_listValue == value);

        Assert.Pass();
    }
}

public class DrivenFieldTest
{
    private GameplayMachine m_gm;

    private Player m_player;
    private bool m_nearDeathFlag;
    private bool m_statusFlag;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);
        m_player = m_gm.CreateGameplayObject<Player>();
    }

    [Test]
    public void Test1()
    {
        m_player.Health = 100;
        if (m_player.IsNearDeath)
        {
            Assert.Fail($"Player should not be NearDeath");
        }

        m_player.AfterIsNearDeathChanged.Bind(delegate (object conetxt)
        {
            m_nearDeathFlag = true;
        });

        m_player.AfterUnitStatusChanged.Bind(delegate (object conetxt)
        {
            m_statusFlag = true;
            Console.WriteLine(m_player.UnitStatus);
        });

        m_player.Health = -1;
        if (!m_player.IsNearDeath)
        {
            Assert.Fail($"Player should be NearDeath");
        }

        if (!m_nearDeathFlag)
        {
            Assert.Fail($"NearDeathFlag is not up");
        }

        if (!m_statusFlag)
        {
            Assert.Fail($"UnitStatusFlag is not up");
        }

        Assert.Pass();
    }
}

public class CheckModifyTest
{
    private GameplayMachine m_gm;

    private Monster m_testMonster;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);
        m_testMonster = m_gm.CreateGameplayObject<Monster>();
        m_gm.SetModifyDataInsideExecutionOnly(true);
    }

    [Test]
    public void Test1()
    {
        try
        {
            m_gm.CreateGameplayObject<Player>();
            Assert.Fail();
        }
        catch
        { 
        
        }

        try
        {
            m_testMonster.Health = 24242;
            Assert.Fail();
        }
        catch
        {

        }

        try
        {
            // Even intentions are not permitted
            m_testMonster.Health = m_testMonster.Health;
            Assert.Fail();
        }
        catch
        {

        }

        try
        {
            m_gm.GetGame().Units.Add(m_testMonster);
            Assert.Fail();
        }
        catch
        {

        }

        try
        {
            m_testMonster.Name = "DisplayName";
        }
        catch
        {
            Assert.Fail();
        }

        Assert.Pass();
    }
}
