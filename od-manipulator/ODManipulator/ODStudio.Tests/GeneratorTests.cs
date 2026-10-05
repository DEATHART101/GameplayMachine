using System.Diagnostics;
using System.Security;
using System.Text.Json.Nodes;
using ODStudio.Export;
using ODStudio.Export.Unity;
using ODStudio.Generator;
using ODStudio.Model;
using ODStudio.Package;

namespace ODStudio.Tests;

public sealed class GeneratorTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "odstudio-generator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [Test]
    public async Task LockstepProjectGeneratesDeterministicContractsWithoutTransform()
    {
        OdProject project = TestProject.Create();
        project.Runtime.NetworkMode = OdNetworkMode.Lockstep;
        OdClassDefinition root = project.Definitions.OfType<OdClassDefinition>().Single();
        root.Fields.Add(new OdFieldDefinition
        {
            Name = "Power",
            Type = OdTypeReference.BuiltInType(OdBuiltInType.Single),
            DefaultValue = JsonValue.Create(1.5),
        });
        root.Fields.Add(new OdFieldDefinition
        {
            Name = "Tags",
            Type = new OdTypeReference
            {
                BuiltIn = OdBuiltInType.String,
                Container = OdContainerKind.Set,
            },
        });
        var coordinate = new OdStructDefinition
        {
            Name = "Coordinate",
            Namespace = project.DefaultNamespace,
            Fields =
            {
                new OdFieldDefinition { Name = "X", Type = OdTypeReference.BuiltInType(OdBuiltInType.Int32) },
                new OdFieldDefinition { Name = "Y", Type = OdTypeReference.BuiltInType(OdBuiltInType.Int32) },
            },
        };
        project.Definitions.Add(coordinate);
        root.Fields.Add(new OdFieldDefinition
        {
            Name = "ValuesByCoordinate",
            Type = new OdTypeReference
            {
                BuiltIn = OdBuiltInType.Int32,
                Container = OdContainerKind.Dictionary,
                DictionaryKeyBuiltIn = OdBuiltInType.None,
                DictionaryKeyDefinitionId = coordinate.Id,
            },
        });
        project.Definitions.Add(new OdInterfaceDefinition
        {
            Name = "PlayCard",
            Namespace = project.DefaultNamespace,
            InterfaceType = OdInterfaceType.PlayerInput,
            Inputs =
            {
                new OdFieldDefinition
                {
                    Name = "Cost",
                    Type = OdTypeReference.BuiltInType(OdBuiltInType.Decimal),
                },
            },
        });
        OdProjectCodeManager.EnsureCodeFiles(project);
        string packagePath = Path.Combine(_directory, "lockstep.odpkg");
        string outputPath = Path.Combine(_directory, "LockstepGenerated");
        OdPackageBuildResult package = await OdPackageCompiler.BuildAsync(project, packagePath);
        Assert.That(package.Issues, Has.None.Matches<OdIssue>(issue => issue.Severity == OdIssueSeverity.Error),
            string.Join(Environment.NewLine, package.Issues));

        OdCodeGenerationResult generation = await OdCSharpGenerator.GeneratePackageAsync(packagePath, outputPath);
        Assert.That(generation.Succeeded, Is.True, string.Join(Environment.NewLine, generation.Issues));
        string module = File.ReadAllText(Path.Combine(outputPath, "Tests", "OD", "ODModule.g.cs"));
        string runtime = File.ReadAllText(Path.Combine(outputPath, "Tests", "OD", "ODRuntime.g.cs"));
        string types = File.ReadAllText(Path.Combine(outputPath, "Tests", "OD", "ODTypes.g.cs"));
        string behaviors = File.ReadAllText(Path.Combine(outputPath, "Tests", "OD", "ODBehaviors.g.cs"));
        OdClassDefinition lockstepPlayerController = OdNetworkPackage.CreateProject(OdNetworkMode.Lockstep)
            .Definitions.OfType<OdClassDefinition>().Single();
        var legacyPlayerControllerReference = new OdTypeReference
        {
            DefinitionId = OdNetworkPackage.PlayerControllerClassId,
        };
        OdNetworkPackage.MigrateLegacyType(legacyPlayerControllerReference, OdNetworkMode.Lockstep);

        Assert.Multiple(() =>
        {
            Assert.That(module, Does.Contain("od-network:12"));
            Assert.That(module, Does.Contain("synchronization:Lockstep"));
            Assert.That(module, Does.Contain("GameplayRpcMode.Lockstep"));
            Assert.That(module, Does.Contain("writer.WriteInt64(value.RawValue)"));
            Assert.That(runtime, Does.Contain("GameplaySynchronizationMode.Lockstep"));
            Assert.That(runtime, Does.Contain("global::GMCore.Lockstep.PlayerController GetLocalPlayerController()"));
            Assert.That(runtime, Does.Contain("TryGetExecutingPlayerController(out global::GMCore.Lockstep.PlayerController playerController)"));
            Assert.That(types, Does.Contain("global::XLockstep.Fixed64 Power"));
            Assert.That(types, Does.Contain("global::XLockstep.DeterministicSet<global::System.String>"));
            Assert.That(types, Does.Contain("global::XLockstep.DeterministicMap<global::Tests.OD.Coordinate, global::System.Int32>"));
            Assert.That(types, Does.Contain("global::System.IComparable<Coordinate>"));
            Assert.That(types, Does.Contain("DeterministicComparer<global::System.Int32>.Default.Compare(X, other.X)"));
            Assert.That(behaviors, Does.Contain("global::GMCore.Lockstep.PlayerController playerController"));
            Assert.That(behaviors, Does.Contain("TryGetExecutingPlayerController"));
            Assert.That(behaviors, Does.Not.Contain("global::GMCore.StateSync.PlayerController playerController"));
            Assert.That(lockstepPlayerController.Name, Is.EqualTo("PlayerController"));
            Assert.That(lockstepPlayerController.Id,
                Is.EqualTo(OdNetworkPackage.LockstepPlayerControllerClassId));
            Assert.That(lockstepPlayerController.Id,
                Is.Not.EqualTo(OdNetworkPackage.PlayerControllerClassId));
            Assert.That(legacyPlayerControllerReference.DefinitionId,
                Is.EqualTo(OdNetworkPackage.LockstepPlayerControllerClassId));
            Assert.That(lockstepPlayerController.Fields.Select(field => field.Name),
                Is.EqualTo(new[] { "SlotID" }));
            Assert.That(OdNetworkPackage.CreateProject(OdNetworkMode.Lockstep).Definitions
                .Any(definition => definition.Id == OdNetworkPackage.TransformStructId), Is.False);
        });

        string playerInputImplementation = project.CodeFiles.Single(file =>
            file.RelativePath.EndsWith("Tests/OD/Interfaces/PlayCardParam.cs", StringComparison.Ordinal))
            .Content;
        Assert.Multiple(() =>
        {
            Assert.That(playerInputImplementation, Does.Contain("using GMCore.Lockstep;"));
            Assert.That(playerInputImplementation, Does.Contain("PlayerController playerController"));
            Assert.That(playerInputImplementation, Does.Not.Contain("LockstepPlayerController"));
            Assert.That(playerInputImplementation, Does.Not.Contain("GMCore.StateSync"));
        });
    }

    [Test]
    public void LockstepGenerationOmitsPartitionConfigurationAndCallbacks()
    {
        OdProject project = TestProject.Create();
        OdClassDefinition root = project.Definitions.OfType<OdClassDefinition>().Single();
        root.Partition = OdObjectPartitionMode.Spatial;
        project.Runtime.PartitionResolver = OdPartitionResolverKind.Custom;
        project.Runtime.ProvisionPartitions = true;
        OdProjectCodeManager.GetOrCreateGameplayMachineFunctionFile(
            project, OdGameplayMachineFunction.OnProvisionPartitions);
        OdProjectCodeManager.GetOrCreateGameplayMachineFunctionFile(
            project, OdGameplayMachineFunction.OnQueryPartitions);
        OdProjectCodeManager.GetOrCreateGameplayMachineFunctionFile(
            project, OdGameplayMachineFunction.TryResolvePartition);

        project.Runtime.NetworkMode = OdNetworkMode.Lockstep;
        OdCodeGenerationResult result = OdCSharpGenerator.GenerateFiles(project);
        string module = result.Files.Single(item => item.RelativePath.EndsWith("/ODModule.g.cs")).Content;
        string runtime = result.Files.Single(item => item.RelativePath.EndsWith("/ODRuntime.g.cs")).Content;
        string[] paths = result.Files.Select(item => item.RelativePath).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, string.Join(Environment.NewLine, result.Issues));
            Assert.That(module, Does.Contain("PartitionMode = global::GMCore.GameplayObjectPartitionMode.Manual"));
            Assert.That(runtime, Does.Not.Contain("GameplayGridPartitionResolver"));
            Assert.That(runtime, Does.Not.Contain("PartitionResolver ="));
            Assert.That(runtime, Does.Not.Contain("ProvisionConfiguredPartitions"));
            Assert.That(paths, Has.None.EndsWith("/GameplayMachine/OnProvisionPartitions.cs"));
            Assert.That(paths, Has.None.EndsWith("/GameplayMachine/OnQueryPartitions.cs"));
            Assert.That(paths, Has.None.EndsWith("/GameplayMachine/TryResolvePartition.cs"));
            Assert.That(OdGameplayMachineFunctionCatalog.IsAvailable(
                project, OdGameplayMachineFunction.OnGameStart), Is.True);
            Assert.That(OdGameplayMachineFunctionCatalog.IsAvailable(
                project, OdGameplayMachineFunction.OnQueryPartitions), Is.False);
            Assert.That(() => OdProjectCodeManager.GetOrCreateGameplayMachineFunctionFile(
                project, OdGameplayMachineFunction.OnQueryPartitions), Throws.InvalidOperationException);
        });
    }

    [Test]
    public void ResourceInterfaceCallGeneratesInheritedContractsCopiesAndTypedDispatch()
    {
        OdProject project = TestProject.Create();
        var piece = new OdFieldDefinition
        {
            Name = "Piece",
            Type = OdTypeReference.BuiltInType(OdBuiltInType.Int32),
        };
        var position = new OdFieldDefinition
        {
            Name = "ToPosition",
            Type = OdTypeReference.BuiltInType(OdBuiltInType.Int32),
        };
        var movePiece = new OdInterfaceDefinition
        {
            Name = "MovePiece",
            Namespace = project.DefaultNamespace,
            Abstract = true,
            Inputs = { piece, position },
        };
        var distance = new OdFieldDefinition
        {
            Name = "MaxDistance",
            Type = OdTypeReference.BuiltInType(OdBuiltInType.Int32),
        };
        var moveQueen = new OdInterfaceDefinition
        {
            Name = "MoveQueen",
            Namespace = project.DefaultNamespace,
            BaseInterfaceId = movePiece.Id,
            Inputs = { distance },
        };
        var moveAction = new OdFieldDefinition
        {
            Name = "MoveAction",
            Type = new OdTypeReference { DefinitionId = movePiece.Id },
        };
        var pieceResource = new OdResourceDefinition
        {
            Name = "PieceDefinition",
            Namespace = project.DefaultNamespace,
            Fields = { moveAction },
        };
        JsonObject call = OdInterfaceCallValue.Create(moveQueen.Id);
        OdInterfaceCallValue.GetValues(call)![distance.Id.ToString("D")] = 7;
        var queen = new OdDataRecord { Name = "Queen" };
        queen.Values[moveAction.Id] = call;
        project.Definitions.Add(movePiece);
        project.Definitions.Add(moveQueen);
        project.Definitions.Add(pieceResource);
        project.DataSets.Add(new OdDataSet
        {
            Name = "Pieces",
            DefinitionId = pieceResource.Id,
            Records = { queen },
        });

        OdCodeGenerationResult result = OdCSharpGenerator.GenerateFiles(project);
        string behaviors = result.Files.Single(file => file.RelativePath.EndsWith("ODBehaviors.g.cs")).Content;
        string types = result.Files.Single(file => file.RelativePath.EndsWith("ODTypes.g.cs")).Content;
        string resources = result.Files.Single(file => file.RelativePath.EndsWith("ODResources.g.cs")).Content;

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, string.Join(Environment.NewLine, result.Issues));
            Assert.That(behaviors, Does.Contain("public interface IMovePiece : global::GMCore.IODInterfaceCall"));
            Assert.That(behaviors, Does.Contain("public interface IMoveQueen : global::Tests.OD.IMovePiece"));
            Assert.That(behaviors, Does.Contain("public sealed class MoveQueenCall : global::Tests.OD.IMoveQueen"));
            Assert.That(behaviors, Does.Contain("new MoveQueenParam { Piece = Piece, ToPosition = ToPosition, MaxDistance = MaxDistance }"));
            Assert.That(behaviors, Does.Contain("global::ODCore.EventError global::GMCore.IODInterfaceCall.CanExecute"));
            Assert.That(behaviors, Does.Contain("return machine.CanExecute<MoveQueenResult>(new MoveQueenParam"));
            Assert.That(types, Does.Contain("public global::Tests.OD.IMovePiece? MoveAction"));
            Assert.That(types, Does.Contain("CopyUntyped()"));
            Assert.That(resources, Does.Contain("new global::Tests.OD.MoveQueenCall"));
            Assert.That(resources, Does.Contain("MaxDistance = 7"));
            Assert.That(result.Files, Has.None.Matches<OdGeneratedFile>(file =>
                file.RelativePath.EndsWith("MovePieceParam.cs", StringComparison.Ordinal)));
        });
    }

    [Test]
    public async Task PackageGeneratesDeterministicGameplayMachineSource()
    {
        var project = TestProject.Create();
        OdClassDefinition rootClass = project.Definitions.OfType<OdClassDefinition>().Single();
        rootClass.Replication = OdObjectReplicationMode.OwnerOnly;
        rootClass.IsClasses.Add(OdNetworkPackage.PawnClassId);
        rootClass.Relevancy = OdObjectRelevancyMode.Spatial;
        rootClass.Partition = OdObjectPartitionMode.Spatial;
        AddExtendedRuntimeTypes(project);
        var playerInput = new OdInterfaceDefinition
        {
            Name = "MovePlayer",
            Namespace = "Tests.OD",
            InterfaceType = OdInterfaceType.PlayerInput,
            Inputs =
            {
                new OdFieldDefinition
                {
                    Name = "Actor",
                    Type = new OdTypeReference { DefinitionId = rootClass.Id },
                    NotNull = true,
                },
            },
        };
        project.Definitions.Add(playerInput);
        OdProjectCodeManager.EnsureCodeFiles(project);
        OdCodeFile resolverFile = OdProjectCodeManager.GetOrCreateResolverFile(project);
        string packagePath = Path.Combine(_directory, "test.odpkg");
        string outputPath = Path.Combine(_directory, "Generated");
        OdPackageBuildResult package = await OdPackageCompiler.BuildAsync(project, packagePath);
        Assert.That(package.Issues, Has.None.Matches<OdIssue>(issue => issue.Severity == OdIssueSeverity.Error),
            string.Join(Environment.NewLine, package.Issues));

        var first = await OdCSharpGenerator.GeneratePackageAsync(packagePath, outputPath);
        Assert.That(first.Succeeded, Is.True, string.Join(Environment.NewLine, first.Issues));
        var firstContents = first.Files.ToDictionary(item => item.RelativePath, item => item.Content);
        string damageImplementationPath = Path.Combine(outputPath, "Tests", "OD", "Interfaces", "DamageParam.cs");
        string drivenImplementationPath = Path.Combine(outputPath, "Tests", "OD", "DrivenValues", "Actor_IsAlive.cs");
        const string preservedImplementation = "// external edit must be overwritten";
        const string preservedDrivenImplementation = "// external edit must be overwritten";
        await File.WriteAllTextAsync(damageImplementationPath, preservedImplementation);
        await File.WriteAllTextAsync(drivenImplementationPath, preservedDrivenImplementation);
        var second = await OdCSharpGenerator.GeneratePackageAsync(packagePath, outputPath);

        Assert.Multiple(() =>
        {
            Assert.That(first.Succeeded, Is.True);
            Assert.That(first.Files, Has.Some.Matches<OdGeneratedFile>(item => item.RelativePath == "Tests/OD/Interfaces/DamageParam.cs"));
            Assert.That(first.Files, Has.Some.Matches<OdGeneratedFile>(item => item.RelativePath == "Tests/OD/DrivenValues/Actor_IsAlive.cs"));
            Assert.That(first.Files, Has.Some.Matches<OdGeneratedFile>(item => item.RelativePath == "Tests/OD/ODRuntime.g.cs"));
            Assert.That(first.Files, Has.Some.Matches<OdGeneratedFile>(item => item.RelativePath == resolverFile.RelativePath));
            Assert.That(first.Files.All(item => item.OverwriteExisting), Is.True);
            Assert.That(second.Files.All(item => firstContents[item.RelativePath] == item.Content), Is.True);
            Assert.That(File.ReadAllText(damageImplementationPath), Is.Not.EqualTo(preservedImplementation));
            Assert.That(File.ReadAllText(drivenImplementationPath), Is.Not.EqualTo(preservedDrivenImplementation));
            string manifest = File.ReadAllText(Path.Combine(outputPath, ".gameplaymachine-generated"));
            Assert.That(manifest, Does.Contain("Interfaces"));
            string module = File.ReadAllText(Path.Combine(outputPath, "Tests", "OD", "ODModule.g.cs"));
            Assert.That(module, Does.Contain(project.Ods.Single().Id.ToString("N")));
            Assert.That(module, Does.Contain("global::GMCore.IODNetworkModule"));
            Assert.That(module, Does.Contain("global::GMCore.IODDebugModule"));
            Assert.That(module, Does.Not.Contain("global::GMCore.IODSpatialModule"));
            Assert.That(module, Does.Contain("RelevancyMode = global::GMCore.GameplayObjectRelevancyMode.Spatial"));
            Assert.That(module, Does.Contain("PartitionMode = global::GMCore.GameplayObjectPartitionMode.Spatial"));
            Assert.That(module, Does.Contain("public ulong DebugSchemaID"));
            Assert.That(module, Does.Contain("GetAllODDebugClassMetas"));
            Assert.That(module, Does.Contain("GetAllODDebugInterfaceMetas"));
            Assert.That(module, Does.Contain("global::GMCore.ODDebugInvocationResult"));
            Assert.That(module, Does.Contain("AssignableClassIDs = new global::System.Collections.Generic.List<ulong>"));
            Assert.That(module, Does.Contain("GameplayObjectClassID = global::GMCore.ODSaveHash.Compute"));
            Assert.That(module, Does.Contain("ReplicationMode = global::GMCore.ODFieldReplicationMode.AllClients"));
            Assert.That(module, Does.Contain("ReplicationMode = global::GMCore.GameplayObjectReplicationMode.OwnerOnly"));
            Assert.That(module, Does.Contain("ReplicationMode = global::GMCore.GameplayObjectReplicationMode.None"));
            Assert.That(module, Does.Contain("global::GMCore.Pawn.ClassName"));
            Assert.That(module, Does.Contain("GMCore.Pawn.PlayerController"));
            Assert.That(module, Does.Contain("InitialValueFactory = () => new global::GMCore.Collections.ListCollection<global::System.String>()"));
            Assert.That(module, Does.Contain("InitialValueFactory = () => new global::GMCore.Collections.SetCollection<global::System.String>()"));
            Assert.That(module, Does.Contain("InitialValueFactory = () => new global::GMCore.Collections.MapCollection<global::System.String, global::Tests.OD.Stats?>()"));
            Assert.That(module, Does.Contain("DeltaKind = global::GMCore.ODCollectionDeltaKind.Set"));
            Assert.That(module, Does.Contain("DeltaKind = global::GMCore.ODCollectionDeltaKind.Map"));
            Assert.That(module, Does.Contain("DrivingOfs = new global::System.Collections.Generic.List<global::GMCore.ODFieldName> { global::Tests.OD.Actor.Fields.IsAlive }"));
            Assert.That(module, Does.Contain("replication:AllClients"));
            Assert.That(module, Does.Contain("GetAllODRpcMetas"));
            Assert.That(module, Does.Contain("WriteVarInt32"));
            Assert.That(module, Does.Contain("ReadVarInt32"));
            Assert.That(module, Does.Contain("WriteVarInt64"));
            Assert.That(module, Does.Contain("ReadVarInt64"));
            Assert.That(module, Does.Contain("WriteVarInt32((int)"));
            Assert.That(module, Does.Contain(")reader.ReadVarInt32()"));
            Assert.That(module, Does.Contain("od-network:11"));
            Assert.That(module, Does.Contain($"od:{project.Ods.Single().Id:N}:rpc:"));
            Assert.That(module, Does.Contain("global::GMCore.IODMulticastEventNetworkModule"));
            Assert.That(module, Does.Contain("GetAllODMulticastEventMetas"));
            Assert.That(module, Does.Contain("InterfaceType = global::GMCore.GameplayInterfaceType.PlayerInput"));
            Assert.That(module, Does.Contain("if (!value.Actor.ObjectID.IsValid)"));
            Assert.That(module, Does.Contain("writer.WriteObjectID(value.Actor.ObjectID);"));
            Assert.That(module, Does.Contain("NotNull gameplay object reference cannot be null."));
            Assert.That(module, Does.Contain("global::Tests.OD.Actor.ClassName, global::Tests.OD.Named.ClassName"));
            Assert.That(module, Does.Not.Contain("GetGlobalInstances"));
            Assert.That(module, Does.Not.Contain("GeneratedGlobalInstances"));
            string types = File.ReadAllText(Path.Combine(outputPath, "Tests", "OD", "ODTypes.g.cs"));
            Assert.That(types, Does.Contain("public enum Mood : int"));
            Assert.That(types, Does.Contain("implicit operator global::Tests.OD.Actor"));
            Assert.That(types, Does.Contain("implicit operator global::Tests.OD.Named"));
            Assert.That(types, Does.Contain("global::System.IEquatable<Actor>"));
            Assert.That(types, Does.Contain("ReferenceEquals(Machine, other.Machine) && ObjectID.Equals(other.ObjectID)"));
            Assert.That(types, Does.Contain("public global::System.String Alias"));
            Assert.That(types, Does.Contain("public global::GMCore.StateSync.PlayerController? Controller"));
            Assert.That(types, Does.Contain("implicit operator global::GMCore.Pawn"));
            Assert.That(types, Does.Contain("public global::GMCore.StateSync.PlayerController? PlayerController"));
            Assert.That(types, Does.Contain("AfterHealthChanged"));
            Assert.That(types, Does.Contain("struct ActorReference"));
            Assert.That(types, Does.Contain("public partial struct Coordinate : global::GMCore.IODStruct, global::System.IEquatable<Coordinate>"));
            Assert.That(types, Does.Contain("public bool Equals(Coordinate other)"));
            Assert.That(types, Does.Contain("public static bool operator ==(Coordinate left, Coordinate right)"));
            Assert.That(types, Does.Contain("public partial class Stats : global::GMCore.IODStruct"));
            Assert.That(types, Does.Contain("enum SwitchTypes"));
            Assert.That(types, Does.Contain("public global::GMCore.Collections.List<global::System.String> Tags"));
            Assert.That(types, Does.Contain("DrivenValueImplementations.Get_Actor_IsAlive(Health)"));
            Assert.That(types, Does.Not.Contain("GetOrCreateGameplayObjectValue"));
            string behaviors = File.ReadAllText(Path.Combine(outputPath, "Tests", "OD", "ODBehaviors.g.cs"));
            Assert.That(behaviors, Does.Not.Contain("GetCallParam"));
            Assert.That(behaviors, Does.Not.Contain("OnCanExecute"));
            Assert.That(behaviors, Does.Not.Contain("OnDoExecute"));
            Assert.That(behaviors, Does.Not.Contain("Default"));
            Assert.That(behaviors, Does.Not.Contain("struct Damage :"));
            Assert.That(behaviors, Does.Not.Contain(".From(this)"));
            Assert.That(behaviors, Does.Contain("ICheckableInterface<DamageResult>.CanExecute"));
            Assert.That(behaviors, Does.Contain("CanExecute((global::Tests.OD.TestGameGameplayMachineProxy)machine, this)"));
            Assert.That(behaviors, Does.Not.Contain(".IsA<"));
            Assert.That(behaviors, Does.Contain("struct DamageResult"));
            Assert.That(behaviors, Does.Contain("[global::System.Diagnostics.CodeAnalysis.NotNull]"));
            Assert.That(behaviors, Does.Contain("public global::Tests.OD.Actor Target;"));
            Assert.That(behaviors, Does.Not.Contain("public global::Tests.OD.Actor? Target;"));
            Assert.That(behaviors, Does.Contain("struct NotifyActorParam : global::GMCore.ICheckableInterface<NotifyActorResult>, global::GMCore.IODEvent, global::GMCore.IGameplayInterface"));
            Assert.That(behaviors, Does.Contain("struct PredictActorParam : global::GMCore.IPredictableInterface"));
            Assert.That(behaviors, Does.Contain("struct NotifyActorResult"));
            Assert.That(behaviors, Does.Not.Contain("IReplicatedInterface<NotifyActorResult>"));
            Assert.That(behaviors, Does.Contain("PredictionMode => global::GMCore.GameplayPredictionMode.Both"));
            Assert.That(behaviors, Does.Contain("MovePlayerParam : global::GMCore.IReplicatedInterface, global::GMCore.IODEvent, global::GMCore.IPlayerInputInterface"));
            Assert.That(behaviors, Does.Contain("TryGetExecutingPlayerController(out global::GMCore.StateSync.PlayerController playerController)"));
            Assert.That(behaviors, Does.Contain("CanExecute(typedMachine, playerController, this)"));
            string implementation = firstContents["Tests/OD/Interfaces/DamageParam.cs"];
            Assert.That(implementation, Does.Contain("partial struct DamageParam"));
            Assert.That(implementation, Does.Contain("TestGameGameplayMachineProxy machine"));
            Assert.That(implementation, Does.Contain("private static EventError CanExecute("));
            Assert.That(implementation, Does.Not.Contain("global::"));
            Assert.That(implementation, Does.Contain("DamageParam input"));
            Assert.That(implementation, Does.Not.Contain("in DamageParam input"));
            Assert.That(implementation, Does.Contain("private static void DoExecute("));
            Assert.That(implementation, Does.Not.Contain("OnCanExecute"));
            Assert.That(implementation, Does.Not.Contain("OnDoExecute"));
            string predictableImplementation = firstContents["Tests/OD/Interfaces/PredictActorParam.cs"];
            Assert.That(predictableImplementation, Does.Contain(" OnPredictClient("));
            Assert.That(predictableImplementation, Does.Contain(" OnSuccessClient("));
            Assert.That(predictableImplementation, Does.Contain(" OnFailClient("));
            Assert.That(predictableImplementation, Does.Contain("PredictActorParam input"));
            Assert.That(predictableImplementation, Does.Not.Contain("in PredictActorParam input"));
            string routineImplementation = firstContents["Tests/OD/Interfaces/UpdateActorParam.cs"];
            Assert.That(routineImplementation, Does.Contain("UpdateActorParam input"));
            Assert.That(routineImplementation, Does.Not.Contain("in UpdateActorParam input"));
            Assert.That(routineImplementation, Does.Contain("UpdateActorParam input)"));
            Assert.That(routineImplementation, Does.Not.Contain("IEnumerator DoExecute(global::GMCore.GameplayMachine.GameplayMachineProxy machine, in UpdateActorParam"));
            string playerInputImplementation = firstContents["Tests/OD/Interfaces/MovePlayerParam.cs"];
            Assert.That(playerInputImplementation, Does.Contain("using GMCore.StateSync;"));
            Assert.That(playerInputImplementation, Does.Contain("PlayerController playerController"));
            Assert.That(playerInputImplementation, Does.Contain("CanExecute(TestGameGameplayMachineProxy machine, PlayerController playerController, MovePlayerParam input)"));
            Assert.That(playerInputImplementation, Does.Contain("DoExecute(TestGameGameplayMachineProxy machine, PlayerController playerController, MovePlayerParam input)"));
            Assert.That(playerInputImplementation, Does.Not.Contain("global::"));
        });
    }

    [Test]
    public void ResourceReferencesAreEncodedByStableIdWithoutRecursiveCodecGeneration()
    {
        OdProject project = TestProject.Create();
        var item = new OdResourceDefinition { Name = "Item", Namespace = "Tests.OD" };
        var related = new OdFieldDefinition
        {
            Name = "Related",
            Type = new OdTypeReference { DefinitionId = item.Id, Nullable = true },
        };
        item.Fields.Add(related);
        project.Definitions.OfType<OdClassDefinition>().Single().Fields.Add(new OdFieldDefinition
        {
            Name = "EquippedItem",
            Type = new OdTypeReference { DefinitionId = item.Id, Nullable = true },
        });
        var first = new OdDataRecord { Name = "First" };
        var second = new OdDataRecord { Name = "Second" };
        first.Values[related.Id] = JsonValue.Create(second.Id.ToString("D"));
        second.Values[related.Id] = JsonValue.Create(first.Id.ToString("D"));
        project.Definitions.Add(item);
        project.DataSets.Add(new OdDataSet
        {
            Name = "Items",
            DefinitionId = item.Id,
            Records = { first, second },
        });

        OdCodeGenerationResult result = OdCSharpGenerator.GenerateFiles(project);
        string module = result.Files.Single(file => file.RelativePath.EndsWith("ODModule.g.cs")).Content;
        string data = result.Files.Single(file => file.RelativePath.EndsWith("ODResources.g.cs")).Content;

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, string.Join(Environment.NewLine, result.Issues));
            Assert.That(module, Does.Contain("ResourceCatalogID => global::GMCore.ODSaveHash.Compute(\"od-resource-catalog:2|"));
            Assert.That(module, Does.Contain(first.Id.ToString("N")));
            Assert.That(module, Does.Contain(second.Id.ToString("N")));
            Assert.That(module, Does.Not.Contain("ResourceCatalogID => 0UL"));
            Assert.That(module, Does.Contain("GeneratedResources.GetId("));
            Assert.That(module, Does.Contain("GeneratedResources.Resolve<global::Tests.OD.Item>(reader.ReadResourceID())"));
            Assert.That(module, Does.Contain("IsResourceReference = true"));
            Assert.That(module, Does.Contain("new global::GMCore.ODDebugResourceOption"));
            Assert.That(module, Does.Contain("Name = \"Items / First\""));
            Assert.That(module, Does.Contain("Value = global::Tests.OD.GeneratedResources.Items_First"));
            Assert.That(module, Does.Contain("writer.WriteBoolean(value != null);"));
            Assert.That(module, Does.Not.Contain("value.HasValue"));
            Assert.That(data, Does.Contain("Items_First.Related = global::Tests.OD.GeneratedResources.Items_Second"));
            Assert.That(data, Does.Contain("Items_Second.Related = global::Tests.OD.GeneratedResources.Items_First"));
            Assert.That(data, Does.Not.Contain("GeneratedStaticData"));
        });
    }

    [Test]
    public void SwitchStructResourceValueGeneratesOnlyTheSelectedVariant()
    {
        OdProject project = TestProject.Create();
        var count = new OdFieldDefinition
        {
            Name = "Count",
            Type = OdTypeReference.BuiltInType(OdBuiltInType.Int32),
        };
        var label = new OdFieldDefinition
        {
            Name = "Label",
            Type = OdTypeReference.BuiltInType(OdBuiltInType.String),
        };
        var choice = new OdSwitchStructDefinition
        {
            Name = "RewardChoice",
            Namespace = "Tests.OD",
            Fields = { count, label },
        };
        var configuredChoice = new OdFieldDefinition
        {
            Name = "Choice",
            Type = new OdTypeReference { DefinitionId = choice.Id },
        };
        var reward = new OdResourceDefinition
        {
            Name = "Reward",
            Namespace = "Tests.OD",
            Fields = { configuredChoice },
        };
        var record = new OdDataRecord { Name = "Welcome" };
        record.Values[configuredChoice.Id] = new JsonObject
        {
            [label.Id.ToString("D")] = JsonValue.Create("Starter"),
        };
        project.Definitions.Add(choice);
        project.Definitions.Add(reward);
        project.DataSets.Add(new OdDataSet
        {
            Name = "Rewards",
            DefinitionId = reward.Id,
            Records = { record },
        });

        OdCodeGenerationResult result = OdCSharpGenerator.GenerateFiles(project);
        string resources = result.Files.Single(file => file.RelativePath.EndsWith("ODResources.g.cs")).Content;

        Assert.That(resources, Does.Contain(
            "new global::Tests.OD.RewardChoice { Label = \"Starter\" }"));
        Assert.That(resources, Does.Not.Contain(
            "new global::Tests.OD.RewardChoice { Count ="));
    }

    [Test]
    public void RuntimeGenerationConfiguresRootAndInitialScene()
    {
        OdProject project = TestProject.Create();
        OdClassDefinition actor = project.Definitions.OfType<OdClassDefinition>().Single();
        var sceneClass = new OdClassDefinition { Name = "WorldState", Namespace = project.DefaultNamespace };
        project.Definitions.Add(sceneClass);
        var sceneObject = new OdSceneObject { Name = "World", ClassDefinitionId = sceneClass.Id };
        var scene = new OdScene
        {
            Name = "Main",
            OdId = project.Ods.Single().Id,
            Objects = { sceneObject },
        };
        project.Scenes.Add(scene);
        project.InitialSceneId = scene.Id;
        project.Runtime = new OdRuntimeSettings
        {
            SpatialCellSize = 64f,
            PartitionResolver = OdPartitionResolverKind.Grid,
            PartitionCellSize = 16f,
            PartitionIncludeY = true,
            LoadMargin = 8f,
            UnloadDelaySeconds = 1.5f,
            MaxLoadsPerUpdate = 12,
            ManagePartitionLoading = false,
        };

        OdCodeGenerationResult result = OdCSharpGenerator.GenerateFiles(project);
        string scenes = result.Files.Single(file => file.RelativePath.EndsWith("ODScenes.g.cs")).Content;
        string runtime = result.Files.Single(file => file.RelativePath.EndsWith("ODRuntime.g.cs")).Content;

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, string.Join(Environment.NewLine, result.Issues));
            Assert.That(scenes, Does.Not.Contain("instance.SetRoot"));
            Assert.That(runtime, Does.Contain("settings.RootGameplayObjectClass = global::Tests.OD.Actor.ClassName;"));
            Assert.That(runtime, Does.Contain("settings.InitialScene = global::Tests.OD.MainScene.Instance;"));
            Assert.That(runtime, Does.Contain("class TestGameGameplayMachineProxy"));
            Assert.That(runtime, Does.Contain("public new global::Tests.OD.Actor GetRoot()"));
            Assert.That(runtime, Does.Contain("return new TestGameGameplayMachineProxy(this);"));
            Assert.That(runtime, Does.Contain("CellSize = 64f"));
            Assert.That(runtime, Does.Contain("new global::GMCore.GameplayGridPartitionResolver(16f, includeY: true)"));
            Assert.That(runtime, Does.Contain("LoadMargin = 8f"));
            Assert.That(runtime, Does.Contain("UnloadDelaySeconds = 1.5f"));
            Assert.That(runtime, Does.Contain("MaxLoadsPerUpdate = 12"));
            Assert.That(runtime, Does.Contain("ManagePartitionLoading = false"));
            Assert.That(runtime, Does.Not.Contain("RuntimeConfiguration.Configure"));
        });
    }

    [Test]
    public async Task PackageGenerationOverwritesFilesOutsideTheOdProject()
    {
        OdProject project = TestProject.Create();
        AddExtendedRuntimeTypes(project);
        string packagePath = Path.Combine(_directory, "test.odpkg");
        string outputPath = Path.Combine(_directory, "Generated");
        string legacyInterfacePath = Path.Combine(outputPath, "Tests", "OD", "Interfaces", "DamageParam.cs");
        string legacyDrivenPath = Path.Combine(outputPath, "Tests", "OD", "DrivenValues", "Actor_IsAlive.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyInterfacePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyDrivenPath)!);
        await File.WriteAllTextAsync(legacyInterfacePath, "// legacy interface implementation");
        await File.WriteAllTextAsync(legacyDrivenPath, "// legacy driven implementation");
        await OdPackageCompiler.BuildAsync(project, packagePath);

        OdCodeGenerationResult result = await OdCSharpGenerator.GeneratePackageAsync(packagePath, outputPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, string.Join(Environment.NewLine, result.Issues));
            Assert.That(File.Exists(legacyInterfacePath), Is.True);
            Assert.That(File.Exists(legacyDrivenPath), Is.True);
            Assert.That(File.ReadAllText(legacyInterfacePath), Is.Not.EqualTo("// legacy interface implementation"));
            Assert.That(File.ReadAllText(legacyDrivenPath), Is.Not.EqualTo("// legacy driven implementation"));
            Assert.That(Directory.Exists(Path.Combine(outputPath, "User")), Is.False);
        });
    }

    [Test]
    public async Task GeneratedSourceCompilesAndGameplayCollectionsRaiseCriticalEvents()
    {
        var project = TestProject.Create();
        AddExtendedRuntimeTypes(project);
        var rootClass = new OdClassDefinition { Name = "GameRoot", Namespace = project.DefaultNamespace };
        project.Definitions.Add(rootClass);
        project.RootClassId = rootClass.Id;
        project.Runtime.PartitionResolver = OdPartitionResolverKind.Custom;
        project.Runtime.PartitionCellSize = 16f;
        OdProjectCodeManager.GetOrCreateGameplayMachineFunctionFile(
            project, OdGameplayMachineFunction.OnQueryPartitions);
        OdProjectCodeManager.GetOrCreateGameplayMachineFunctionFile(
            project, OdGameplayMachineFunction.TryResolvePartition);
        string packagePath = Path.Combine(_directory, "test.odpkg");
        string outputPath = Path.Combine(_directory, "Generated");
        await OdPackageCompiler.BuildAsync(project, packagePath);
        var result = await OdCSharpGenerator.GeneratePackageAsync(packagePath, outputPath);
        Assert.That(result.Succeeded, Is.True, string.Join(Environment.NewLine, result.Issues));
        string drivenPath = Path.Combine(outputPath, "Tests", "OD", "DrivenValues", "Actor_IsAlive.cs");
        await File.WriteAllTextAsync(drivenPath, """
            #nullable enable
            namespace Tests.OD;
            public static partial class DrivenValueImplementations
            {
                public static bool Get_Actor_IsAlive(int Health) => Health > 0;
            }
            """);
        string damagePath = Path.Combine(outputPath, "Tests", "OD", "Interfaces", "DamageParam.cs");
        await File.WriteAllTextAsync(damagePath, """
            #nullable enable
            namespace Tests.OD;
            public partial struct DamageParam
            {
                private static global::ODCore.EventError CanExecute(
                    global::Tests.OD.TestGameGameplayMachineProxy machine,
                    DamageParam input) => true;

                private static void DoExecute(
                    global::Tests.OD.TestGameGameplayMachineProxy machine,
                    DamageParam input,
                    ref DamageResult outResult)
                {
                    _ = machine.GetRoot().ObjectID;
                    input.Target.Health = 10;
                    outResult.Applied = true;
                }
            }
            """);

        string coreProject = FindGameplayMachineCoreProject();
        string projectFile = Path.Combine(_directory, "GeneratedCompile.csproj");
        string escapedReference = SecurityElement.Escape(coreProject)!;
        await File.WriteAllTextAsync(projectFile, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <WarningsAsErrors>CS8600;CS8601;CS8602;CS8603;CS8604;CS8618;CS8619;CS8625</WarningsAsErrors>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{escapedReference}}" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(_directory, "Program.cs"), """
            global::Tests.OD.Money wallet = 5;
            global::Tests.OD.Money price = 2;
            global::Tests.OD.Money remaining = wallet - price;
            if ((int)remaining != 3 || !(wallet > price))
                throw new global::System.Exception("IsStruct numeric forwarding failed.");
            var runtime = new global::Tests.OD.TestGameRuntime();
            var module = new global::Tests.OD.TestGameModule();
            var machine = global::GMCore.GameplayMachineBase.SpawnGameplayMachine(
                null,
                runtime.CreateSettings(),
                module);
            var first = machine.CreateGameplayObject<global::Tests.OD.Actor>();
            first.Parent = new global::Tests.OD.Actor
            {
                Machine = machine,
                ObjectID = new global::GMCore.GObjectID { ID = first.ObjectID.ID + 1 },
            };
            if (first.Parent is not null)
                throw new global::System.Exception("An unavailable GameplayObject reference did not resolve to null.");

            var second = machine.CreateGameplayObject<global::Tests.OD.Actor>();
            if (first.Parent is not { } arrived || arrived.ObjectID.ID != second.ObjectID.ID)
                throw new global::System.Exception("A GameplayObject reference did not recover after its object arrived.");
            var composite = machine.CreateGameplayObject<global::Tests.OD.CompositeActor>();

            first.Parent = second;
            first.RelatedActors.Add(second);
            first.ActorSet.Add(second);
            first.ActorByName.Add("second", second);
            first.ActorByActor.Add(second, first);
            if (!first.ActorByActor.TryGet(second, out var mappedActor) || mappedActor is not { } actualActor || actualActor != first)
                throw new global::System.Exception("GameplayObject Map key identity was not stable.");
            first.ActorByActor.Remove(second);
            var coordinate = new global::Tests.OD.Coordinate { X = 3, Y = 4 };
            var equalCoordinate = new global::Tests.OD.Coordinate { X = 3, Y = 4 };
            if (coordinate != equalCoordinate || coordinate.GetHashCode() != equalCoordinate.GetHashCode())
                throw new global::System.Exception("Struct value equality was not stable.");
            first.ValueByCoordinate.Add(coordinate, 7);
            if (!first.ValueByCoordinate.TryGet(equalCoordinate, out var coordinateValue) || coordinateValue != 7)
                throw new global::System.Exception("Struct Map key equality was not stable.");
            var actorReference = new global::Tests.OD.ActorReference { Actor = second };
            var equalActorReference = new global::Tests.OD.ActorReference { Actor = second };
            first.ValueByReference.Add(actorReference, 9);
            if (!first.ValueByReference.TryGet(equalActorReference, out var referenceValue) || referenceValue != 9)
                throw new global::System.Exception("Switch Struct Map key equality was not stable.");
            first.Stats = global::Tests.OD.GeneratedResources.Stats_DefaultStats;
            first.Reference = new global::Tests.OD.ActorReference { Actor = second };
            if (first.Parent is null || first.RelatedActors[0] is null || first.ActorSet.First() is null ||
                first.ActorByName["second"] is null || first.Reference.Actor is null)
                throw new global::System.Exception("Valid GameplayObject references were unexpectedly hidden.");

            int drivenChanged = 0;
            var drivenChangedHandle = first.AfterIsAliveChanged.Bind((object context) => drivenChanged++);
            first.Health = 0;
            if (first.IsAlive || drivenChanged != 1)
                throw new global::System.Exception($"Driven field result/callback was invalid: value={first.IsAlive}, callbacks={drivenChanged}.");

            int listChanged = 0;
            int setAdded = 0;
            int setRemoved = 0;
            int setChanged = 0;
            int mapAdded = 0;
            int mapRemoved = 0;
            int mapItemChanged = 0;
            int mapSpecificItemChanged = 0;
            int mapChanged = 0;
            var tags = first.Tags;
            var labels = first.Labels;
            var statsByName = first.StatsByName;
            var listChangedHandle = first.TagsBinder.Bind((object context) => listChanged++);
            var setItemHandle = first.LabelsBinder.BindItemChanged(change =>
            {
                if (change.ChangeType == global::GMCore.CollectionItemChangeType.Add) setAdded++;
                if (change.ChangeType == global::GMCore.CollectionItemChangeType.Remove) setRemoved++;
            });
            var setChangedHandle = first.LabelsBinder.BindChanged((object context) => setChanged++);
            var mapItemHandle = first.StatsByNameBinder.BindItemChanged(change =>
            {
                if (change.ChangeType == global::GMCore.CollectionItemChangeType.Add) mapAdded++;
                if (change.ChangeType == global::GMCore.CollectionItemChangeType.Remove) mapRemoved++;
                mapItemChanged++;
                if (change.Item == "stats") mapSpecificItemChanged++;
            });
            var mapChangedHandle = first.StatsByNameBinder.BindChanged((object context) => mapChanged++);

            tags.Add("first");
            tags[0] = "changed";
            tags.Remove("changed");
            labels.Add("label");
            labels.Add("label");
            labels.Remove("label");
            statsByName.Add("stats", new global::Tests.OD.Stats { Score = 1 });
            statsByName["stats"] = new global::Tests.OD.Stats { Score = 2 };
            statsByName.Remove("stats");

            if (listChanged != 3)
                throw new global::System.Exception($"List Changed event count was {listChanged}, expected 3.");
            if (setAdded != 1 || setRemoved != 1 || setChanged != 2)
                throw new global::System.Exception($"Set events were Add={setAdded}, Remove={setRemoved}, Changed={setChanged}.");
            if (mapAdded != 1 || mapRemoved != 1 || mapItemChanged != 3 || mapSpecificItemChanged != 3 || mapChanged != 3)
                throw new global::System.Exception($"Map events were Add={mapAdded}, Remove={mapRemoved}, ItemChanged={mapItemChanged}, SpecificItem={mapSpecificItemChanged}, Changed={mapChanged}.");

            if (first.Tags.Count != 0 || first.Labels.Count != 0 || first.StatsByName.Count != 0 || composite.Tags.Count != 0)
                throw new global::System.Exception("Collection fields were not initialized during object creation.");
            if (second.Tags.Count != 0 || second.Labels.Count != 0 || second.StatsByName.Count != 0)
                throw new global::System.Exception("Collection fields are shared between GameplayObjects.");
            machine.EnterCacheMode();
            var cached = machine.CreateGameplayObject<global::Tests.OD.Actor>();
            if (cached.Tags.Count != 0 || cached.Labels.Count != 0 || cached.StatsByName.Count != 0)
                throw new global::System.Exception("Cached GameplayObject collections were not initialized.");
            first.Parent = cached;
            if (first.Parent is null)
                throw new global::System.Exception("A GameplayObject created in the active transaction was hidden.");
            first.Parent = second;
            machine.DeleteGameplayObject(second);
            if (first.Parent is not null || first.RelatedActors.Count != 0 || first.ActorSet.Count != 0 ||
                first.ActorByName.Count != 0 || first.Reference.Actor is not null)
                throw new global::System.Exception("Dangling GameplayObject references did not resolve to null.");
            machine.LeaveCacheMode();
            if (cached.Tags.Count != 0 || cached.Labels.Count != 0 || cached.StatsByName.Count != 0)
                throw new global::System.Exception("Cached GameplayObject collections were not committed.");
            if (first.RelatedActors.Count != 0 || first.ActorSet.Count != 0 || first.ActorByName.Count != 0)
                throw new global::System.Exception("Deleted GameplayObject references remained in a GameplayMachine collection.");

            global::GMCore.InMemoryGameplayNetworkTransport.CreatePair(out var serverTransport, out var clientTransport);
            machine.EnableNetworking(serverTransport);
            var client = global::GMCore.GameplayMachineBase.CreateClientProxy(
                null,
                new global::GMCore.GameplayMachineSettings(),
                module);
            string? networkFailureReason = null;
            var networkStateHandle = client.BindMachineEvent<global::GMCore.GameplayNetworkStateChangedParam>(
                (global::GMCore.GameplayNetworkStateChangedParam state) => networkFailureReason = state.Reason);
            client.ConnectNetworking(clientTransport);
            for (int index = 0; index < 10 && client.NetworkState != global::GMCore.GameplayNetworkState.Ready; index++)
            {
                client.UpdateNetwork();
                machine.UpdateNetwork();
                client.UpdateNetwork();
            }
            if (client.NetworkState != global::GMCore.GameplayNetworkState.Ready)
                throw new global::System.Exception($"Generated network module did not connect: {client.NetworkState}, {networkFailureReason}.");

            var clientActor = client.GetGameplayObject<global::Tests.OD.Actor>(first.ObjectID);
            if (clientActor.Parent is not null || clientActor.RelatedActors.Count != 0 ||
                clientActor.ActorSet.Count != 0 || clientActor.ActorByName.Count != 0)
                throw new global::System.Exception("Initial replication exposed unavailable GameplayObject references.");
            int clientListChanged = 0;
            int clientSetAdded = 0;
            int clientSetRemoved = 0;
            int clientMapAdded = 0;
            int clientMapRemoved = 0;
            int clientMapItemChanged = 0;
            int clientMapSpecificItemChanged = 0;
            int clientDrivenChanged = 0;
            var clientListHandle = clientActor.TagsBinder.Bind((object context) => clientListChanged++);
            var clientSetItemHandle = clientActor.LabelsBinder.BindItemChanged(change =>
            {
                if (change.ChangeType == global::GMCore.CollectionItemChangeType.Add) clientSetAdded++;
                if (change.ChangeType == global::GMCore.CollectionItemChangeType.Remove) clientSetRemoved++;
            });
            var clientMapItemHandle = clientActor.StatsByNameBinder.BindItemChanged(change =>
            {
                if (change.ChangeType == global::GMCore.CollectionItemChangeType.Add) clientMapAdded++;
                if (change.ChangeType == global::GMCore.CollectionItemChangeType.Remove) clientMapRemoved++;
                clientMapItemChanged++;
                if (change.Item == "remote") clientMapSpecificItemChanged++;
            });
            var clientDrivenHandle = clientActor.AfterIsAliveChanged.Bind((object context) => clientDrivenChanged++);

            first.Tags.Add("remote-list");
            first.Labels.Add("remote-set");
            first.Labels.Remove("remote-set");
            var authorityStats = first.StatsByName;
            authorityStats.Add("remote", global::Tests.OD.GeneratedResources.Stats_DefaultStats);
            authorityStats["remote"] = global::Tests.OD.GeneratedResources.Stats_AlternateStats;
            authorityStats.Remove("remote");
            first.Health = 5;
            for (int index = 0; index < 10; index++)
            {
                client.UpdateNetwork();
                machine.UpdateNetwork();
                client.UpdateNetwork();
            }

            if (clientActor.Tags.Count != 1 || clientActor.Tags.First() != "remote-list" || clientListChanged != 1)
                throw new global::System.Exception($"List full synchronization failed: count={clientActor.Tags.Count}, callbacks={clientListChanged}.");
            if (clientActor.Labels.Count != 0 || clientSetAdded != 1 || clientSetRemoved != 1)
                throw new global::System.Exception($"Set delta callbacks failed: Add={clientSetAdded}, Remove={clientSetRemoved}.");
            if (clientActor.StatsByName.Count != 0 || clientMapAdded != 1 || clientMapRemoved != 1 ||
                clientMapItemChanged != 3 || clientMapSpecificItemChanged != 3)
                throw new global::System.Exception($"Map delta callbacks failed: Add={clientMapAdded}, Remove={clientMapRemoved}, ItemChanged={clientMapItemChanged}, Specific={clientMapSpecificItemChanged}.");
            if (!clientActor.IsAlive || clientDrivenChanged != 1)
                throw new global::System.Exception($"Replicated source did not notify driven field: value={clientActor.IsAlive}, callbacks={clientDrivenChanged}.");
            client.DisableNetworking();
            machine.DisableNetworking();
            global::System.Console.WriteLine("initialized");
            """);

        var process = Process.Start(new ProcessStartInfo("dotnet", $"build \"{projectFile}\" --nologo -nodeReuse:false")
        {
            WorkingDirectory = _directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.That(process.ExitCode, Is.Zero, output + Environment.NewLine + error);

        var run = Process.Start(new ProcessStartInfo("dotnet", $"run --project \"{projectFile}\" --no-build --nologo")
        {
            WorkingDirectory = _directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        string runOutput = await run.StandardOutput.ReadToEndAsync();
        string runError = await run.StandardError.ReadToEndAsync();
        await run.WaitForExitAsync();
        Assert.Multiple(() =>
        {
            Assert.That(run.ExitCode, Is.Zero, runOutput + Environment.NewLine + runError);
            Assert.That(runOutput, Does.Contain("initialized"));
        });
    }

    [Test]
    public void GameplayMachineFunctionsAreScaffoldedAsIndependentFiles()
    {
        OdProject project = TestProject.Create();
        OdProject lockstepProject = TestProject.Create();
        lockstepProject.Runtime.NetworkMode = OdNetworkMode.Lockstep;

        OdCodeFile join = OdProjectCodeManager.GetOrCreateGameplayMachineFunctionFile(
            project, OdGameplayMachineFunction.OnPlayerJoin);
        OdCodeFile gameStart = OdProjectCodeManager.GetOrCreateGameplayMachineFunctionFile(
            lockstepProject, OdGameplayMachineFunction.OnGameStart);
        OdCodeFile disconnect = OdProjectCodeManager.GetOrCreateGameplayMachineFunctionFile(
            project, OdGameplayMachineFunction.OnPlayerDisconnected);
        OdCodeFile started = OdProjectCodeManager.GetOrCreateGameplayMachineFunctionFile(
            project, OdGameplayMachineFunction.OnAuthorityStarted);
        OdCodeFile provision = OdProjectCodeManager.GetOrCreateGameplayMachineFunctionFile(
            project, OdGameplayMachineFunction.OnProvisionPartitions);
        OdCodeFile query = OdProjectCodeManager.GetOrCreateGameplayMachineFunctionFile(
            project, OdGameplayMachineFunction.OnQueryPartitions);
        OdCodeFile resolve = OdProjectCodeManager.GetOrCreateGameplayMachineFunctionFile(
            project, OdGameplayMachineFunction.TryResolvePartition);

        Assert.Multiple(() =>
        {
            Assert.That(join.Kind, Is.EqualTo(OdCodeFileKind.GameplayMachine));
            Assert.That(join.RelativePath, Is.EqualTo("Tests/OD/GameplayMachine/OnPlayerJoin.cs"));
            Assert.That(join.Content, Does.Contain("protected override void OnPlayerJoin"));
            Assert.That(gameStart.Content, Does.Contain("protected override void OnGameStart"));
            Assert.That(disconnect.RelativePath, Is.EqualTo("Tests/OD/GameplayMachine/OnPlayerDisconnected.cs"));
            Assert.That(started.Content, Does.Contain("protected override void OnAuthorityStarted"));
            Assert.That(provision.Content, Does.Contain("protected override void OnProvisionPartitions"));
            Assert.That(query.Content, Does.Contain("protected override void OnQueryPartitions"));
            Assert.That(resolve.Content, Does.Contain("protected override bool TryResolvePartition"));
            Assert.That(join.Id, Is.Not.EqualTo(disconnect.Id));
            Assert.That(project.CodeFiles.Count(item => item.Kind == OdCodeFileKind.GameplayMachine), Is.EqualTo(6));
            Assert.That(lockstepProject.CodeFiles.Count(item => item.Kind == OdCodeFileKind.GameplayMachine), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RenamingSymbolsDoesNotChangeGeneratedPersistenceIdentity()
    {
        var project = TestProject.Create();
        var actor = project.Definitions.OfType<OdClassDefinition>().Single();
        var health = actor.Fields.Single();
        string classIdentity = $"od:{project.Ods.Single().Id:N}:class:{actor.Id:N}";
        string fieldIdentity = $"{classIdentity}:field:{health.Id:N}";

        var first = await OdCSharpGenerator.GenerateAsync(project, Path.Combine(_directory, "First"));
        actor.Name = "RenamedActor";
        health.Name = "RenamedHealth";
        var second = await OdCSharpGenerator.GenerateAsync(project, Path.Combine(_directory, "Second"));

        string firstModule = first.Files.Single(item => item.RelativePath == "Tests/OD/ODModule.g.cs").Content;
        string secondModule = second.Files.Single(item => item.RelativePath == "Tests/OD/ODModule.g.cs").Content;
        string firstNetworkSchema = firstModule.Split('\n').Single(item => item.Contains("NetworkSchemaID", StringComparison.Ordinal));
        string secondNetworkSchema = secondModule.Split('\n').Single(item => item.Contains("NetworkSchemaID", StringComparison.Ordinal));
        Assert.Multiple(() =>
        {
            Assert.That(firstModule, Does.Contain(classIdentity));
            Assert.That(firstModule, Does.Contain(fieldIdentity));
            Assert.That(secondModule, Does.Contain(classIdentity));
            Assert.That(secondModule, Does.Contain(fieldIdentity));
            Assert.That(secondNetworkSchema, Is.EqualTo(firstNetworkSchema));
        });
    }

    [Test]
    public async Task MultipleOdsGenerateIntoTheirNamespaceDirectories()
    {
        var project = TestProject.Create();
        var inventoryScope = new OdScope { Name = "Inventory", Namespace = "Tests.Inventory" };
        var inventoryData = new OdStructDefinition
        {
            Name = "InventoryData",
            Namespace = inventoryScope.Namespace,
        };
        var equip = new OdInterfaceDefinition
        {
            Name = "Equip",
            Namespace = inventoryScope.Namespace,
            Inputs =
            {
                new OdFieldDefinition
                {
                    Name = "Actor",
                    Type = new OdTypeReference
                    {
                        DefinitionId = project.Definitions.OfType<OdClassDefinition>().Single().Id,
                    },
                },
            },
        };
        project.Ods.Add(inventoryScope);
        project.Definitions.Add(inventoryData);
        project.Definitions.Add(equip);

        var result = await OdCSharpGenerator.GenerateAsync(project, Path.Combine(_directory, "MultipleOds"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, string.Join(Environment.NewLine, result.Issues));
            Assert.That(result.Files, Has.Some.Matches<OdGeneratedFile>(item => item.RelativePath == "Tests/OD/ODModule.g.cs"));
            Assert.That(result.Files, Has.Some.Matches<OdGeneratedFile>(item => item.RelativePath == "Tests/Inventory/ODModule.g.cs"));
            Assert.That(result.Files, Has.Some.Matches<OdGeneratedFile>(item => item.RelativePath == "Tests/Inventory/Interfaces/EquipParam.cs"));
            string inventoryTypes = result.Files.Single(item => item.RelativePath == "Tests/Inventory/ODTypes.g.cs").Content;
            Assert.That(inventoryTypes, Does.Contain("struct InventoryData"));
            string inventoryBehaviors = result.Files.Single(item => item.RelativePath == "Tests/Inventory/ODBehaviors.g.cs").Content;
            Assert.That(inventoryBehaviors, Does.Contain("global::Tests.OD.Actor"));
            string inventoryModule = result.Files.Single(item => item.RelativePath == "Tests/Inventory/ODModule.g.cs").Content;
            Assert.That(inventoryModule, Does.Contain("class InventoryModule"));
            Assert.That(inventoryModule, Does.Not.Contain("class Actor"));
        });
    }

    [Test]
    public void GameplayAllowsOutputsButPlayerInputDoesNot()
    {
        var project = TestProject.Create();
        var definition = project.Definitions.OfType<OdInterfaceDefinition>().Single();
        definition.IsRoutine = true;
        definition.Outputs.Add(new OdFieldDefinition
        {
            Name = "Result",
            Type = OdTypeReference.BuiltInType(OdBuiltInType.Boolean),
        });

        var gameplayRoutineIssues = OdProjectValidator.Validate(project);
        definition.IsRoutine = false;
        var gameplayIssues = OdProjectValidator.Validate(project);
        definition.InterfaceType = OdInterfaceType.PlayerInput;
        definition.Prediction = OdPredictionMode.Both;
        var playerInputIssues = OdProjectValidator.Validate(project);

        Assert.Multiple(() =>
        {
            Assert.That(gameplayRoutineIssues, Has.None.Matches<OdIssue>(issue => issue.Code is "OD0206" or "OD0207"));
            Assert.That(gameplayIssues, Has.None.Matches<OdIssue>(issue => issue.Code is "OD0206" or "OD0207"));
            Assert.That(playerInputIssues, Has.Some.Matches<OdIssue>(issue => issue.Code == "OD0206"));
        });
    }

    [Test]
    public void RuntimeGeneratesFixedTickAndPortableGameHost()
    {
        var project = TestProject.Create();
        project.Runtime.StateSyncTickMode = OdStateSyncTickMode.FixedRate;
        project.Runtime.TickIntervalMilliseconds = 25;
        OdProjectCodeManager.GetOrCreateGameplayMachineFunctionFile(
            project, OdGameplayMachineFunction.OnTick);

        OdCodeGenerationResult result = OdCSharpGenerator.GenerateFiles(project);
        string runtime = result.Files.Single(item => item.RelativePath.EndsWith("/ODRuntime.g.cs")).Content;
        string tick = result.Files.Single(item => item.RelativePath.EndsWith("/GameplayMachine/OnTick.cs")).Content;

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, string.Join(Environment.NewLine, result.Issues));
            Assert.That(runtime, Does.Contain("settings.TickMode = global::GMCore.GameplayTickMode.FixedRate"));
            Assert.That(runtime, Does.Contain("settings.TickIntervalMilliseconds = 25"));
            Assert.That(runtime, Does.Contain("GameHost : global::System.IDisposable"));
            Assert.That(runtime, Does.Contain("public string LogDirectory { get; set; } = \"logs\";"));
            Assert.That(runtime, Does.Contain("public bool UseDelayedCallbacks { get; set; } = true;"));
            Assert.That(runtime, Does.Contain("machine.SetUseDelayedCallback(options.UseDelayedCallbacks);"));
            Assert.That(runtime, Does.Contain("new global::GMCore.GameplayLogger"));
            Assert.That(runtime, Does.Contain("public void Update(double deltaSeconds)"));
            Assert.That(runtime, Does.Contain("public byte[] Save()"));
            Assert.That(tick, Does.Contain("protected override void OnTick()"));
        });
    }

    [Test]
    public void StateSyncRuntimeCanDisableAutomaticTicks()
    {
        var project = TestProject.Create();
        project.Runtime.StateSyncTickMode = OdStateSyncTickMode.None;
        project.Runtime.TickIntervalMilliseconds = 0;

        OdCodeGenerationResult result = OdCSharpGenerator.GenerateFiles(project);
        string runtime = result.Files.Single(item => item.RelativePath.EndsWith("/ODRuntime.g.cs")).Content;

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, string.Join(Environment.NewLine, result.Issues));
            Assert.That(runtime, Does.Contain("settings.TickMode = global::GMCore.GameplayTickMode.None"));
            Assert.That(result.Issues, Has.None.Matches<OdIssue>(issue => issue.Code == "OD0712"));
        });
    }

    [Test]
    public void LockstepRuntimeGeneratesOptionalEmbeddedRelayHost()
    {
        OdProject project = TestProject.Create();
        project.Runtime.NetworkMode = OdNetworkMode.Lockstep;
        project.Runtime.LockstepTickMode = OdLockstepTickMode.InputDriven;

        OdCodeGenerationResult result = OdCSharpGenerator.GenerateFiles(project);
        string runtime = result.Files.Single(item => item.RelativePath.EndsWith("/ODRuntime.g.cs")).Content;

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, string.Join(Environment.NewLine, result.Issues));
            Assert.That(runtime, Does.Contain("Mode { get; set; } = TestGameLaunchMode.LockstepClient"));
            Assert.That(runtime, Does.Contain("public bool StartRelayServer { get; set; }"));
            Assert.That(runtime, Does.Contain("public int RequestedSlot { get; set; }"));
            Assert.That(runtime, Does.Contain("lockstep.RequestedSlot = options.RequestedSlot"));
            Assert.That(runtime, Does.Contain("GameplayLockstepRelayServer? _relayServer"));
            Assert.That(runtime, Does.Contain("lockstep.TickMode = global::GMCore.GameplayLockstepTickMode.InputDriven"));
            Assert.That(runtime, Does.Contain("lockstep.InputDelayTicks = 0"));
            Assert.That(runtime, Does.Contain("relayAddress = \"127.0.0.1\""));
            Assert.That(runtime, Does.Contain("RelayAddress cannot specify a remote server"));
            Assert.That(runtime, Does.Contain("_relayServer?.Update();"));
        });
    }

    [Test]
    public void BuildOutputsMatchNetworkModeAndCommandLinesExposeHelp()
    {
        var state = TestProject.Create();
        var lockstep = TestProject.Create();
        lockstep.Runtime.NetworkMode = OdNetworkMode.Lockstep;
        lockstep.Runtime.LockstepTickMode = OdLockstepTickMode.InputDriven;

        Assert.Multiple(() =>
        {
            Assert.That(GameplayMachineBuildPublisher.OutputsFor(state.Runtime.NetworkMode),
                Is.EquivalentTo(new[] { GameplayMachineBuildOutput.StateSyncCommandLineGame,
                    GameplayMachineBuildOutput.StateSyncExportedGame }));
            Assert.That(GameplayMachineBuildPublisher.OutputsFor(lockstep.Runtime.NetworkMode),
                Does.Contain(GameplayMachineBuildOutput.LockstepRelayServer));
            Assert.That(GameplayMachineBuildPublisher.ProgramSource(state,
                    GameplayMachineBuildOutput.StateSyncCommandLineGame),
                Does.Contain("--mode <offline|host|dedicated-server>")
                    .And.Contain("--log-directory")
                    .And.Contain("GameplayLogger")
                    .And.Contain("machine.SetUseDelayedCallback(true)")
                    .And.Contain("--help"));
            Assert.That(GameplayMachineBuildPublisher.ProgramSource(lockstep,
                    GameplayMachineBuildOutput.LockstepRelayServer),
                Does.Contain("--record-directory").And.Contain("--players")
                    .And.Contain("--log-directory")
                    .And.Contain("Logger = logger")
                    .And.Contain("TickMode = GameplayLockstepTickMode.InputDriven")
                    .And.Contain("InputDelayTicks = 0"));
            Assert.That(GameplayMachineBuildPublisher.ProgramSource(lockstep,
                    GameplayMachineBuildOutput.LockstepCommandLineGame),
                Does.Contain("--start-relay")
                    .And.Contain("--requested-slot")
                    .And.Contain("--relay-listen-address")
                    .And.Contain("--relay-address cannot be used with --start-relay")
                    .And.Contain("--debug-address")
                    .And.Contain("--log-directory")
                    .And.Contain("GameplayLogger")
                    .And.Contain("machine.SetUseDelayedCallback(true)")
                    .And.Contain("TickMode = GameplayLockstepTickMode.InputDriven")
                    .And.Contain("InputDelayTicks = 0"));
        });
    }

    [Test]
    public void InactiveTickModeDoesNotInvalidateProject()
    {
        OdProject project = TestProject.Create();
        project.Runtime.LockstepTickMode = OdLockstepTickMode.InputDriven;

        IReadOnlyList<OdIssue> stateSyncIssues = OdProjectValidator.Validate(project);
        project.Runtime.NetworkMode = OdNetworkMode.Lockstep;
        IReadOnlyList<OdIssue> lockstepIssues = OdProjectValidator.Validate(project);

        Assert.That(stateSyncIssues.Concat(lockstepIssues),
            Has.None.Matches<OdIssue>(issue => issue.Code == "OD0714"));
    }

    [Test]
    public async Task CommandLineBuildReportsPublishProgress()
    {
        OdProject project = TestProject.Create();
        string output = Path.Combine(_directory, "command-line-build");
        var progressMessages = new List<string>();

        GameplayMachineBuildResult result = await GameplayMachineBuildPublisher.PublishAsync(
            project,
            GameplayMachineBuildOutput.StateSyncCommandLineGame,
            output,
            AppContext.BaseDirectory,
            progress: new InlineProgress<string>(progressMessages.Add));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, string.Join(Environment.NewLine, result.Issues));
            Assert.That(progressMessages, Has.Some.Contains("Generating GameplayMachine source"));
            Assert.That(progressMessages, Has.Some.StartsWith("> dotnet publish"));
            Assert.That(progressMessages, Has.Some.Contains("Published"));
        });
    }

    [Test]
    public async Task ExportedGameBuildSupportsCSharpUnityAndGodotPlatforms()
    {
        OdProject project = TestProject.Create();
        IOdExportTarget[] targets =
        [
            new PlainCSharpExportTarget(),
            new UnityExportTarget(),
            new GodotExportTarget(),
        ];

        foreach (IOdExportTarget target in targets)
        {
            string output = Path.Combine(_directory, target.Id);
            var progressMessages = new List<string>();
            GameplayMachineBuildResult result = await GameplayMachineBuildPublisher.PublishAsync(
                project,
                GameplayMachineBuildOutput.StateSyncExportedGame,
                output,
                AppContext.BaseDirectory,
                target,
                default,
                new InlineProgress<string>(progressMessages.Add));

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True,
                    $"{target.DisplayName}: {string.Join(Environment.NewLine, result.Issues)}");
                Assert.That(File.Exists(Path.Combine(output, ".gameplaymachine-export.json")), Is.True);
                Assert.That(File.ReadAllText(Path.Combine(output, ".gameplaymachine-export.json")),
                    Does.Contain($"\"Target\": \"{target.Id}\""));
                Assert.That(File.ReadAllText(Path.Combine(output, "GAMEPLAY_MACHINE_INTEGRATION.md")),
                    Does.Contain($"Platform: {target.DisplayName}"));
                Assert.That(progressMessages,
                    Has.Some.Contains("Validating TestGame")
                        .And.Some.Contains("Building OD package")
                        .And.Some.Contains($"Exporting {target.DisplayName}")
                        .And.Some.Contains("Exported"));
                if (target is GodotExportTarget)
                    Assert.That(Directory.EnumerateFiles(Path.Combine(output, "Godot"), "*Runner.cs"),
                        Is.Not.Empty);
            });
        }
    }

    [Test]
    public async Task GodotExportIntegratesSharedRuntimeIntoProject()
    {
        OdProject project = TestProject.Create();
        string godotRoot = Path.Combine(_directory, "GodotGame");
        string output = Path.Combine(godotRoot, "gameplaymachine");
        Directory.CreateDirectory(godotRoot);
        await File.WriteAllTextAsync(Path.Combine(godotRoot, "project.godot"),
            "[application]\n\n[dotnet]\n\nproject/assembly_name=\"Presentation\"\n");
        string runtimeProject = Path.Combine(FindRepositorySibling("gameplaymachine_core"), "main",
            "GameplayMachineGodot", "GameplayMachineGodot.csproj");
        var target = new GodotExportTarget();
        var progressMessages = new List<string>();

        GameplayMachineBuildResult result = await GameplayMachineBuildPublisher.PublishAsync(
            project,
            GameplayMachineBuildOutput.StateSyncExportedGame,
            output,
            AppContext.BaseDirectory,
            target,
            progress: new InlineProgress<string>(progressMessages.Add));

        string projectFile = Path.Combine(godotRoot, "Presentation.csproj");
        string solutionFile = Path.Combine(godotRoot, "Presentation.sln");
        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True,
                string.Join(Environment.NewLine, result.Issues));
            Assert.That(File.ReadAllText(projectFile),
                Does.Contain("GameplayMachineGodot.csproj").And.Contain("ProjectReference"));
            Assert.That(File.Exists(solutionFile), Is.True);
            Assert.That(File.ReadAllText(solutionFile),
                Does.Contain("Presentation.csproj").And.Contain("ExportRelease|Any CPU"));
            Assert.That(File.Exists(runtimeProject), Is.True);
            Assert.That(progressMessages,
                Has.Some.StartsWith("> dotnet build").And.Some.Contains(
                    "Godot C# project built successfully"));
        });

        const string customSolution = "user-owned solution";
        await File.WriteAllTextAsync(solutionFile, customSolution);
        await target.PostProcessAsync(new OdExportModel(project),
            new OdExportRequest(string.Empty, output, new OdExportProfile { Target = target.Id }));
        Assert.That(await File.ReadAllTextAsync(solutionFile), Is.EqualTo(customSolution));
    }

    [Test]
    public void GodotExportOwnsAllExportedGameplaySource()
    {
        OdProject project = TestProject.Create();
        var target = new GodotExportTarget();

        OdExportPlan plan = target.BuildPlan(new OdExportModel(project),
            new OdExportProfile { Target = target.Id });

        Assert.That(plan.Artifacts, Is.Not.Empty.And.All.Matches<OdExportArtifact>(artifact =>
            artifact.OverwriteExisting));
    }

    [Test]
    public void GodotRunnerUsesCompositionInsteadOfCrossAssemblyNodeInheritance()
    {
        OdProject project = TestProject.Create();
        var target = new GodotExportTarget();

        OdExportArtifact runner = target.BuildPlan(new OdExportModel(project),
                new OdExportProfile { Target = target.Id })
            .Artifacts.Single(artifact => artifact.RelativePath.EndsWith("Runner.cs", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(runner.Content, Does.Contain(": global::Godot.Node"));
            Assert.That(runner.Content, Does.Contain("GameplayMachineRunnerCore"));
            Assert.That(runner.Content, Does.Contain("UseDelayedCallbacks { get; set; } = true;"));
            Assert.That(runner.Content, Does.Contain("_core.UseDelayedCallbacks = UseDelayedCallbacks;"));
            Assert.That(runner.Content, Does.Not.Contain(
                "public partial class TestGameRunner : global::ODStudio.Godot.Runtime.GameplayMachineRunner"));
            Assert.That(runner.Content, Does.Not.Contain("ExportGroup(\"Lockstep\")"));
            Assert.That(runner.Content, Does.Not.Contain("LockstepPlayerCount"));
            Assert.That(runner.Content, Does.Not.Contain("RequestedSlot"));
            Assert.That(runner.Content, Does.Not.Contain("LockstepRecordingDirectory"));
            Assert.That(runner.Content, Does.Not.Contain("CreateLockstepOptions"));
        });
    }

    [Test]
    public void GodotLockstepRunnerIncludesLockstepConfiguration()
    {
        OdProject project = TestProject.Create();
        project.Runtime.NetworkMode = OdNetworkMode.Lockstep;
        project.Runtime.LockstepTickMode = OdLockstepTickMode.InputDriven;
        var target = new GodotExportTarget();

        OdExportArtifact runner = target.BuildPlan(new OdExportModel(project),
                new OdExportProfile { Target = target.Id })
            .Artifacts.Single(artifact => artifact.RelativePath.EndsWith("Runner.cs", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(runner.Content, Does.Contain("ExportGroup(\"Lockstep\")"));
            Assert.That(runner.Content, Does.Contain("LockstepPlayerCount"));
            Assert.That(runner.Content, Does.Contain("RequestedSlot"));
            Assert.That(runner.Content, Does.Contain("LockstepRecordingDirectory"));
            Assert.That(runner.Content, Does.Contain("CreateLockstepOptions"));
            Assert.That(runner.Content, Does.Contain("GameplayLockstepTickMode.InputDriven"));
        });
    }

    private static string FindGameplayMachineCoreProject()
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current is not null && !string.Equals(current.Name, "od-manipulator", StringComparison.OrdinalIgnoreCase))
            current = current.Parent;
        if (current?.Parent is null)
            throw new DirectoryNotFoundException("Could not locate the repository root from the test directory.");
        string result = Path.Combine(current.Parent.FullName, "gameplaymachine_core", "main", "GameplayMachineCore", "GameplayMachineCore.csproj");
        if (!File.Exists(result))
            throw new FileNotFoundException("The local GameplayMachine Core project is required for the generated-code compile test.", result);
        return result;
    }

    private static string FindRepositorySibling(string name)
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current is not null && !string.Equals(current.Name, "od-manipulator", StringComparison.OrdinalIgnoreCase))
            current = current.Parent;
        if (current?.Parent is null)
            throw new DirectoryNotFoundException("Could not locate the repository root from the test directory.");
        return Path.Combine(current.Parent.FullName, name);
    }

    private static void AddExtendedRuntimeTypes(OdProject project)
    {
        var actor = project.Definitions.OfType<OdClassDefinition>().Single();
        var damage = project.Definitions.OfType<OdInterfaceDefinition>().Single();
        damage.Outputs.Add(new OdFieldDefinition { Name = "Applied", Type = OdTypeReference.BuiltInType(OdBuiltInType.Boolean) });
        var mood = new OdEnumDefinition
        {
            Name = "Mood",
            Namespace = "Tests.OD",
            Members =
            {
                new OdEnumMember { Name = "Idle", Value = 0 },
                new OdEnumMember { Name = "Busy", Value = 1 },
            },
        };
        var stats = new OdResourceDefinition
        {
            Name = "Stats",
            Namespace = "Tests.OD",
            Fields =
            {
                new OdFieldDefinition { Name = "Score", Type = OdTypeReference.BuiltInType(OdBuiltInType.Int64) },
            },
        };
        var actorReference = new OdSwitchStructDefinition
        {
            Name = "ActorReference",
            Namespace = "Tests.OD",
            Fields =
            {
                new OdFieldDefinition { Name = "Actor", Type = new OdTypeReference { DefinitionId = actor.Id } },
                new OdFieldDefinition
                {
                    Name = "Tags",
                    Type = new OdTypeReference
                    {
                        BuiltIn = OdBuiltInType.String,
                        Container = OdContainerKind.List,
                    },
                },
            },
        };
        var money = new OdStructDefinition
        {
            Name = "Money",
            Namespace = "Tests.OD",
            IsStructOf = OdTypeReference.BuiltInType(OdBuiltInType.Int32),
        };
        var coordinate = new OdStructDefinition
        {
            Name = "Coordinate",
            Namespace = "Tests.OD",
            Fields =
            {
                new OdFieldDefinition { Name = "X", Type = OdTypeReference.BuiltInType(OdBuiltInType.Int32) },
                new OdFieldDefinition { Name = "Y", Type = OdTypeReference.BuiltInType(OdBuiltInType.Int32) },
            },
        };
        actor.Fields.AddRange(new[]
        {
            new OdFieldDefinition
            {
                Name = "IsAlive",
                Type = OdTypeReference.BuiltInType(OdBuiltInType.Boolean),
                Mode = OdFieldMode.Driven,
                Replication = OdFieldReplicationMode.None,
                DrivenByFields = { actor.Fields.Single(item => item.Name == "Health").Id },
            },
            new OdFieldDefinition { Name = "Enabled", Type = OdTypeReference.BuiltInType(OdBuiltInType.Boolean) },
            new OdFieldDefinition { Name = "Ratio", Type = OdTypeReference.BuiltInType(OdBuiltInType.Single) },
            new OdFieldDefinition { Name = "Created", Type = OdTypeReference.BuiltInType(OdBuiltInType.DateTime) },
            new OdFieldDefinition { Name = "OptionalCount", Type = new OdTypeReference { BuiltIn = OdBuiltInType.Int32, Nullable = true } },
            new OdFieldDefinition { Name = "LongValue", Type = OdTypeReference.BuiltInType(OdBuiltInType.Int64) },
            new OdFieldDefinition
            {
                Name = "Controller",
                Type = new OdTypeReference { DefinitionId = OdNetworkPackage.PlayerControllerClassId },
                Replication = OdFieldReplicationMode.AllClients,
            },
            new OdFieldDefinition { Name = "Mood", Type = new OdTypeReference { DefinitionId = mood.Id }, DefaultValue = JsonValue.Create(1L) },
            new OdFieldDefinition { Name = "Stats", Type = new OdTypeReference { DefinitionId = stats.Id } },
            new OdFieldDefinition { Name = "Reference", Type = new OdTypeReference { DefinitionId = actorReference.Id } },
            new OdFieldDefinition { Name = "Balance", Type = new OdTypeReference { DefinitionId = money.Id } },
            new OdFieldDefinition
            {
                Name = "Parent",
                Type = new OdTypeReference { DefinitionId = actor.Id, Nullable = true },
                Replication = OdFieldReplicationMode.AllClients,
            },
            new OdFieldDefinition
            {
                Name = "RelatedActors",
                Type = new OdTypeReference { DefinitionId = actor.Id, Container = OdContainerKind.List },
                Replication = OdFieldReplicationMode.AllClients,
            },
            new OdFieldDefinition
            {
                Name = "ActorSet",
                Type = new OdTypeReference { DefinitionId = actor.Id, Container = OdContainerKind.Set },
                Replication = OdFieldReplicationMode.AllClients,
            },
            new OdFieldDefinition
            {
                Name = "ActorByName",
                Type = new OdTypeReference
                {
                    DefinitionId = actor.Id,
                    Container = OdContainerKind.Dictionary,
                    DictionaryKeyBuiltIn = OdBuiltInType.String,
                },
                Replication = OdFieldReplicationMode.AllClients,
            },
            new OdFieldDefinition
            {
                Name = "ActorByActor",
                Type = new OdTypeReference
                {
                    DefinitionId = actor.Id,
                    Container = OdContainerKind.Dictionary,
                    DictionaryKeyBuiltIn = OdBuiltInType.None,
                    DictionaryKeyDefinitionId = actor.Id,
                },
                Replication = OdFieldReplicationMode.AllClients,
            },
            new OdFieldDefinition
            {
                Name = "ValueByCoordinate",
                Type = new OdTypeReference
                {
                    BuiltIn = OdBuiltInType.Int32,
                    Container = OdContainerKind.Dictionary,
                    DictionaryKeyBuiltIn = OdBuiltInType.None,
                    DictionaryKeyDefinitionId = coordinate.Id,
                },
                Replication = OdFieldReplicationMode.AllClients,
            },
            new OdFieldDefinition
            {
                Name = "ValueByReference",
                Type = new OdTypeReference
                {
                    BuiltIn = OdBuiltInType.Int32,
                    Container = OdContainerKind.Dictionary,
                    DictionaryKeyBuiltIn = OdBuiltInType.None,
                    DictionaryKeyDefinitionId = actorReference.Id,
                },
                Replication = OdFieldReplicationMode.AllClients,
            },
            new OdFieldDefinition { Name = "Tags", Type = new OdTypeReference { BuiltIn = OdBuiltInType.String, Container = OdContainerKind.List }, Replication = OdFieldReplicationMode.AllClients },
            new OdFieldDefinition { Name = "Labels", Type = new OdTypeReference { BuiltIn = OdBuiltInType.String, Container = OdContainerKind.Set }, Replication = OdFieldReplicationMode.AllClients },
            new OdFieldDefinition
            {
                Name = "StatsByName",
                Type = new OdTypeReference
                {
                    DefinitionId = stats.Id,
                    Container = OdContainerKind.Dictionary,
                    DictionaryKeyBuiltIn = OdBuiltInType.String,
                },
                Replication = OdFieldReplicationMode.AllClients,
            },
        });
        project.Definitions.Add(mood);
        project.Definitions.Add(stats);
        project.Definitions.Add(actorReference);
        project.Definitions.Add(money);
        project.Definitions.Add(coordinate);
        var statsRecord = new OdDataRecord
        {
            Name = "DefaultStats",
            Values = { [stats.Fields.Single(item => item.Name == "Score").Id] = JsonValue.Create(42L) },
        };
        var alternateStatsRecord = new OdDataRecord
        {
            Name = "AlternateStats",
            Values = { [stats.Fields.Single(item => item.Name == "Score").Id] = JsonValue.Create(4L) },
        };
        project.DataSets.Add(new OdDataSet
        {
            Name = "Stats",
            DefinitionId = stats.Id,
            Records = { statsRecord, alternateStatsRecord },
        });
        var named = new OdClassDefinition
        {
            Name = "Named",
            Namespace = "Tests.OD",
            Replication = OdObjectReplicationMode.None,
            Fields = { new OdFieldDefinition { Name = "Alias", Type = OdTypeReference.BuiltInType(OdBuiltInType.String) } },
        };
        project.Definitions.Add(named);
        project.Definitions.Add(new OdClassDefinition
        {
            Name = "CompositeActor",
            Namespace = "Tests.OD",
            IsClasses = { actor.Id, named.Id },
            Fields = { new OdFieldDefinition { Name = "Title", Type = OdTypeReference.BuiltInType(OdBuiltInType.String) } },
        });
        project.Definitions.Add(new OdEventDefinition
        {
            Name = "ActorChanged",
            Namespace = "Tests.OD",
            Multicast = true,
            Fields =
            {
                new OdFieldDefinition
                {
                    Name = "Actor",
                    Type = new OdTypeReference { DefinitionId = actor.Id },
                },
            },
        });
        project.Definitions.Add(new OdInterfaceDefinition
        {
            Name = "NotifyActor",
            Namespace = "Tests.OD",
            Inputs =
            {
                new OdFieldDefinition
                {
                    Name = "Actor",
                    Type = new OdTypeReference { DefinitionId = actor.Id },
                    NotNull = true,
                },
            },
            Outputs =
            {
                new OdFieldDefinition
                {
                    Name = "Delivered",
                    Type = OdTypeReference.BuiltInType(OdBuiltInType.Boolean),
                },
            },
        });
        project.Definitions.Add(new OdInterfaceDefinition
        {
            Name = "PredictActor",
            Namespace = "Tests.OD",
            InterfaceType = OdInterfaceType.PlayerInput,
            Prediction = OdPredictionMode.Both,
            Inputs = { new OdFieldDefinition { Name = "Actor", Type = new OdTypeReference { DefinitionId = actor.Id } } },
        });
        project.Definitions.Add(new OdInterfaceDefinition
        {
            Name = "UpdateActor",
            Namespace = "Tests.OD",
            IsRoutine = true,
            Inputs = { new OdFieldDefinition { Name = "Actor", Type = new OdTypeReference { DefinitionId = actor.Id } } },
        });
        var record = project.DataSets.Single(item => item.Name == "Actors").Records.Single();
        record.Values[actor.Fields.Single(item => item.Name == "Stats").Id] = JsonValue.Create(statsRecord.Id.ToString("D"));
        record.Values[actor.Fields.Single(item => item.Name == "Tags").Id] = new JsonArray("one", "two");
        record.Values[actor.Fields.Single(item => item.Name == "StatsByName").Id] = new JsonObject
        {
            ["primary"] = statsRecord.Id.ToString("D"),
        };
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
