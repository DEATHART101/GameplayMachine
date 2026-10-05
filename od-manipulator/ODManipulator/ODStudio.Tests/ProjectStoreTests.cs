using System.Text.Json.Nodes;
using ODStudio.Model;
using ODStudio.Package;

namespace ODStudio.Tests;

public sealed class ProjectStoreTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Path.Combine(Path.GetTempPath(), "odstudio-tests", Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [Test]
    public async Task ProjectDirectoryRoundTripPreservesStableIdsAndContent()
    {
        var project = TestProject.Create();
        var interfaceInput = project.Definitions.OfType<OdInterfaceDefinition>().Single().Inputs
            .Single(item => item.Name == "Target");
        var resourceClass = project.Definitions.OfType<OdClassDefinition>().Single();
        resourceClass.Fields.Add(new OdFieldDefinition
        {
            Name = "Template",
            Type = new OdTypeReference { DefinitionId = resourceClass.Id, IsResourceClass = true },
        });
        project.Definitions.Add(new OdResourceDefinition
        {
            Name = "StoredSprite",
            Namespace = project.DefaultNamespace,
        });
        project.Definitions.Add(new OdSwitchStructDefinition
        {
            Name = "StoredChoice",
            Namespace = project.DefaultNamespace,
            Fields =
            {
                new OdFieldDefinition { Name = "Count", Type = OdTypeReference.BuiltInType(OdBuiltInType.Int32) },
            },
        });
        project.EditorState.CollapsedInheritedFieldSections.Add("owner:source");
        Guid codeId = Guid.NewGuid();
        project.CodeFiles.Add(new OdCodeFile
        {
            Id = codeId,
            OwnerId = project.Definitions.OfType<OdInterfaceDefinition>().Single().Id,
            Kind = OdCodeFileKind.Interface,
            RelativePath = "Tests/OD/Interfaces/DamageParam.cs",
            Content = "// project-owned implementation",
        });
        var storedActor = new OdSceneObject
        {
            Name = "StoredActor",
            ClassDefinitionId = resourceClass.Id,
            Values = { [resourceClass.Fields.Single(item => item.Name == "Health").Id] = JsonValue.Create(55) },
        };
        var storedScene = new OdScene
        {
            Name = "StoredScene",
            OdId = project.Ods.Single().Id,
            Objects = { storedActor },
        };
        project.Scenes.Add(storedScene);
        project.InitialSceneId = storedScene.Id;
        project.Runtime = new OdRuntimeSettings
        {
            NetworkMode = OdNetworkMode.Lockstep,
            StateSyncTickMode = OdStateSyncTickMode.None,
            LockstepTickMode = OdLockstepTickMode.InputDriven,
            TickIntervalMilliseconds = 25,
            SpatialCellSize = 24f,
            PartitionResolver = OdPartitionResolverKind.Grid,
            PartitionCellSize = 8f,
            ProvisionPartitions = true,
            MaxLoadsPerUpdate = 4,
        };

        await OdProjectStore.SaveAsync(_directory, project);
        var loaded = await OdProjectStore.LoadAsync(_directory);

        Assert.That(loaded.Id, Is.EqualTo(project.Id));
        Assert.That(loaded.Runtime.NetworkMode, Is.EqualTo(OdNetworkMode.Lockstep));
        Assert.That(loaded.Runtime.StateSyncTickMode, Is.EqualTo(OdStateSyncTickMode.None));
        Assert.That(loaded.Runtime.LockstepTickMode, Is.EqualTo(OdLockstepTickMode.InputDriven));
        Assert.That(loaded.Runtime.TickIntervalMilliseconds, Is.EqualTo(25));
        Assert.That(loaded.Ods.Select(item => item.Id), Is.EqualTo(project.Ods.Select(item => item.Id)));
        Assert.That(loaded.Definitions.Select(item => item.Id), Is.EquivalentTo(project.Definitions.Select(item => item.Id)));
        Assert.That(((OdClassDefinition)loaded.Definitions[0]).Fields[0].Name, Is.EqualTo("Health"));
        Assert.That(((OdClassDefinition)loaded.Definitions[0]).Fields.Single(item => item.Name == "Template").Type.IsResourceClass, Is.True);
        Assert.That(loaded.Definitions.OfType<OdInterfaceDefinition>().Single().Inputs
            .Single(item => item.Name == interfaceInput.Name).NotNull, Is.True);
        Assert.That(loaded.Definitions.OfType<OdSwitchStructDefinition>().Single().Fields.Single().Name,
            Is.EqualTo("Count"));
        Assert.That(loaded.Definitions.OfType<OdResourceDefinition>().Single().Name,
            Is.EqualTo("StoredSprite"));
        Assert.That(loaded.DataSets.Single().Records.Single().Values.Values.Single()!.GetValue<int>(), Is.EqualTo(100));
        Assert.That(loaded.History.Single().Summary, Is.EqualTo("Create test project"));
        Assert.That(loaded.EditorState.CollapsedInheritedFieldSections, Does.Contain("owner:source"));
        Assert.That(loaded.Scenes.Single().Objects.Single().Values.Values.Single()!.GetValue<int>(), Is.EqualTo(55));
        Assert.That(loaded.RootClassId, Is.EqualTo(project.RootClassId));
        Assert.That(loaded.InitialSceneId, Is.EqualTo(storedScene.Id));
        Assert.That(loaded.Runtime.SpatialCellSize, Is.EqualTo(24f));
        Assert.That(loaded.Runtime.PartitionResolver, Is.EqualTo(OdPartitionResolverKind.Grid));
        Assert.That(loaded.Runtime.PartitionCellSize, Is.EqualTo(8f));
        Assert.That(loaded.Runtime.ProvisionPartitions, Is.True);
        Assert.That(loaded.Runtime.MaxLoadsPerUpdate, Is.EqualTo(4));
        Assert.That(loaded.CodeFiles.Single().Id, Is.EqualTo(codeId));
        Assert.That(loaded.CodeFiles.Single().Kind, Is.EqualTo(OdCodeFileKind.Interface));
        Assert.That(loaded.CodeFiles.Single().Content, Is.EqualTo("// project-owned implementation"));
        Assert.That(File.Exists(Path.Combine(_directory, "project.json")), Is.True);
        Assert.That(Directory.EnumerateFiles(Path.Combine(_directory, "ods"), "*.json").Count(), Is.EqualTo(1));
        Assert.That(Directory.EnumerateFiles(Path.Combine(_directory, "schemas"), "*.json").Count(), Is.EqualTo(4));
        Assert.That(Directory.EnumerateFiles(Path.Combine(_directory, "scenes"), "*.json").Count(), Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(_directory, "code", ".gameplaymachine-code.json")), Is.True);
    }

    [Test]
    public async Task RefreshCodeFilesHonorsDeletionOfPersistedSource()
    {
        var project = TestProject.Create();
        project.CodeFiles.Add(new OdCodeFile
        {
            Kind = OdCodeFileKind.Resolver,
            RelativePath = "Tests/OD/Resolvers/TestResolver.cs",
            Content = "// delete me",
        });
        await OdProjectStore.SaveAsync(_directory, project);
        File.Delete(Path.Combine(_directory, "code", "Tests", "OD", "Resolvers", "TestResolver.cs"));

        OdProjectStore.RefreshCodeFilesFromDisk(_directory, project);

        Assert.That(project.CodeFiles, Is.Empty);
    }

    [Test]
    public async Task NormalSavePreservesCodeEditedOutsideTheWorkspace()
    {
        var project = TestProject.Create();
        project.CodeFiles.Add(new OdCodeFile
        {
            Kind = OdCodeFileKind.Interface,
            RelativePath = "Tests/OD/Interfaces/TestParam.cs",
            Content = "// workspace",
        });
        await OdProjectStore.SaveAsync(_directory, project);
        string sourcePath = Path.Combine(_directory, "code", "Tests", "OD", "Interfaces", "TestParam.cs");
        await File.WriteAllTextAsync(sourcePath, "// external");

        await OdProjectStore.SaveAsync(_directory, project);

        Assert.That(await File.ReadAllTextAsync(sourcePath), Is.EqualTo("// external"));
        Assert.That(project.CodeFiles.Single().Content, Is.EqualTo("// external"));
    }

    [Test]
    public async Task OverwriteSaveReplacesExternalCodeWithWorkspaceState()
    {
        var project = TestProject.Create();
        project.CodeFiles.Add(new OdCodeFile
        {
            Kind = OdCodeFileKind.Interface,
            RelativePath = "Tests/OD/Interfaces/TestParam.cs",
            Content = "// workspace",
        });
        await OdProjectStore.SaveAsync(_directory, project);
        string codeDirectory = Path.Combine(_directory, "code");
        string sourcePath = Path.Combine(codeDirectory, "Tests", "OD", "Interfaces", "TestParam.cs");
        string externalPath = Path.Combine(codeDirectory, "Tests", "OD", "Interfaces", "External.cs");
        await File.WriteAllTextAsync(sourcePath, "// external edit");
        await File.WriteAllTextAsync(externalPath, "// external file");

        await OdProjectStore.OverwriteAsync(_directory, project);

        Assert.That(await File.ReadAllTextAsync(sourcePath), Is.EqualTo("// workspace"));
        Assert.That(File.Exists(externalPath), Is.False);
    }

    [Test]
    public async Task DiskSnapshotDetectsProjectChangesButIgnoresEditorStateAndBuildArtifacts()
    {
        var project = TestProject.Create();
        await OdProjectStore.SaveAsync(_directory, project);
        OdProjectDiskSnapshot original = OdProjectDiskSnapshot.Capture(_directory);

        project.EditorState.CollapsedInheritedFieldSections.Add("class-a:class-b");
        await OdProjectStore.SaveEditorStateAsync(_directory, project.EditorState);
        string generatedDirectory = Path.Combine(_directory, "code", ".gameplaymachine-editor");
        Directory.CreateDirectory(generatedDirectory);
        await File.WriteAllTextAsync(Path.Combine(generatedDirectory, "Generated.cs"), "// generated");

        Assert.That(OdProjectDiskSnapshot.Capture(_directory), Is.EqualTo(original));

        string schemaPath = Directory.EnumerateFiles(Path.Combine(_directory, "schemas"), "*.json").First();
        await File.AppendAllTextAsync(schemaPath, Environment.NewLine);

        Assert.That(OdProjectDiskSnapshot.Capture(_directory), Is.Not.EqualTo(original));
    }

    [Test]
    public async Task WorkspaceBuildArtifactsAreNotTrackedAsProjectCode()
    {
        var project = TestProject.Create();
        await OdProjectStore.SaveAsync(_directory, project);
        string codeDirectory = Path.Combine(_directory, "code");
        Directory.CreateDirectory(Path.Combine(codeDirectory, ".gameplaymachine-editor", "Generated"));
        Directory.CreateDirectory(Path.Combine(codeDirectory, "bin", "Debug"));
        Directory.CreateDirectory(Path.Combine(codeDirectory, "obj", "Debug"));
        await File.WriteAllTextAsync(Path.Combine(codeDirectory, ".gameplaymachine-editor", "Generated", "Generated.cs"), "// generated");
        await File.WriteAllTextAsync(Path.Combine(codeDirectory, "bin", "Debug", "Binary.cs"), "// binary");
        await File.WriteAllTextAsync(Path.Combine(codeDirectory, "obj", "Debug", "Intermediate.cs"), "// intermediate");

        OdProjectStore.RefreshCodeFilesFromDisk(_directory, project);
        OdProject loaded = await OdProjectStore.LoadAsync(_directory);

        Assert.Multiple(() =>
        {
            Assert.That(project.CodeFiles, Has.None.Matches<OdCodeFile>(item =>
                item.RelativePath.StartsWith(".gameplaymachine-editor/") ||
                item.RelativePath.StartsWith("bin/") ||
                item.RelativePath.StartsWith("obj/")));
            Assert.That(loaded.CodeFiles, Has.None.Matches<OdCodeFile>(item =>
                item.RelativePath.StartsWith(".gameplaymachine-editor/") ||
                item.RelativePath.StartsWith("bin/") ||
                item.RelativePath.StartsWith("obj/")));
        });
    }

    [Test]
    public async Task LegacyProjectWithoutOdFilesCreatesScopesFromDefinitionNamespaces()
    {
        var project = TestProject.Create();
        await OdProjectStore.SaveAsync(_directory, project);
        Directory.Delete(Path.Combine(_directory, "ods"), true);

        var loaded = await OdProjectStore.LoadAsync(_directory);

        Assert.That(loaded.Ods, Has.Count.EqualTo(1));
        Assert.That(loaded.Ods.Single().Namespace, Is.EqualTo("Tests.OD"));
    }

    [Test]
    public async Task LegacyResourceStructMigratesToIndependentResourceDefinition()
    {
        var project = TestProject.Create();
        var legacyResource = new OdStructDefinition
        {
            Name = "LegacyResource",
            Namespace = project.DefaultNamespace,
            Fields =
            {
                new OdFieldDefinition { Name = "Value", Type = OdTypeReference.BuiltInType(OdBuiltInType.Int32) },
            },
        };
        project.Definitions.Add(legacyResource);
        await OdProjectStore.SaveAsync(_directory, project);

        string schemaPath = Path.Combine(_directory, "schemas", $"LegacyResource.{legacyResource.Id:N}.json");
        var schema = JsonNode.Parse(await File.ReadAllTextAsync(schemaPath))!.AsObject();
        schema["isResource"] = true;
        schema["externalAssetKind"] = "scriptableObject";
        await File.WriteAllTextAsync(schemaPath, schema.ToJsonString(OdJson.Options));

        var loaded = await OdProjectStore.LoadAsync(_directory);

        OdResourceDefinition migrated = loaded.Definitions.OfType<OdResourceDefinition>()
            .Single(item => item.Id == legacyResource.Id);
        Assert.That(migrated.Name, Is.EqualTo(legacyResource.Name));
        Assert.That(migrated.Fields.Single().Name, Is.EqualTo("Value"));
        Assert.That(loaded.Definitions.OfType<OdStructDefinition>().Any(item => item.Id == legacyResource.Id), Is.False);
    }

    [Test]
    public async Task LegacyPawnAndGameplayBuiltInTypesMigrateToNetworkPackageDefinitions()
    {
        var project = TestProject.Create();
        OdClassDefinition gameplayClass = project.Definitions.OfType<OdClassDefinition>().Single();
        await OdProjectStore.SaveAsync(_directory, project);

        string schemaPath = Directory.EnumerateFiles(Path.Combine(_directory, "schemas"),
            $"*.{gameplayClass.Id:N}.json").Single();
        JsonObject schema = JsonNode.Parse(await File.ReadAllTextAsync(schemaPath))!.AsObject();
        schema["isPawn"] = true;
        JsonObject fieldType = schema["fields"]![0]!["type"]!.AsObject();
        fieldType["builtIn"] = "playerController";
        fieldType["definitionId"] = null;
        await File.WriteAllTextAsync(schemaPath, schema.ToJsonString(OdJson.Options));

        OdProject loaded = await OdProjectStore.LoadAsync(_directory);
        OdClassDefinition migrated = loaded.Definitions.OfType<OdClassDefinition>()
            .Single(item => item.Id == gameplayClass.Id);

        Assert.Multiple(() =>
        {
            Assert.That(migrated.IsClasses, Does.Contain(OdNetworkPackage.PawnClassId));
            Assert.That(migrated.Fields[0].Type.BuiltIn, Is.EqualTo(OdBuiltInType.None));
            Assert.That(migrated.Fields[0].Type.DefinitionId,
                Is.EqualTo(OdNetworkPackage.PlayerControllerClassId));
        });
    }

    [Test]
    public async Task EditorStateCanBeSavedWithoutSavingTheProject()
    {
        var project = TestProject.Create();
        await OdProjectStore.SaveAsync(_directory, project);
        project.EditorState.CollapsedInheritedFieldSections.Add("class-a:class-b");

        await OdProjectStore.SaveEditorStateAsync(_directory, project.EditorState);
        var loaded = await OdProjectStore.LoadAsync(_directory);

        Assert.That(loaded.EditorState.CollapsedInheritedFieldSections, Does.Contain("class-a:class-b"));
    }

    [Test]
    public async Task SavingRemovesOrphanedEntityFiles()
    {
        var project = TestProject.Create();
        await OdProjectStore.SaveAsync(_directory, project);
        project.Definitions.RemoveAt(1);

        await OdProjectStore.SaveAsync(_directory, project);

        Assert.That(Directory.EnumerateFiles(Path.Combine(_directory, "schemas"), "*.json").Count(), Is.EqualTo(1));
    }
}
