using GMCore;

namespace Test;

[TestFixture]
public sealed class GameplayLoggingTests
{
    [Test]
    public void GameplayLoggerWritesAllImportanceLevelsToFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gameplaymachine-logs-" + Guid.NewGuid().ToString("N"));
        string path;
        try
        {
            using (var logger = new GameplayLogger(new GameplayLoggerOptions
                   {
                       DirectoryPath = directory,
                       FilePrefix = "test",
                       WriteToConsole = false,
                   }))
            {
                path = logger.FilePath;
                logger.Detail("Tests", "detail entry");
                logger.Important("Tests", "important entry");
                logger.Warning("Tests", "warning entry");
                logger.Error("Tests", "error entry");
            }

            string content = File.ReadAllText(path);
            Assert.Multiple(() =>
            {
                Assert.That(content, Does.Contain("[Detail]").And.Contain("detail entry"));
                Assert.That(content, Does.Contain("[Important]").And.Contain("important entry"));
                Assert.That(content, Does.Contain("[Warning]").And.Contain("warning entry"));
                Assert.That(content, Does.Contain("[Error]").And.Contain("error entry"));
                Assert.That(content, Does.Contain("Log stopped"));
            });
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Test]
    public void LockstepRelayWritesLifecycleEntriesToConfiguredLogger()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gameplaymachine-relay-logs-" + Guid.NewGuid().ToString("N"));
        string path;
        try
        {
            using (var logger = new GameplayLogger(new GameplayLoggerOptions
                   {
                       DirectoryPath = directory,
                       FilePrefix = "relay-test",
                       WriteToConsole = false,
                   }))
            {
                path = logger.FilePath;
                InMemoryGameplayNetworkTransport.CreatePair(
                    out InMemoryGameplayNetworkTransport relayTransport,
                    out InMemoryGameplayNetworkTransport peerTransport);
                using (peerTransport)
                using (var relay = new GameplayLockstepRelayServer(relayTransport,
                           new GameplayLockstepOptions { Logger = logger }))
                {
                    relay.Start();
                }
            }

            string content = File.ReadAllText(path);
            Assert.Multiple(() =>
            {
                Assert.That(content, Does.Contain("Relay started"));
                Assert.That(content, Does.Contain("State Disabled -> Connecting"));
                Assert.That(content, Does.Contain("Relay disposed"));
            });
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
