using ODStudio.Generator;

namespace ODStudio.Tests;

public class RuntimeReferenceTests
{
    [Test]
    public void ReferencesContainRuntimeClosureButNotEditorOrRetiredDependencies()
    {
        string[] names = OdRuntimeReferences.Resolve(AppContext.BaseDirectory)
            .Select(path => Path.GetFileNameWithoutExtension(path)).ToArray();
        Assert.That(names, Does.Contain("GameplayMachineCore"));
        Assert.That(names, Does.Contain("ODCore"));
        Assert.That(names, Does.Contain("LiteNetLib"));
        Assert.That(names, Does.Contain("XLockstep"));
        Assert.That(names, Does.Contain("event_system"));
        Assert.That(names, Does.Contain("lifetime_object"));
        Assert.That(names, Does.Not.Contain("FieldSerializer"));
        Assert.That(names, Has.None.StartsWith("ODStudio."));
        Assert.That(names, Has.None.StartsWith("System."));
        Assert.That(names, Has.None.StartsWith("Avalonia"));
    }

    [Test]
    public void SelfContainedFrameworkAndNativeFilesAreIgnored()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gm-reference-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var expected = OdRuntimeReferences.Resolve(AppContext.BaseDirectory);
            foreach (string path in expected)
                File.Copy(path, Path.Combine(directory, Path.GetFileName(path)));
            File.Copy(typeof(object).Assembly.Location, Path.Combine(directory, "System.Private.CoreLib.dll"));
            File.WriteAllBytes(Path.Combine(directory, "native-library.dll"), new byte[] { 0, 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(directory, "UnusedPlugin.dll"), new byte[] { 4, 5 });

            Assert.That(OdRuntimeReferences.Resolve(directory).Select(Path.GetFileName),
                Is.EqualTo(expected.Select(Path.GetFileName)));
            File.Delete(Path.Combine(directory, "ODCore.dll"));
            Assert.Throws<FileNotFoundException>(() => OdRuntimeReferences.Resolve(directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
