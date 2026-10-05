using ODCore;
using System.Collections;
using System.Runtime;
using TestGM_OD.Interfaces;

namespace Test;

public class MapCollectionTest
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
        Unit testUnit = m_gm.CreateGameplayObject<Player>();
        var units = m_gm.GetGame().IDUnits;

        Assert.IsTrue(units.Count == 0);
        units.Add(1, testUnit);
        Assert.IsTrue(units.Count == 1);
        units.Remove(1);
        Assert.IsTrue(units.Count == 0);

        units.Add(1, testUnit);
        units.Add(1, testUnit);
        Assert.IsTrue(units.Count == 1);
        units.Add(2, testUnit);
        Assert.IsTrue(units.Count == 2);
        units.Clear();
        Assert.IsTrue(units.Count == 0);

        units[1] = testUnit;
        units[1] = testUnit;
        Assert.IsTrue(units.Count == 1);
        units[2] = units[1];
        Assert.IsTrue(units.Count == 2);
        try
        {
            units[2] = units[90];
            Assert.Fail();
        }
        catch
        {

        }

        Assert.Pass();
    }
}

public class MapCollectionCallBackTest
{
    private GameplayMachine m_gm;

    int m_addFlag;
    int m_removeFlag;
    int m_keyChangedFlag;
    int m_withKeyChangedFlag;

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
        Unit testUnit = m_gm.CreateGameplayObject<Player>();
        Unit testUnit2 = m_gm.CreateGameplayObject<Monster>();

        testUnit.Health = 1;
        testUnit2.Health = 2;
        var units = m_gm.GetGame().IDUnits;

        ICollectionFieldObject<int, Unit> fieldObject = units;
        var binder = fieldObject.GetBinder();

        XLifetimeObject.LifetimeObject life = new XLifetimeObject.LifetimeObject();

        int withKey = 5;

        binder.BindItemChanged(delegate (CollectionItemChangedParam<int> change)
        {
            m_keyChangedFlag++;
            if (change.ChangeType == CollectionItemChangeType.Add)
                m_addFlag++;
            else if (change.ChangeType == CollectionItemChangeType.Remove)
                m_removeFlag++;
            if (change.Item == withKey)
                m_withKeyChangedFlag++;
        }).BindTo(life);

        units.Add(1, testUnit);
        Assert.IsTrue(m_addFlag == 1);
        Assert.IsTrue(m_removeFlag == 0);
        Assert.IsTrue(m_keyChangedFlag == m_addFlag + m_removeFlag + 0);
        m_addFlag = 0;
        m_removeFlag = 0;
        m_keyChangedFlag = 0;

        units.Remove(1);
        Assert.IsTrue(m_addFlag == 0);
        Assert.IsTrue(m_removeFlag == 1);
        Assert.IsTrue(m_keyChangedFlag == m_addFlag + m_removeFlag + 0);
        m_addFlag = 0;
        m_removeFlag = 0;
        m_keyChangedFlag = 0;

        units[1] = testUnit;
        units[1] = testUnit2;
        Assert.IsTrue(m_addFlag == 1);
        Assert.IsTrue(m_removeFlag == 0);
        Assert.IsTrue(m_keyChangedFlag == m_addFlag + m_removeFlag + 1);
        units[1] = testUnit;
        Assert.IsTrue(m_keyChangedFlag == m_addFlag + m_removeFlag + 2);
        m_addFlag = 0;
        m_removeFlag = 0;
        m_keyChangedFlag = 0;

        units[2] = testUnit;
        units.Clear();
        Assert.IsTrue(m_addFlag == 1);
        Assert.IsTrue(m_removeFlag == 2);
        Assert.IsTrue(m_keyChangedFlag == m_addFlag + m_removeFlag + 0);

        m_addFlag = 0;
        m_removeFlag = 0;
        m_keyChangedFlag = 0;
        m_withKeyChangedFlag = 0;
        units[withKey - 1] = testUnit;
        units[withKey] = testUnit;
        units[withKey] = testUnit2;
        units.Clear();
        Assert.IsTrue(m_addFlag == 2);
        Assert.IsTrue(m_removeFlag == 2);
        Assert.IsTrue(m_keyChangedFlag == m_addFlag + m_removeFlag + 1);
        Assert.IsTrue(m_withKeyChangedFlag == 3);

        Assert.Pass();
    }
}

public class MapCollectionCallBackTestWithDelay
{
    private GameplayMachine m_gm;

    int m_changedFlag;
    int m_addFlag;
    int m_removeFlag;
    int m_keyChangedFlag;
    int m_withKeyChangedFlag;

    Unit testUnit;
    Unit testUnit2;

    private GameplayMachineSettings m_settings = new GameplayMachineSettings()
    {

    };

    [SetUp]
    public void Setup()
    {
        m_gm = GameplayMachineBase.SpawnGameplayMachine(null, m_settings, TestGMModule.Instance);
        m_gm.GetGame();
        testUnit = m_gm.CreateGameplayObject<Player>();
        testUnit2 = m_gm.CreateGameplayObject<Monster>();
        testUnit.Health = 1;
        testUnit2.Health = 2;
        m_gm.SetUseDelayedCallback(true);
    }

    [Test]
    public void Test1()
    {
        var units = m_gm.GetGame().IDUnits;

        ICollectionFieldObject<int, Unit> fieldObject = units;
        var binder = fieldObject.GetBinder();

        XLifetimeObject.LifetimeObject life = new XLifetimeObject.LifetimeObject();

        int withKey = 5;

        binder.BindItemChanged(delegate (CollectionItemChangedParam<int> change)
        {
            m_keyChangedFlag++;
            if (change.ChangeType == CollectionItemChangeType.Add)
                m_addFlag++;
            else if (change.ChangeType == CollectionItemChangeType.Remove)
                m_removeFlag++;
            if (change.Item == withKey)
                m_withKeyChangedFlag++;
        }).BindTo(life);

        binder.BindChanged(delegate (object context)
        {
            m_changedFlag++;
        });

        m_gm.Execute(new TestUnitsParam()
        {
            Adds = new  List<KVPair<int, Unit>> {
                new KVPair<int, Unit>(1, testUnit),
                new KVPair<int, Unit>(2, testUnit2),
            },
            Removes = new List<int>() { },
        });
        Assert.IsTrue(m_changedFlag == 1);
        Assert.IsTrue(m_addFlag == 2);
        Assert.IsTrue(m_removeFlag == 0);
        Assert.IsTrue(m_keyChangedFlag == m_addFlag + m_removeFlag + 0);
        m_changedFlag = 0;
        m_addFlag = 0;
        m_removeFlag = 0;
        m_keyChangedFlag = 0;

        m_gm.Execute(new TestUnitsParam()
        {
            Adds = new List<KVPair<int, Unit>> {
                new KVPair<int, Unit>(1, testUnit),
                new KVPair<int, Unit>(2, testUnit2),
            },
            Removes = new List<int>() { },
        });
        Assert.IsTrue(m_changedFlag == 0);
        Assert.IsTrue(m_addFlag == 0);
        Assert.IsTrue(m_removeFlag == 0);
        Assert.IsTrue(m_keyChangedFlag == m_addFlag + m_removeFlag + 0);
        m_changedFlag = 0;
        m_addFlag = 0;
        m_removeFlag = 0;
        m_keyChangedFlag = 0;

        m_gm.Execute(new TestUnitsParam()
        {
            Adds = new List<KVPair<int, Unit>> { },
            Removes = new List<int>() { 1, 2 },
        });
        Assert.IsTrue(m_changedFlag == 1);
        Assert.IsTrue(m_addFlag == 0);
        Assert.IsTrue(m_removeFlag == 2);
        Assert.IsTrue(m_keyChangedFlag == m_addFlag + m_removeFlag + 0);
        m_changedFlag = 0;
        m_addFlag = 0;
        m_removeFlag = 0;
        m_keyChangedFlag = 0;

        m_addFlag = 0;
        m_removeFlag = 0;
        m_keyChangedFlag = 0;
        m_withKeyChangedFlag = 0;
        Assert.IsTrue(units.Count == 0);
        m_gm.Execute(new TestUnitsParam()
        {
            Adds = new List<KVPair<int, Unit>> {
                new KVPair<int, Unit>(withKey - 1, testUnit),
                new KVPair<int, Unit>(withKey, testUnit),
                new KVPair<int, Unit>(withKey, testUnit2),
            },
            Removes = new List<int>() { withKey - 1, withKey },
        });
        Assert.IsTrue(m_addFlag == 0);
        Assert.IsTrue(m_removeFlag == 0);
        Assert.IsTrue(m_keyChangedFlag == 0);
        Assert.IsTrue(m_withKeyChangedFlag == 0);

        Assert.Pass();
    }
}

