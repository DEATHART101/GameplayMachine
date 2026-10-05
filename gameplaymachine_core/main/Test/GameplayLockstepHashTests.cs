namespace Test;

public class GameplayLockstepHashTests
{
    private GameplayMachine m_machine;

    [SetUp]
    public void SetUp()
    {
        TestStatics.SetupNewTest();
        m_machine = GameplayMachineBase.SpawnGameplayMachine(
            TestStatics.TestKey,
            new GameplayMachineSettings
            {
                Serializers = new List<IGMSerializer>
                {
                    new TestCommon.MyCustomObjectSurrogate(),
                },
            },
            TestGMModule.Instance);
    }

    [Test]
    public void DirtyObjectUpdateProducesTheSameRootAsAFullRebuild()
    {
        Player player = m_machine.CreateGameplayObject<Player>();
        player.Name = "before";
        var tracker = new LockstepStateHashTracker(m_machine);
        tracker.Initialize();
        ulong before = tracker.CurrentHash;

        player.Name = "after";
        tracker.MarkObject(player.ObjectID);
        ulong incremental = tracker.CommitDirty();
        LockstepStateHashAudit audit = tracker.AuditAndRebuild(null);

        Assert.Multiple(() =>
        {
            Assert.That(incremental, Is.Not.EqualTo(before));
            Assert.That(audit.IncrementalHash, Is.EqualTo(incremental));
            Assert.That(audit.RebuiltHash, Is.EqualTo(incremental));
            Assert.That(audit.Matches, Is.True);
        });
    }

    [Test]
    public void FullAuditReportsAMissedDirtyMarkBeforeReplacingTheTree()
    {
        Player player = m_machine.CreateGameplayObject<Player>();
        player.Name = "before";
        var tracker = new LockstepStateHashTracker(m_machine);
        tracker.Initialize();
        ulong oldRoot = tracker.CurrentHash;

        player.Name = "changed without a dirty mark";
        bool reported = false;
        ulong rootSeenByReporter = 0;
        LockstepStateHashAudit audit = tracker.AuditAndRebuild(item =>
        {
            reported = true;
            rootSeenByReporter = tracker.CurrentHash;
            Assert.That(item.IncrementalHash, Is.EqualTo(oldRoot));
        });

        Assert.Multiple(() =>
        {
            Assert.That(reported, Is.True);
            Assert.That(audit.Matches, Is.False);
            Assert.That(rootSeenByReporter, Is.EqualTo(oldRoot),
                "The reporter must run before the rebuilt tree is adopted");
            Assert.That(tracker.CurrentHash, Is.EqualTo(audit.RebuiltHash));
            Assert.That(tracker.CurrentHash, Is.Not.EqualTo(oldRoot));
        });
    }

    [Test]
    public void ObjectCreationAndDeletionUpdateAllocatorAndSparseLeaves()
    {
        var tracker = new LockstepStateHashTracker(m_machine);
        tracker.Initialize();
        ulong empty = tracker.CurrentHash;

        Player player = m_machine.CreateGameplayObject<Player>();
        tracker.MarkObjectLife(player.ObjectID);
        ulong created = tracker.CommitDirty();
        Assert.That(created, Is.EqualTo(LockstepStateHashTree.Build(m_machine).RootHash));

        GObjectID objectID = player.ObjectID;
        Assert.That(m_machine.DeleteGameplayObject(player), Is.True);
        tracker.MarkObjectLife(objectID);
        ulong deleted = tracker.CommitDirty();

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.Not.EqualTo(empty));
            Assert.That(deleted, Is.Not.EqualTo(empty),
                "The monotonic allocator remains part of state after deletion");
            Assert.That(deleted, Is.EqualTo(LockstepStateHashTree.Build(m_machine).RootHash));
        });
    }
}
