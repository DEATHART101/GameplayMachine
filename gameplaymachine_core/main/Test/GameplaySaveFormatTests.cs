using System.Text;

namespace Test;

public class GameplaySaveFormatTests
{
    private static readonly GameplayMachineSettings s_settings = new GameplayMachineSettings
    {
        Serializers = new List<IGMSerializer>
        {
            new TestCommon.MyCustomObjectSurrogate(),
        },
    };

    private GameplayMachine m_machine;

    [Test]
    public void ReplicationEnumsUseTheProtocolOrder()
    {
        Assert.Multiple(() =>
        {
            Assert.That((int)GameplayObjectReplicationMode.None, Is.EqualTo(0));
            Assert.That((int)GameplayObjectReplicationMode.OwnerOnly, Is.EqualTo(1));
            Assert.That((int)GameplayObjectReplicationMode.AllClients, Is.EqualTo(2));
            Assert.That((int)ODFieldReplicationMode.None, Is.EqualTo(0));
            Assert.That((int)ODFieldReplicationMode.OwnerOnly, Is.EqualTo(1));
            Assert.That((int)ODFieldReplicationMode.AllClients, Is.EqualTo(2));
        });
    }

    [SetUp]
    public void SetUp()
    {
        TestStatics.SetupNewTest();
        m_machine = GameplayMachineBase.SpawnGameplayMachine(
            TestStatics.TestKey,
            s_settings,
            TestGMModule.Instance);
    }

    [Test]
    public void SaveUsesGeneratedGms2Format()
    {
        Assert.That(m_machine.GetAllGameplayObjects().Single().ObjectClass,
            Is.EqualTo(PlayerController.ClassName));

        byte[] bytes = GameplayMachineBase.SaveGameplayMachine(m_machine);

        Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("GMS2"));
        Assert.That(BitConverter.ToUInt16(bytes, 4), Is.EqualTo(9));
        Assert.That(Encoding.UTF8.GetString(bytes), Does.Not.Contain("TestGM.dll"));
        Assert.That(Encoding.UTF8.GetString(bytes), Does.Not.Contain("GameplayMachineCore"));
    }

    [Test]
    public void SaveRestoresLocalPlayerController()
    {
        PlayerController original = m_machine.GetLocalPlayerController();
        byte[] bytes = GameplayMachineBase.SaveGameplayMachine(m_machine);
        TestStatics.SetupNewTest();

        GameplayMachine loaded = GameplayMachineBase.LoadGameplayMachine(
            TestStatics.TestKey, s_settings, bytes, TestGMModule.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.GetLocalPlayerController().ObjectID, Is.EqualTo(original.ObjectID));
            Assert.That(loaded.GetPlayerControllers().Select(item => item.ObjectID),
                Is.EqualTo(new[] { original.ObjectID }));
        });
    }

    [Test]
    public void SaveRestoresPossessedPawnAndOwnership()
    {
        PlayerController playerController = m_machine.GetLocalPlayerController();
        Pawn pawn = m_machine.CreateGameplayObject<Pawn>();
        CheckableInterfaceResult possessResult = m_machine.Execute(new PossessParam
        {
            PlayerController = playerController,
            Pawn = pawn,
        });
        Assert.That(possessResult.Sussceeded, Is.True, possessResult.ErrorMessage.Error);

        byte[] bytes = GameplayMachineBase.SaveGameplayMachine(m_machine);
        TestStatics.SetupNewTest();
        GameplayMachine loaded = GameplayMachineBase.LoadGameplayMachine(
            TestStatics.TestKey, s_settings, bytes, TestGMModule.Instance);
        PlayerController loadedController = loaded.GetLocalPlayerController();
        Pawn loadedPawn = loaded.GetGameplayObject<Pawn>(pawn.ObjectID);

        Assert.Multiple(() =>
        {
            Assert.That(loadedController.Pawn?.ObjectID, Is.EqualTo(loadedPawn.ObjectID));
            Assert.That(loadedPawn.PlayerController?.ObjectID, Is.EqualTo(loadedController.ObjectID));
            Assert.That(loaded.GetNetworkOwnerID(loadedPawn), Is.EqualTo(loadedController.ObjectID));
        });
    }

    [Test]
    public void SaveRoundTripsGameplayObjectNetworkMetadata()
    {
        PlayerController playerController = m_machine.GetLocalPlayerController();
        Monster monster = m_machine.CreateGameplayObject<Monster>();
        m_machine.Execute(new SetNetworkOwnerParam
        {
            Object = Common(monster.ObjectID),
            Owner = Common(playerController.ObjectID),
        });

        byte[] bytes = GameplayMachineBase.SaveGameplayMachine(m_machine);
        TestStatics.SetupNewTest();
        GameplayMachine loaded = GameplayMachineBase.LoadGameplayMachine(
            TestStatics.TestKey, s_settings, bytes, TestGMModule.Instance);
        Monster loadedMonster = loaded.GetGameplayObject<Monster>(monster.ObjectID);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.GetNetworkOwnerID(loadedMonster), Is.EqualTo(playerController.ObjectID));
            Assert.That(loaded.GetGameplayObjectReplicationMode(loadedMonster),
                Is.EqualTo(GameplayObjectReplicationMode.AllClients));
        });
    }

    [Test]
    public void CorruptedSaveIsRejectedBeforeLoad()
    {
        byte[] bytes = GameplayMachineBase.SaveGameplayMachine(m_machine);
        bytes[8] ^= 0x40;
        TestStatics.SetupNewTest();

        Assert.Throws<InvalidDataException>(() => GameplayMachineBase.LoadGameplayMachine(
            TestStatics.TestKey,
            s_settings,
            bytes,
            TestGMModule.Instance));
    }

    private CommonGameplayObject Common(GObjectID objectID) => new()
    {
        Machine = m_machine,
        ObjectID = objectID,
    };

    [Test]
    public void GeneratedCodecsRestoreCollectionsReferencesAndAllocator()
    {
        Game game = m_machine.GetGame();
        Player player = m_machine.CreateGameplayObject<Player>();
        player.Name = "saved player";
        game.Player = player;
        game.IntArray.Add(4);
        game.IntArray.Add(9);
        game.Units.Add(player);
        game.IDUnits.Add(27, player);

        byte[] bytes = GameplayMachineBase.SaveGameplayMachine(m_machine);
        TestStatics.SetupNewTest();
        GameplayMachine loaded = GameplayMachineBase.LoadGameplayMachine(
            TestStatics.TestKey,
            s_settings,
            bytes,
            TestGMModule.Instance);

        Game loadedGame = loaded.GetGame();
        Player loadedPlayer = loadedGame.Player.Value;
        Assert.That(loadedPlayer.ObjectID, Is.EqualTo(player.ObjectID));
        Assert.That(loadedPlayer.Machine, Is.SameAs(loaded));
        Assert.That(loadedPlayer.Name, Is.EqualTo("saved player"));
        Assert.That(loadedGame.IntArray, Is.EqualTo(new[] { 4, 9 }));
        Assert.That(loadedGame.Units.Count, Is.EqualTo(1));
        Assert.That(loadedGame.IDUnits.TryGet(27, out Unit mappedPlayer), Is.True);
        Assert.That(mappedPlayer.ObjectID, Is.EqualTo(player.ObjectID));
        Assert.That(mappedPlayer.Machine, Is.SameAs(loaded));

        Player nextPlayer = loaded.CreateGameplayObject<Player>();
        Assert.That(nextPlayer.ObjectID.ID, Is.GreaterThan(player.ObjectID.ID));
    }

    [Test]
    public void SaveRestoresRootObject()
    {
        Game game = m_machine.GetGame();
        byte[] bytes = GameplayMachineBase.SaveGameplayMachine(m_machine);
        TestStatics.SetupNewTest();

        GameplayMachine loaded = GameplayMachineBase.LoadGameplayMachine(
            TestStatics.TestKey,
            s_settings,
            bytes,
            TestGMModule.Instance);

        Assert.That(loaded.HasRoot, Is.True);
        Assert.That(loaded.GetRoot().ObjectID, Is.EqualTo(game.ObjectID));
        Assert.That(loaded.GetRoot().Machine, Is.SameAs(loaded));
    }

    [Test]
    public void DeletingRootClearsRootReference()
    {
        Game game = m_machine.GetGame();

        Assert.That(m_machine.DeleteGameplayObject(game.ObjectID), Is.True);
        Assert.That(m_machine.HasRoot, Is.False);
        Assert.Throws<InvalidOperationException>(() => m_machine.GetRoot());
    }
}
