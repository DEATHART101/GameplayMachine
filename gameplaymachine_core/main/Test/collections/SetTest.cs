using ODCore;
using System.Collections;
using System.Runtime;
using TestGM_OD.Interfaces;

namespace Test;



public class CollectionTest
{
    private GameplayMachine m_gm;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);
        m_gm.GetGame();

        m_gm.GetGame().Units.Add(m_gm.CreateGameplayObject<Player>());
    }

    [Test]
    public void Test1()
    {
        Assert.IsTrue(m_gm.GetGame().Units.Count == 1);

        Assert.Pass();
    }
}

public class CollectionSetTest
{
    private GameplayMachine m_gm;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);
        m_gm.GetGame();

        m_gm.GetGame().Units.Add(m_gm.CreateGameplayObject<Player>());
    }

    [Test]
    public void Test1()
    {
        Game game = m_gm.GetGame();
        Assert.IsTrue(m_gm.GetGame().Units.Count == 1);
        game.Units = game.Units;
        Assert.IsTrue(m_gm.GetGame().Units.Count == 1);
        game.Units2 = game.Units;
        Assert.IsTrue(m_gm.GetGame().Units2.Count == 1);
        game.Units.Clear();
        Assert.IsTrue(m_gm.GetGame().Units2.Count == 1);

        Assert.Pass();
    }
}

public class CollectionSetCallBackTest
{
    private GameplayMachine m_gm;

    private int m_addFlag;
    private int m_changedFlag;
    private int m_removeFlag;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);
        m_gm.GetGame();
    }

    [Test]
    public void Test1()
    {
        var unitBinder = m_gm.GetGame().Units2Binder;
        unitBinder.BindItemChanged(delegate (CollectionItemChangedParam<Unit> change)
        {
            if (change.ChangeType == CollectionItemChangeType.Add)
                m_addFlag++;
            else if (change.ChangeType == CollectionItemChangeType.Remove)
                m_removeFlag++;
        });
        unitBinder.BindChanged(delegate (object context)
        {
            m_changedFlag++;
        });
        m_gm.GetGame().Units.Add(m_gm.CreateGameplayObject<Monster>());
        m_gm.GetGame().Units.Add(m_gm.CreateGameplayObject<Monster>());

        Game game = m_gm.GetGame();
        game.Units2 = game.Units;
        Assert.IsTrue(m_addFlag == 2);
        Assert.IsTrue(m_changedFlag == 1);

        m_addFlag = 0;
        m_changedFlag = 0;
        m_removeFlag = 0;
        game.Units.Clear();
        Assert.IsTrue(game.Units2.Count == 2);
        Assert.IsTrue(m_removeFlag == 0);

        game.Units2 = game.Units;
        Assert.IsTrue(m_removeFlag == 2);
        Assert.IsTrue(game.Units2.Count == 0);

        Assert.Pass();
    }
}

public class CollectionTestWithCache
{
    private GameplayMachine m_gm;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);
        m_gm.GetGame();
    }

    [Test]
    public void Test1()
    {
        Assert.IsTrue(m_gm.GetGame().Units.Count == 0);
        m_gm.GetGame().Units.Add(m_gm.CreateGameplayObject<Monster>());
        m_gm.GetGame().Units.Add(m_gm.CreateGameplayObject<Monster>());
        m_gm.GetGame().Units.Add(m_gm.CreateGameplayObject<Monster>());
        Assert.IsTrue(m_gm.GetGame().Units.Count == 3);

        m_gm.EnterCacheMode();
        m_gm.GetGame().Units.Clear();
        Assert.IsTrue(m_gm.GetGame().Units.Count == 0);
        m_gm.LeaveCacheMode();

        Assert.IsTrue(m_gm.GetGame().Units.Count == 0);

        Assert.Pass();
    }

    [Test]
    public void Test2()
    {
        Assert.IsTrue(m_gm.GetGame().Units.Count == 0);
        m_gm.EnterCacheMode();
        Assert.IsTrue(m_gm.GetGame().Units.Count == 0);
        m_gm.GetGame().Units.Add(m_gm.CreateGameplayObject<Monster>());
        m_gm.GetGame().Units.Add(m_gm.CreateGameplayObject<Monster>());
        m_gm.GetGame().Units.Add(m_gm.CreateGameplayObject<Monster>());
        Assert.IsTrue(m_gm.GetGame().Units.Count == 3);
        m_gm.LeaveCacheMode();
        Assert.IsTrue(m_gm.GetGame().Units.Count == 3);

        Assert.Pass();
    }

    [Test]
    public void Test3()
    {
        Monster a = m_gm.CreateGameplayObject<Monster>();
        Monster b = m_gm.CreateGameplayObject<Monster>();

        m_gm.GetGame().Units.Add(a);
        m_gm.EnterCacheMode();
        m_gm.GetGame().Units.Add(b);
        Assert.IsTrue(m_gm.GetGame().Units.Count == 2);
        m_gm.GetGame().Units.Remove(a);
        Assert.IsTrue(m_gm.GetGame().Units.Count == 1);
        m_gm.LeaveCacheMode();
        Assert.IsTrue(m_gm.GetGame().Units.Count == 1);
        foreach (Unit item in m_gm.GetGame().Units)
        {
            Assert.IsTrue(GameplayMachine.IsSame<Unit>(item, b));
        }

        Assert.Pass();
    }
}

public class CollectionCallbackTest
{
    private GameplayMachine m_gm;

    private int m_addFlag;
    private int m_changedFlag;
    private int m_removeFlag;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);
        m_gm.GetGame();
    }

    [Test]
    public void Test1()
    {
        XLifetimeObject.LifetimeObject life = new XLifetimeObject.LifetimeObject();
        CollectionBinder<Unit> unitBinder = (m_gm.GetGame().Units as ICollectionFieldObject<Unit>).GetBinder();

        unitBinder.BindItemChanged(delegate (CollectionItemChangedParam<Unit> change)
        {
            if (change.ChangeType == CollectionItemChangeType.Add)
                m_addFlag++;
            else if (change.ChangeType == CollectionItemChangeType.Remove)
                m_removeFlag++;
        }).BindTo(life);
        m_gm.GetGame().UnitsBinder.BindChanged(delegate (object context)
        {
            m_changedFlag++;
        }).BindTo(life);
        m_gm.GetGame().Units.Add(m_gm.CreateGameplayObject<Player>());
        Assert.IsTrue(m_addFlag > 0);
        Assert.IsTrue(m_changedFlag > 0);
        Assert.IsTrue(m_removeFlag == 0);

        m_gm.GetGame().Units.Clear();
        Assert.IsTrue(m_removeFlag > 0);

        life.Return();
        m_addFlag = 0;
        m_changedFlag = 0;
        m_removeFlag = 0;
        m_gm.GetGame().Units.Add(m_gm.CreateGameplayObject<Player>());
        Assert.IsTrue(m_gm.GetGame().Units.Count == 1);
        m_gm.GetGame().Units.Clear();
        Assert.IsTrue(m_gm.GetGame().Units.Count == 0);
        Assert.IsTrue(m_addFlag == 0);
        Assert.IsTrue(m_changedFlag == 0);
        Assert.IsTrue(m_removeFlag == 0);

        Assert.Pass();
    }
}

public class CollectionCallbackTest2
{
    private GameplayMachine m_gm;

    private int m_addFlag;
    private int m_changedFlag;
    private int m_removeFlag;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);
        m_gm.GetGame();
    }

    [Test]
    public void Test2()
    {
        CollectionBinder<Unit> unitBinder = (m_gm.GetGame().Units as ICollectionFieldObject<Unit>).GetBinder();

        unitBinder.BindItemChanged(delegate (CollectionItemChangedParam<Unit> change)
        {
            if (change.ChangeType == CollectionItemChangeType.Add)
                m_addFlag++;
            else if (change.ChangeType == CollectionItemChangeType.Remove)
                m_removeFlag++;
        });
        unitBinder.BindChanged(delegate (object context)
        {
            m_changedFlag++;
        });
        m_gm.GetGame().Units.Add(m_gm.CreateGameplayObject<Player>());
        m_gm.GetGame().Units.Add(m_gm.CreateGameplayObject<Player>());
        Assert.IsTrue(m_addFlag == 2);
        Assert.IsTrue(m_changedFlag == 2);

        m_addFlag = 0;
        m_changedFlag = 0;
        m_gm.GetGame().Units.Clear();
        Assert.IsTrue(m_removeFlag == 2);
        Assert.IsTrue(m_changedFlag == 1);

        Assert.Pass();
    }
}

public class CollectionCallbackTest3
{
    private GameplayMachine m_gm;

    private int m_addFlag;
    private int m_changedFlag;
    private int m_removeFlag;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);
        m_gm.GetGame();
    }

    [Test]
    public void Test3()
    {
        m_gm.SetUseDelayedCallback(true);

        CollectionBinder<Unit> unitBinder = (m_gm.GetGame().Units as ICollectionFieldObject<Unit>).GetBinder();

        unitBinder.BindItemChanged(delegate (CollectionItemChangedParam<Unit> change)
        {
            if (change.ChangeType == CollectionItemChangeType.Add)
                m_addFlag++;
            else if (change.ChangeType == CollectionItemChangeType.Remove)
                m_removeFlag++;
            Assert.IsFalse(m_gm.DuringExecution);
        });
        unitBinder.BindChanged(delegate (object context)
        {
            m_changedFlag++;
            Assert.IsFalse(m_gm.DuringExecution);
        });
        m_gm.Execute(new CreateMonstersParam()
        {
            Count = 2,
        });
        Assert.IsTrue(m_addFlag == 2);
        Assert.IsTrue(m_changedFlag == 1);

        Assert.Pass();
    }
}
