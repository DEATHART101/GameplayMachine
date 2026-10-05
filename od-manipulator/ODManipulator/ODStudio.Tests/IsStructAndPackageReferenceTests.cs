using ODStudio.Export;
using ODStudio.Generator;
using ODStudio.Model;
using ODStudio.Package;

namespace ODStudio.Tests;

public sealed class IsStructAndPackageReferenceTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "odstudio-reference-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [Test]
    public async Task IsStructRoundTripsAndGeneratesStrictNumericWrapper()
    {
        OdProject project = TestProject.Create();
        var money = new OdStructDefinition
        {
            Name = "Money",
            Namespace = project.DefaultNamespace,
            IsStructOf = OdTypeReference.BuiltInType(OdBuiltInType.Int32),
        };
        project.Definitions.Add(money);
        project.Definitions.OfType<OdClassDefinition>().Single().Fields.Add(new OdFieldDefinition
        {
            Name = "Balance",
            Type = new OdTypeReference { DefinitionId = money.Id },
        });
        string packagePath = Path.Combine(_directory, "game.odpkg");

        OdPackageBuildResult build = await OdPackageCompiler.BuildAsync(project, packagePath);
        Assert.That(build.Size, Is.GreaterThan(0), string.Join(Environment.NewLine, build.Issues));
        OdProject loaded = await OdPackageCompiler.LoadAsync(packagePath);
        OdStructDefinition loadedMoney = loaded.Definitions.OfType<OdStructDefinition>().Single();
        Assert.That(loadedMoney.IsStructOf?.BuiltIn, Is.EqualTo(OdBuiltInType.Int32));

        OdCodeGenerationResult generation = OdCSharpGenerator.GenerateFiles(loaded);
        Assert.That(generation.Succeeded, Is.True, string.Join(Environment.NewLine, generation.Issues));
        string types = generation.Files.Single(item => item.RelativePath.EndsWith("ODTypes.g.cs")).Content;
        string module = generation.Files.Single(item => item.RelativePath.EndsWith("ODModule.g.cs")).Content;
        Assert.Multiple(() =>
        {
            Assert.That(types, Does.Contain("public global::System.Int32 Value;"));
            Assert.That(types, Does.Contain("public static implicit operator Money(global::System.Int32 value)"));
            Assert.That(types, Does.Contain("public static explicit operator global::System.Int32(Money value)"));
            Assert.That(types, Does.Contain("operator +(Money left, Money right)"));
            Assert.That(module, Does.Contain("value.Value"));
            Assert.That(module, Does.Contain("BaseType = typeof(global::System.Int32)"));
            Assert.That(module, Does.Contain("is-struct:Single:False:Int32"));
        });
    }

    [Test]
    public void IsStructRejectsFieldsAndCycles()
    {
        OdProject project = TestProject.Create();
        var first = new OdStructDefinition { Name = "First", Namespace = project.DefaultNamespace };
        var second = new OdStructDefinition { Name = "Second", Namespace = project.DefaultNamespace };
        first.IsStructOf = new OdTypeReference { DefinitionId = second.Id };
        second.IsStructOf = new OdTypeReference { DefinitionId = first.Id };
        first.Fields.Add(new OdFieldDefinition { Name = "Invalid", Type = OdTypeReference.BuiltInType(OdBuiltInType.Int32) });
        project.Definitions.AddRange(new OdDefinition[] { first, second });

        IReadOnlyList<OdIssue> issues = OdProjectValidator.Validate(project);

        Assert.That(issues, Has.Some.Matches<OdIssue>(item => item.Code == "OD0231"));
        Assert.That(issues, Has.Some.Matches<OdIssue>(item => item.Code == "OD0234"));
    }

    [Test]
    public async Task ExportIncludesOnlyReferencedPackagesThatAreActuallyUsed()
    {
        OdProject common = CreateCommonProject();
        string commonPackagePath = Path.Combine(_directory, "common.odpkg");
        Assert.That((await OdPackageCompiler.BuildAsync(common, commonPackagePath)).Size, Is.GreaterThan(0));

        OdProject root = TestProject.Create();
        root.SourceDirectory = _directory;
        root.PackageReferences.Add(new OdPackageReference
        {
            Name = common.Name,
            PackageId = common.Id,
            PackagePath = Path.GetFileName(commonPackagePath),
        });
        string rootPackagePath = Path.Combine(_directory, "root.odpkg");
        Assert.That((await OdPackageCompiler.BuildAsync(root, rootPackagePath)).Size, Is.GreaterThan(0));

        var profile = new OdExportProfile { Name = "test", Target = "csharp" };
        OdExportResult unused = await OdExportPipeline.ExportAsync(new PlainCSharpExportTarget(),
            new OdExportRequest(rootPackagePath, Path.Combine(_directory, "unused"), profile, DryRun: true));
        Assert.That(unused.Plan.Artifacts.Any(item => item.RelativePath.StartsWith("Common/Math/", StringComparison.Ordinal)), Is.False);

        OdStructDefinition vector = common.Definitions.OfType<OdStructDefinition>().Single();
        root.Definitions.OfType<OdClassDefinition>().Single().Fields.Add(new OdFieldDefinition
        {
            Name = "Position",
            Type = new OdTypeReference { DefinitionId = vector.Id },
        });
        Assert.That((await OdPackageCompiler.BuildAsync(root, rootPackagePath)).Size, Is.GreaterThan(0));
        OdExportResult used = await OdExportPipeline.ExportAsync(new PlainCSharpExportTarget(),
            new OdExportRequest(rootPackagePath, Path.Combine(_directory, "used"), profile, DryRun: true));

        Assert.Multiple(() =>
        {
            Assert.That(used.Succeeded, Is.True, string.Join(Environment.NewLine, used.Plan.Issues));
            Assert.That(used.Plan.Artifacts, Has.Some.Matches<OdExportArtifact>(item =>
                item.RelativePath == "Common/Math/ODTypes.g.cs"));
            Assert.That(used.Plan.Artifacts.Single(item => item.RelativePath == "Tests/OD/ODTypes.g.cs").Content,
                Does.Contain("global::Common.Math.Vector2 Position"));
        });
    }

    [Test]
    public async Task ProjectStorePreservesPackageReferences()
    {
        OdProject project = TestProject.Create();
        var packageReference = new OdPackageReference
        {
            Name = "Common",
            PackageId = Guid.NewGuid(),
            PackagePath = "../packages/common.odpkg",
        };
        project.PackageReferences.Add(packageReference);

        await OdProjectStore.SaveAsync(_directory, project);
        OdProject loaded = await OdProjectStore.LoadAsync(_directory);

        Assert.That(loaded.PackageReferences.Single().Id, Is.EqualTo(packageReference.Id));
        Assert.That(loaded.PackageReferences.Single().PackageId, Is.EqualTo(packageReference.PackageId));
        Assert.That(loaded.PackageReferences.Single().PackagePath, Is.EqualTo(packageReference.PackagePath));
        Assert.That(loaded.SourceDirectory, Is.EqualTo(Path.GetFullPath(_directory)));
    }

    private static OdProject CreateCommonProject()
    {
        var vector = new OdStructDefinition
        {
            Name = "Vector2",
            Namespace = "Common.Math",
            Fields =
            {
                new OdFieldDefinition { Name = "X", Type = OdTypeReference.BuiltInType(OdBuiltInType.Single) },
                new OdFieldDefinition { Name = "Y", Type = OdTypeReference.BuiltInType(OdBuiltInType.Single) },
            },
        };
        return new OdProject
        {
            Name = "CommonMath",
            DefaultNamespace = "Common.Math",
            Ods = { new OdScope { Name = "CommonMath", Namespace = "Common.Math" } },
            Definitions = { vector },
        };
    }
}
