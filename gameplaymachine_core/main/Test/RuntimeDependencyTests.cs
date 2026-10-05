using System.Text.Json;

namespace Test;

public class RuntimeDependencyTests
{
    [Test]
    public void CoreDoesNotReferenceRetiredFieldSerializer()
    {
        string?[] references = typeof(GameplayMachine).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name).ToArray();

        Assert.That(references, Does.Not.Contain("FieldSerializer"));
        Assert.That(references, Does.Not.Contain("XFieldSerializer"));
    }

    [Test]
    public void RuntimeDependencyManifestDoesNotIncludeRetiredFieldSerializer()
    {
        string path = Path.ChangeExtension(typeof(RuntimeDependencyTests).Assembly.Location, ".deps.json");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(path));
        string[] libraries = manifest.RootElement.GetProperty("libraries").EnumerateObject()
            .Select(library => library.Name.Split('/')[0]).ToArray();

        Assert.That(libraries, Does.Not.Contain("XFieldSerializer"));
        Assert.That(libraries, Does.Not.Contain("FieldSerializer"));
    }
}
