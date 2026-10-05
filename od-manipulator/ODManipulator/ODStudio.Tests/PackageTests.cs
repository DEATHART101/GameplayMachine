using ODStudio.Model;
using ODStudio.Package;

namespace ODStudio.Tests;

public sealed class PackageTests
{
    private string _directory = null!;
    private string PackagePath => Path.Combine(_directory, "game.odpkg");

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "odstudio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [Test]
    public async Task BinaryPackageRoundTripPreservesInterfaceInheritance()
    {
        OdProject source = TestProject.Create();
        OdInterfaceDefinition root = source.Definitions.OfType<OdInterfaceDefinition>().Single();
        root.Abstract = true;
        var derived = new OdInterfaceDefinition
        {
            Name = "DerivedDamage",
            Namespace = source.DefaultNamespace,
            BaseInterfaceId = root.Id,
        };
        source.Definitions.Add(derived);

        OdPackageBuildResult result = await OdPackageCompiler.BuildAsync(source, PackagePath);
        Assert.That(result.Issues, Has.None.Matches<OdIssue>(issue => issue.Severity == OdIssueSeverity.Error),
            string.Join(Environment.NewLine, result.Issues));

        OdProject loaded = await OdPackageCompiler.LoadAsync(PackagePath);
        OdInterfaceDefinition loadedRoot = loaded.Definitions.OfType<OdInterfaceDefinition>()
            .Single(item => item.Name == root.Name);
        OdInterfaceDefinition loadedDerived = loaded.Definitions.OfType<OdInterfaceDefinition>()
            .Single(item => item.Name == derived.Name);
        Assert.Multiple(() =>
        {
            Assert.That(loadedRoot.Abstract, Is.True);
            Assert.That(loadedDerived.BaseInterfaceId, Is.EqualTo(loadedRoot.Id));
        });
    }

    [Test]
    public async Task BinaryPackageRoundTripPreservesInputDrivenTickMode()
    {
        OdProject source = TestProject.Create();
        source.Runtime.NetworkMode = OdNetworkMode.Lockstep;
        source.Runtime.LockstepTickMode = OdLockstepTickMode.InputDriven;

        OdPackageBuildResult result = await OdPackageCompiler.BuildAsync(source, PackagePath);
        Assert.That(result.Issues, Has.None.Matches<OdIssue>(issue => issue.Severity == OdIssueSeverity.Error),
            string.Join(Environment.NewLine, result.Issues));

        OdProject loaded = await OdPackageCompiler.LoadAsync(PackagePath);
        Assert.That(loaded.Runtime.LockstepTickMode, Is.EqualTo(OdLockstepTickMode.InputDriven));
    }

    [Test]
    public async Task BinaryPackageRoundTripPreservesStateSyncTickMode()
    {
        OdProject source = TestProject.Create();
        source.Runtime.StateSyncTickMode = OdStateSyncTickMode.None;

        OdPackageBuildResult result = await OdPackageCompiler.BuildAsync(source, PackagePath);
        Assert.That(result.Issues, Has.None.Matches<OdIssue>(issue => issue.Severity == OdIssueSeverity.Error),
            string.Join(Environment.NewLine, result.Issues));

        OdProject loaded = await OdPackageCompiler.LoadAsync(PackagePath);
        Assert.That(loaded.Runtime.StateSyncTickMode, Is.EqualTo(OdStateSyncTickMode.None));
    }

    [Test]
    public async Task BinaryPackageRoundTripPreservesRuntimeModel()
    {
        var source = TestProject.Create();
        source.Definitions.OfType<OdInterfaceDefinition>().Single().InterfaceType = OdInterfaceType.PlayerInput;
        source.Definitions.OfType<OdInterfaceDefinition>().Single().Prediction = OdPredictionMode.OnlySuccess;
        var sourceInput = source.Definitions.OfType<OdInterfaceDefinition>().Single().Inputs
            .Single(item => item.Name == "Target");
        var sourceClass = source.Definitions.OfType<OdClassDefinition>().Single();
        sourceClass.Replication = OdObjectReplicationMode.OwnerOnly;
        sourceClass.Fields.Single().Replication = OdFieldReplicationMode.OwnerOnly;
        sourceClass.Relevancy = OdObjectRelevancyMode.Spatial;
        sourceClass.Partition = OdObjectPartitionMode.Spatial;
        sourceClass.IsClasses.Add(OdNetworkPackage.ActorClassId);
        sourceClass.Fields.Add(new OdFieldDefinition
        {
            Name = "Controller",
            Type = new OdTypeReference { DefinitionId = OdNetworkPackage.PlayerControllerClassId },
        });
        var driven = new OdFieldDefinition
        {
            Name = "IsAlive",
            Type = OdTypeReference.BuiltInType(OdBuiltInType.Boolean),
            Mode = OdFieldMode.Driven,
            Replication = OdFieldReplicationMode.None,
            DrivenByFields = { sourceClass.Fields.Single(item => item.Name == "Health").Id },
        };
        sourceClass.Fields.Add(driven);
        var resourceMap = new OdFieldDefinition
        {
            Name = "ResourceMap",
            Type = new OdTypeReference
            {
                DefinitionId = sourceClass.Id,
                IsResourceClass = true,
                Container = OdContainerKind.Dictionary,
                DictionaryKeyBuiltIn = OdBuiltInType.None,
                DictionaryKeyDefinitionId = sourceClass.Id,
                DictionaryKeyIsResourceClass = true,
            },
        };
        sourceClass.Fields.Add(resourceMap);
        var secondBase = new OdClassDefinition
        {
            Name = "SecondBase",
            Namespace = source.DefaultNamespace,
            Replication = OdObjectReplicationMode.None,
        };
        var derived = new OdClassDefinition
        {
            Name = "Derived",
            Namespace = source.DefaultNamespace,
            PreventInheritance = true,
            IsClasses = { sourceClass.Id, secondBase.Id },
        };
        source.Definitions.Add(secondBase);
        source.Definitions.Add(derived);
        var sourceStruct = new OdResourceDefinition
        {
            Name = "ResourceData",
            Namespace = source.DefaultNamespace,
        };
        source.Definitions.Add(sourceStruct);
        var switchStruct = new OdSwitchStructDefinition
        {
            Name = "ActorReference",
            Namespace = source.DefaultNamespace,
            Fields =
            {
                new OdFieldDefinition { Name = "Actor", Type = new OdTypeReference { DefinitionId = sourceClass.Id, IsResourceClass = true } },
                new OdFieldDefinition { Name = "Name", Type = OdTypeReference.BuiltInType(OdBuiltInType.String) },
            },
        };
        source.Definitions.Add(switchStruct);
        var mood = new OdEnumDefinition
        {
            Name = "Mood",
            Namespace = source.DefaultNamespace,
            Members =
            {
                new OdEnumMember { Name = "Idle", Value = -1 },
                new OdEnumMember { Name = "Busy", Value = int.MaxValue },
            },
        };
        source.Definitions.Add(mood);
        var multicastEvent = new OdEventDefinition
        {
            Name = "MatchStarted",
            Namespace = source.DefaultNamespace,
            Multicast = true,
        };
        source.Definitions.Add(multicastEvent);
        var player = new OdSceneObject
        {
            Name = "Player",
            ClassDefinitionId = sourceClass.Id,
            Values = { [sourceClass.Fields.Single(item => item.Name == "Health").Id] = System.Text.Json.Nodes.JsonValue.Create(75) },
        };
        var scene = new OdScene
        {
            Name = "Arena",
            OdId = source.Ods.Single().Id,
            Objects = { player },
        };
        source.Scenes.Add(scene);
        source.InitialSceneId = scene.Id;
        source.RootClassId = secondBase.Id;
        source.Runtime = new OdRuntimeSettings
        {
            TickIntervalMilliseconds = 25,
            SpatialCellSize = 48f,
            PartitionResolver = OdPartitionResolverKind.Grid,
            PartitionCellSize = 12f,
            PartitionIncludeY = true,
            LoadMargin = 6f,
            UnloadDelaySeconds = 2.5f,
            MaxLoadsPerUpdate = 17,
            ManagePartitionLoading = false,
        };
        source.CodeFiles.Add(new OdCodeFile
        {
            OwnerId = source.Id,
            Kind = OdCodeFileKind.Runtime,
            RelativePath = "Tests/OD/Runtime/TestRuntime.cs",
            Content = "// runtime source",
        });
        var result = await OdPackageCompiler.BuildAsync(source, PackagePath);

        Assert.That(result.Issues.Any(item => item.Severity == OdIssueSeverity.Error), Is.False);
        Assert.That(result.Size, Is.GreaterThan(200));
        Assert.That(BitConverter.ToUInt16(await File.ReadAllBytesAsync(PackagePath), 4), Is.EqualTo(37));
        var loaded = await OdPackageCompiler.LoadAsync(PackagePath);
        Assert.That(loaded.Id, Is.EqualTo(source.Id));
        Assert.That(loaded.Ods.Select(item => item.Id), Is.EqualTo(source.Ods.Select(item => item.Id)));
        Assert.That(loaded.Definitions.Select(item => item.Id), Is.EqualTo(source.Definitions.Select(item => item.Id)));
        Assert.That(loaded.Definitions.OfType<OdInterfaceDefinition>().Single().Prediction,
            Is.EqualTo(OdPredictionMode.OnlySuccess));
        Assert.That(loaded.Definitions.OfType<OdInterfaceDefinition>().Single().InterfaceType,
            Is.EqualTo(OdInterfaceType.PlayerInput));
        Assert.That(loaded.Definitions.OfType<OdEventDefinition>().Single().Multicast, Is.True);
        Assert.That(loaded.Definitions.OfType<OdInterfaceDefinition>().Single().Inputs
            .Single(item => item.Name == sourceInput.Name).NotNull, Is.True);
        Assert.That(loaded.Definitions.OfType<OdClassDefinition>().Single(item => item.Name == sourceClass.Name).Replication,
            Is.EqualTo(OdObjectReplicationMode.OwnerOnly));
        Assert.That(loaded.Definitions.OfType<OdClassDefinition>().Single(item => item.Name == sourceClass.Name).Relevancy,
            Is.EqualTo(OdObjectRelevancyMode.Spatial));
        Assert.That(loaded.Definitions.OfType<OdClassDefinition>().Single(item => item.Name == sourceClass.Name).Partition,
            Is.EqualTo(OdObjectPartitionMode.Spatial));
        Assert.That(loaded.Definitions.OfType<OdClassDefinition>().Single(item => item.Name == secondBase.Name).Replication,
            Is.EqualTo(OdObjectReplicationMode.None));
        Assert.That(loaded.Definitions.OfType<OdClassDefinition>().Single(item => item.Name == derived.Name).PreventInheritance,
            Is.True);
        Assert.That(loaded.Definitions.OfType<OdClassDefinition>().Single(item => item.Name == sourceClass.Name)
            .Fields.Single(item => item.Name == "Health").Replication, Is.EqualTo(OdFieldReplicationMode.OwnerOnly));
        Assert.That(loaded.Definitions.OfType<OdClassDefinition>().Single(item => item.Name == sourceClass.Name)
            .Fields.Single(item => item.Name == "Controller").Type.DefinitionId,
            Is.EqualTo(OdNetworkPackage.PlayerControllerClassId));
        OdFieldDefinition loadedDriven = loaded.Definitions.OfType<OdClassDefinition>()
            .Single(item => item.Name == sourceClass.Name).Fields.Single(item => item.Name == driven.Name);
        Assert.That(loadedDriven.Mode, Is.EqualTo(OdFieldMode.Driven));
        Assert.That(loadedDriven.DrivenByFields, Is.EqualTo(driven.DrivenByFields));
        OdTypeReference loadedResourceMap = loaded.Definitions.OfType<OdClassDefinition>()
            .Single(item => item.Name == sourceClass.Name).Fields.Single(item => item.Name == resourceMap.Name).Type;
        Assert.Multiple(() =>
        {
            Assert.That(loadedResourceMap.IsResourceClass, Is.True);
            Assert.That(loadedResourceMap.DictionaryKeyIsResourceClass, Is.True);
        });
        Assert.That(loaded.Definitions.OfType<OdClassDefinition>().Single(item => item.Name == derived.Name).IsClasses,
            Is.EqualTo(derived.IsClasses));
        OdResourceDefinition loadedResource = loaded.Definitions.OfType<OdResourceDefinition>().Single();
        Assert.That(loadedResource.Name, Is.EqualTo("ResourceData"));
        Assert.That(loaded.Definitions.OfType<OdSwitchStructDefinition>().Single().Fields.Select(item => item.Name),
            Is.EqualTo(new[] { "Actor", "Name" }));
        Assert.That(loaded.Definitions.OfType<OdSwitchStructDefinition>().Single().Fields[0].Type.IsResourceClass, Is.True);
        Assert.That(loaded.Definitions.OfType<OdEnumDefinition>().Single().Members.Select(item => item.Value),
            Is.EqualTo(new[] { -1, int.MaxValue }));
        Assert.That(loaded.Scenes.Single().Objects.Single().Values.Values.Single()!.GetValue<int>(), Is.EqualTo(75));
        Assert.That(loaded.CodeFiles.Single().Content, Is.EqualTo("// runtime source"));
        Assert.That(loaded.RootClassId, Is.EqualTo(secondBase.Id));
        Assert.That(loaded.InitialSceneId, Is.EqualTo(scene.Id));
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Runtime.TickIntervalMilliseconds, Is.EqualTo(25));
            Assert.That(loaded.Runtime.SpatialCellSize, Is.EqualTo(48f));
            Assert.That(loaded.Runtime.PartitionResolver, Is.EqualTo(OdPartitionResolverKind.Grid));
            Assert.That(loaded.Runtime.PartitionCellSize, Is.EqualTo(12f));
            Assert.That(loaded.Runtime.PartitionIncludeY, Is.True);
            Assert.That(loaded.Runtime.LoadMargin, Is.EqualTo(6f));
            Assert.That(loaded.Runtime.UnloadDelaySeconds, Is.EqualTo(2.5f));
            Assert.That(loaded.Runtime.MaxLoadsPerUpdate, Is.EqualTo(17));
            Assert.That(loaded.Runtime.ManagePartitionLoading, Is.False);
        });
        Assert.That(loaded.History, Is.Empty, "Runtime packages must not contain editor history.");
    }

    [Test]
    public async Task NetworkPackageIsAvailableWithoutAnExplicitPackageReference()
    {
        OdProject project = TestProject.Create();
        project.Definitions.OfType<OdClassDefinition>().Single().IsClasses.Add(OdNetworkPackage.PawnClassId);
        project.Runtime = new OdRuntimeSettings
        {
            SpatialCellSize = 48,
            PartitionResolver = OdPartitionResolverKind.Grid,
            PartitionCellSize = 12,
            LoadMargin = 6,
            UnloadDelaySeconds = 2.5f,
            MaxLoadsPerUpdate = 17,
            ManagePartitionLoading = false,
        };

        OdPackageGraph graph = await OdPackageGraph.LoadForProjectAsync(project);
        OdProject composed = graph.ComposeUsedProject();

        Assert.Multiple(() =>
        {
            Assert.That(project.PackageReferences, Is.Empty);
            Assert.That(graph.Projects.Any(OdNetworkPackage.IsPackage), Is.True);
            Assert.That(graph.GetUsedProjects().Any(OdNetworkPackage.IsPackage), Is.True);
            Assert.That(composed.Definitions.Any(item => item.Id == OdNetworkPackage.PawnClassId), Is.False,
                "Runtime built-ins are supplied by GameplayMachineCore and must not be generated again.");
            Assert.That(composed.Runtime, Is.SameAs(project.Runtime));
            Assert.That(composed.Runtime.PartitionCellSize, Is.EqualTo(12));
        });
    }

    [Test]
    public async Task CorruptedPackageIsRejected()
    {
        var source = TestProject.Create();
        await OdPackageCompiler.BuildAsync(source, PackagePath);
        var bytes = await File.ReadAllBytesAsync(PackagePath);
        bytes[bytes.Length / 2] ^= 0x5a;
        await File.WriteAllBytesAsync(PackagePath, bytes);

        Assert.That(async () => await OdPackageCompiler.LoadAsync(PackagePath), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public async Task ValidationErrorsBlockPackageOutput()
    {
        var source = TestProject.Create();
        source.Definitions[1].Name = source.Definitions[0].Name;
        source.Definitions[1].Namespace = source.Definitions[0].Namespace;

        var result = await OdPackageCompiler.BuildAsync(source, PackagePath);

        Assert.That(result.Issues.Any(item => item.Code == "OD0200"), Is.True);
        Assert.That(result.Size, Is.Zero);
        Assert.That(File.Exists(PackagePath), Is.False);
    }

    [TestCase(false, "OD0203")]
    [TestCase(true, "OD0217")]
    public async Task InvalidInheritanceBlocksPackageOutput(bool diamond, string expectedIssueCode)
    {
        var source = TestProject.Create();
        var derived = source.Definitions.OfType<OdClassDefinition>().Single();
        var sharedBase = new OdClassDefinition { Name = "SharedBase", Namespace = source.DefaultNamespace };
        var leftBase = new OdClassDefinition
        {
            Name = "LeftBase",
            Namespace = source.DefaultNamespace,
            IsClasses = { sharedBase.Id },
        };
        source.Definitions.Add(sharedBase);
        source.Definitions.Add(leftBase);

        if (diamond)
        {
            var rightBase = new OdClassDefinition
            {
                Name = "RightBase",
                Namespace = source.DefaultNamespace,
                IsClasses = { sharedBase.Id },
            };
            derived.IsClasses.AddRange(new[] { leftBase.Id, rightBase.Id });
            source.Definitions.Add(rightBase);
        }
        else
        {
            derived.IsClasses.Add(leftBase.Id);
            sharedBase.IsClasses.Add(derived.Id);
        }

        var result = await OdPackageCompiler.BuildAsync(source, PackagePath);

        Assert.That(result.Issues, Has.Some.Matches<OdIssue>(issue =>
            issue.Code == expectedIssueCode && issue.Severity == OdIssueSeverity.Error));
        Assert.That(result.Size, Is.Zero);
        Assert.That(File.Exists(PackagePath), Is.False);
    }
}
