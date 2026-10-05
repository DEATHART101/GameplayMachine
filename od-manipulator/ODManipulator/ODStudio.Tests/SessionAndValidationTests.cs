using ODStudio.Model;

namespace ODStudio.Tests;

public sealed class SessionAndValidationTests
{
    [Test]
    public void LockstepRejectsStateSyncOnlyDefinitionsAndUnorderableCollections()
    {
        OdProject project = TestProject.Create();
        project.Runtime.NetworkMode = OdNetworkMode.Lockstep;
        OdClassDefinition root = project.Definitions.OfType<OdClassDefinition>().Single();
        root.IsClasses.Add(OdNetworkPackage.ActorClassId);
        root.Fields.Add(new OdFieldDefinition
        {
            Name = "InvalidSet",
            Type = new OdTypeReference
            {
                DefinitionId = Guid.NewGuid(),
                Container = OdContainerKind.Set,
            },
        });
        var input = new OdInterfaceDefinition
        {
            Name = "PlayCard",
            Namespace = project.DefaultNamespace,
            InterfaceType = OdInterfaceType.PlayerInput,
            Prediction = OdPredictionMode.Both,
            IsRoutine = true,
        };
        project.Definitions.Add(input);
        var multicast = new OdEventDefinition
        {
            Name = "CardPlayed",
            Namespace = project.DefaultNamespace,
            Multicast = true,
        };
        project.Definitions.Add(multicast);

        IReadOnlyList<OdIssue> issues = OdProjectValidator.Validate(project);

        Assert.Multiple(() =>
        {
            Assert.That(issues.Any(issue => issue.Code == "OD0260" && issue.TargetId == input.Id), Is.True);
            Assert.That(issues.Any(issue => issue.Code == "OD0261" && issue.TargetId == input.Id), Is.True);
            Assert.That(issues.Any(issue => issue.Code == "OD0262" && issue.TargetId == multicast.Id), Is.True);
            Assert.That(issues.Any(issue => issue.Code == "OD0263" && issue.TargetId == root.Id), Is.True);
            Assert.That(issues.Any(issue => issue.Code == "OD0264"), Is.True);
        });
    }

    [Test]
    public void LockstepAllowsOrderableStructsAsSetValuesAndMapKeys()
    {
        OdProject project = TestProject.Create();
        project.Runtime.NetworkMode = OdNetworkMode.Lockstep;
        OdClassDefinition root = project.Definitions.OfType<OdClassDefinition>().Single();
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
        var positions = new OdFieldDefinition
        {
            Name = "Positions",
            Type = new OdTypeReference
            {
                DefinitionId = coordinate.Id,
                Container = OdContainerKind.Set,
            },
        };
        var values = new OdFieldDefinition
        {
            Name = "Values",
            Type = new OdTypeReference
            {
                BuiltIn = OdBuiltInType.Int32,
                Container = OdContainerKind.Dictionary,
                DictionaryKeyBuiltIn = OdBuiltInType.None,
                DictionaryKeyDefinitionId = coordinate.Id,
            },
        };
        root.Fields.AddRange(new[] { positions, values });

        IReadOnlyList<OdIssue> issues = OdProjectValidator.Validate(project);

        Assert.That(issues, Has.None.Matches<OdIssue>(issue =>
            issue.Code == "OD0264" && (issue.TargetId == positions.Id || issue.TargetId == values.Id)));
    }

    [Test]
    public void NewNetworkDefinitionsUseEditorDefaults()
    {
        var definition = new OdClassDefinition();
        var field = new OdFieldDefinition();
        var contract = new OdInterfaceDefinition();

        Assert.Multiple(() =>
        {
            Assert.That(definition.Replication, Is.EqualTo(OdObjectReplicationMode.AllClients));
            Assert.That(definition.Relevancy, Is.EqualTo(OdObjectRelevancyMode.Always));
            Assert.That(definition.Partition, Is.EqualTo(OdObjectPartitionMode.Manual));
            Assert.That(field.Replication, Is.EqualTo(OdFieldReplicationMode.AllClients));
            Assert.That(contract.InterfaceType, Is.EqualTo(OdInterfaceType.Gameplay));
            Assert.That(contract.Prediction, Is.EqualTo(OdPredictionMode.None));
            Assert.That(Enum.GetValues<OdObjectReplicationMode>(), Is.EqualTo(new[]
            {
                OdObjectReplicationMode.None,
                OdObjectReplicationMode.OwnerOnly,
                OdObjectReplicationMode.AllClients,
            }));
            Assert.That(Enum.GetValues<OdFieldReplicationMode>(), Is.EqualTo(new[]
            {
                OdFieldReplicationMode.None,
                OdFieldReplicationMode.OwnerOnly,
                OdFieldReplicationMode.AllClients,
            }));
            Assert.That(Enum.GetValues<OdObjectRelevancyMode>(), Is.EqualTo(new[]
            {
                OdObjectRelevancyMode.Always,
                OdObjectRelevancyMode.Spatial,
            }));
        });
    }

    [Test]
    public void GameplayInterfacesCannotUsePrediction()
    {
        OdProject project = TestProject.Create();
        OdInterfaceDefinition definition = project.Definitions.OfType<OdInterfaceDefinition>().Single();
        definition.InterfaceType = OdInterfaceType.Gameplay;
        definition.Prediction = OdPredictionMode.Both;

        Assert.That(OdProjectValidator.Validate(project),
            Has.Some.Matches<OdIssue>(issue => issue.Code == "OD0213" && issue.TargetId == definition.Id));
    }

    [Test]
    public void SpatialModesRequireActorInheritance()
    {
        OdProject project = TestProject.Create();
        OdClassDefinition definition = project.Definitions.OfType<OdClassDefinition>().Single();
        definition.Relevancy = OdObjectRelevancyMode.Spatial;
        definition.Partition = OdObjectPartitionMode.Spatial;

        IReadOnlyList<OdIssue> issues = OdProjectValidator.Validate(project);

        Assert.That(issues.Any(issue => issue.Code == "OD0243" && issue.TargetId == definition.Id), Is.True);

        definition.IsClasses.Add(OdNetworkPackage.ActorClassId);
        Assert.That(OdProjectValidator.Validate(project),
            Has.None.Matches<OdIssue>(issue => issue.Code == "OD0243" && issue.TargetId == definition.Id));
    }

    [Test]
    public void RenameKeepsPermanentIdAndSupportsUndoRedo()
    {
        var project = TestProject.Create();
        var classId = project.Definitions[0].Id;
        var session = new OdProjectSession(project);

        session.Execute(OdChangeKind.Rename, classId, nameof(OdClassDefinition), "Rename Actor to Unit",
            value => value.Definitions.Single(item => item.Id == classId).Name = "Unit",
            new Dictionary<string, string?> { ["oldName"] = "Actor", ["newName"] = "Unit" });

        Assert.That(session.Project.Definitions[0].Id, Is.EqualTo(classId));
        Assert.That(session.Project.Definitions[0].Name, Is.EqualTo("Unit"));
        Assert.That(session.Project.History.Last().Kind, Is.EqualTo(OdChangeKind.Rename));
        Assert.That(session.Undo(), Is.True);
        Assert.That(session.Project.Definitions[0].Name, Is.EqualTo("Actor"));
        Assert.That(session.Redo(), Is.True);
        Assert.That(session.Project.Definitions[0].Name, Is.EqualTo("Unit"));
        Assert.That(session.Project.Definitions[0].Id, Is.EqualTo(classId));
    }

    [Test]
    public void ValidatorFindsBrokenTypeReferences()
    {
        var project = TestProject.Create();
        ((OdClassDefinition)project.Definitions[0]).Fields[0].Type = new OdTypeReference { DefinitionId = Guid.NewGuid() };

        var issues = OdProjectValidator.Validate(project);

        Assert.That(issues.Any(item => item.Code == "OD0211"), Is.True);
    }

    [Test]
    public void ResourceFieldsRejectLiveClassesAndAllowClassResources()
    {
        var project = TestProject.Create();
        var actor = project.Definitions.OfType<OdClassDefinition>().Single();
        var directClass = new OdFieldDefinition
        {
            Name = "DirectClass",
            Type = new OdTypeReference { DefinitionId = actor.Id },
        };
        var playerController = new OdFieldDefinition
        {
            Name = "Controller",
            Type = new OdTypeReference { DefinitionId = OdNetworkPackage.PlayerControllerClassId },
        };
        var pawn = new OdFieldDefinition
        {
            Name = "Pawn",
            Type = new OdTypeReference { DefinitionId = OdNetworkPackage.PawnClassId },
        };
        var liveClassKey = new OdFieldDefinition
        {
            Name = "ByActor",
            Type = new OdTypeReference
            {
                BuiltIn = OdBuiltInType.Int32,
                Container = OdContainerKind.Dictionary,
                DictionaryKeyBuiltIn = OdBuiltInType.None,
                DictionaryKeyDefinitionId = actor.Id,
            },
        };
        var classResource = new OdFieldDefinition
        {
            Name = "ActorResource",
            Type = new OdTypeReference { DefinitionId = actor.Id, IsResourceClass = true },
        };
        var resource = new OdResourceDefinition
        {
            Name = "StaticConfig",
            Namespace = project.DefaultNamespace,
            Fields = { directClass, playerController, pawn, liveClassKey, classResource },
        };
        project.Definitions.Add(resource);

        HashSet<Guid> rejected = OdProjectValidator.Validate(project)
            .Where(issue => issue.Code == "OD0238" && issue.TargetId.HasValue)
            .Select(issue => issue.TargetId!.Value)
            .ToHashSet();

        Assert.That(rejected, Is.EquivalentTo(new[]
        {
            directClass.Id,
            playerController.Id,
            pawn.Id,
            liveClassKey.Id,
        }));
    }

    [Test]
    public void InheritedPawnFieldsParticipateInNormalFieldConflictValidation()
    {
        var project = TestProject.Create();
        var pawn = project.Definitions.OfType<OdClassDefinition>().Single();
        pawn.IsClasses.Add(OdNetworkPackage.PawnClassId);
        pawn.Fields.Add(new OdFieldDefinition { Name = "PlayerController" });

        Assert.That(OdProjectValidator.Validate(project),
            Has.Some.Matches<OdIssue>(issue => issue.Code == "OD0208" && issue.TargetId == pawn.Id));
    }

    [Test]
    public void PlayerControllerInterfaceInputSupportsNotNull()
    {
        var project = TestProject.Create();
        var definition = project.Definitions.OfType<OdInterfaceDefinition>().Single();
        definition.Inputs.Add(new OdFieldDefinition
        {
            Name = "Controller",
            Type = new OdTypeReference { DefinitionId = OdNetworkPackage.PlayerControllerClassId },
            NotNull = true,
        });

        Assert.That(OdProjectValidator.Validate(project),
            Has.None.Matches<OdIssue>(issue => issue.Code == "OD0227" && issue.TargetId == definition.Inputs.Last().Id));
    }

    [Test]
    public void ValidatorAllowsEveryOdDataTypeAsAMapKey()
    {
        var project = TestProject.Create();
        var actor = project.Definitions.OfType<OdClassDefinition>().Single();
        var structure = new OdStructDefinition
        {
            Name = "Coordinate",
            Namespace = project.DefaultNamespace,
            Fields = { new OdFieldDefinition { Name = "X" } },
        };
        var switchStructure = new OdSwitchStructDefinition
        {
            Name = "Target",
            Namespace = project.DefaultNamespace,
            Fields = { new OdFieldDefinition { Name = "Actor", Type = new OdTypeReference { DefinitionId = actor.Id } } },
        };
        var enumeration = new OdEnumDefinition
        {
            Name = "Direction",
            Namespace = project.DefaultNamespace,
            Members = { new OdEnumMember { Name = "North" } },
        };
        var resource = new OdResourceDefinition { Name = "ActorConfig", Namespace = project.DefaultNamespace };
        project.Definitions.AddRange(new OdDefinition[] { structure, switchStructure, enumeration, resource });

        foreach (OdDefinition keyDefinition in new OdDefinition[] { actor, structure, switchStructure, enumeration, resource })
        {
            actor.Fields.Add(new OdFieldDefinition
            {
                Name = $"By{keyDefinition.Name}",
                Type = new OdTypeReference
                {
                    BuiltIn = OdBuiltInType.Int32,
                    Container = OdContainerKind.Dictionary,
                    DictionaryKeyBuiltIn = OdBuiltInType.None,
                    DictionaryKeyDefinitionId = keyDefinition.Id,
                },
            });
        }

        var issues = OdProjectValidator.Validate(project);

        Assert.That(issues, Has.None.Matches<OdIssue>(issue => issue.Code == "OD0213"));
    }

    [Test]
    public void ValidatorRejectsEmptySwitchStructsAndReservedSubtypeNames()
    {
        var project = TestProject.Create();
        var definition = new OdSwitchStructDefinition
        {
            Name = "Choice",
            Namespace = project.DefaultNamespace,
        };
        project.Definitions.Add(definition);

        Assert.That(OdProjectValidator.Validate(project),
            Has.Some.Matches<OdIssue>(issue => issue.Code == "OD0218"));

        definition.Fields.Add(new OdFieldDefinition { Name = "Value" });
        var issues = OdProjectValidator.Validate(project);
        Assert.Multiple(() =>
        {
            Assert.That(issues, Has.None.Matches<OdIssue>(issue => issue.Code == "OD0218"));
            Assert.That(issues, Has.Some.Matches<OdIssue>(issue => issue.Code == "OD0219"));
        });
    }

    [Test]
    public void ValidatorSupportsMultipleIsClassesAndRejectsCycles()
    {
        var project = TestProject.Create();
        var actor = project.Definitions.OfType<OdClassDefinition>().Single();
        var firstBase = new OdClassDefinition { Name = "FirstBase", Namespace = project.DefaultNamespace };
        var secondBase = new OdClassDefinition { Name = "SecondBase", Namespace = project.DefaultNamespace };
        actor.IsClasses.AddRange(new[] { firstBase.Id, secondBase.Id });
        project.Definitions.Add(firstBase);
        project.Definitions.Add(secondBase);

        Assert.That(OdProjectValidator.Validate(project), Has.None.Matches<OdIssue>(issue => issue.Code is "OD0202" or "OD0203"));

        firstBase.IsClasses.Add(actor.Id);
        Assert.That(OdProjectValidator.Validate(project), Has.Some.Matches<OdIssue>(issue => issue.Code == "OD0203"));
    }

    [Test]
    public void ValidatorRejectsDiamondInheritance()
    {
        var project = TestProject.Create();
        var derived = project.Definitions.OfType<OdClassDefinition>().Single();
        var sharedBase = new OdClassDefinition { Name = "SharedBase", Namespace = project.DefaultNamespace };
        var leftBase = new OdClassDefinition
        {
            Name = "LeftBase",
            Namespace = project.DefaultNamespace,
            IsClasses = { sharedBase.Id },
        };
        var rightBase = new OdClassDefinition
        {
            Name = "RightBase",
            Namespace = project.DefaultNamespace,
            IsClasses = { sharedBase.Id },
        };
        derived.IsClasses.AddRange(new[] { leftBase.Id, rightBase.Id });
        project.Definitions.AddRange(new OdDefinition[] { sharedBase, leftBase, rightBase });

        var issues = OdProjectValidator.Validate(project);

        Assert.That(issues, Has.Some.Matches<OdIssue>(issue =>
            issue.Code == "OD0217" &&
            issue.Severity == OdIssueSeverity.Error &&
            issue.TargetId == derived.Id &&
            issue.Message.Contains(sharedBase.Name, StringComparison.Ordinal)));
    }

    [Test]
    public void ValidatorRejectsDiamondInheritanceThroughBuiltInPawn()
    {
        var project = TestProject.Create();
        var derived = project.Definitions.OfType<OdClassDefinition>().Single();
        var leftBase = new OdClassDefinition
        {
            Name = "LeftPawn",
            Namespace = project.DefaultNamespace,
            IsClasses = { OdNetworkPackage.PawnClassId },
        };
        var rightBase = new OdClassDefinition
        {
            Name = "RightPawn",
            Namespace = project.DefaultNamespace,
            IsClasses = { OdNetworkPackage.PawnClassId },
        };
        derived.IsClasses.AddRange(new[] { leftBase.Id, rightBase.Id });
        project.Definitions.AddRange(new OdDefinition[] { leftBase, rightBase });

        Assert.That(OdProjectValidator.Validate(project), Has.Some.Matches<OdIssue>(issue =>
            issue.Code == "OD0217" && issue.TargetId == derived.Id &&
            issue.Message.Contains("Pawn", StringComparison.Ordinal)));
    }

    [Test]
    public void ValidatorRejectsInheritanceFromClassesThatPreventIt()
    {
        var project = TestProject.Create();
        var derived = project.Definitions.OfType<OdClassDefinition>().Single();
        derived.IsClasses.Add(OdNetworkPackage.PlayerControllerClassId);

        Assert.That(OdProjectValidator.Validate(project), Has.Some.Matches<OdIssue>(issue =>
            issue.Code == "OD0240" && issue.TargetId == derived.Id &&
            issue.Message.Contains("PlayerController", StringComparison.Ordinal)));
    }

    [Test]
    public void NetworkPackageClassesUseNormalInheritanceAndFields()
    {
        var project = TestProject.Create();
        var derived = project.Definitions.OfType<OdClassDefinition>().Single();
        derived.IsClasses.Add(OdNetworkPackage.PawnClassId);
        OdProject network = OdNetworkPackage.CreateProject();

        IReadOnlyList<OdClassDefinition> inherited = project.GetInheritedClasses(derived, network.Definitions);

        Assert.Multiple(() =>
        {
            Assert.That(inherited.Select(item => item.Name), Is.EqualTo(new[] { "Pawn", "Actor" }));
            Assert.That(inherited.Single(item => item.Name == "Pawn").Fields.Select(item => item.Name),
                Is.EqualTo(new[] { "PlayerController" }));
            Assert.That(inherited.Single(item => item.Name == "Actor").Fields.Select(item => item.Name),
                Is.EqualTo(new[] { "Transform" }));
            OdClassDefinition playerController = network.Definitions.OfType<OdClassDefinition>()
                .Single(item => item.Id == OdNetworkPackage.PlayerControllerClassId);
            OdClassDefinition pawn = network.Definitions.OfType<OdClassDefinition>()
                .Single(item => item.Id == OdNetworkPackage.PawnClassId);
            Assert.That(playerController.PreventInheritance, Is.True);
            Assert.That(playerController.Replication, Is.EqualTo(OdObjectReplicationMode.OwnerOnly));
            Assert.That(pawn.Replication, Is.EqualTo(OdObjectReplicationMode.AllClients));
        });
    }

    [Test]
    public void InheritedClassesIncludeTheCompleteChainInDisplayOrder()
    {
        var project = TestProject.Create();
        var derived = project.Definitions.OfType<OdClassDefinition>().Single();
        var directBase = new OdClassDefinition { Name = "DirectBase" };
        var rootBase = new OdClassDefinition { Name = "RootBase" };
        derived.IsClasses.Add(directBase.Id);
        directBase.IsClasses.Add(rootBase.Id);
        project.Definitions.AddRange(new OdDefinition[] { directBase, rootBase });

        var inheritedClasses = project.GetInheritedClasses(derived);

        Assert.That(inheritedClasses.Select(item => item.Name), Is.EqualTo(new[] { "DirectBase", "RootBase" }));
    }

    [Test]
    public void ValidatorAcceptsInheritedDrivenDependenciesAndRejectsNetworkingAndCycles()
    {
        var project = TestProject.Create();
        var actor = project.Definitions.OfType<OdClassDefinition>().Single();
        var baseValue = new OdFieldDefinition { Name = "BaseValue" };
        var baseClass = new OdClassDefinition
        {
            Name = "BaseActor",
            Namespace = project.DefaultNamespace,
            Fields = { baseValue },
        };
        actor.IsClasses.Add(baseClass.Id);
        project.Definitions.Add(baseClass);
        var first = new OdFieldDefinition
        {
            Name = "FirstDriven",
            Mode = OdFieldMode.Driven,
            Replication = OdFieldReplicationMode.None,
            DrivenByFields = { baseValue.Id },
        };
        var second = new OdFieldDefinition
        {
            Name = "SecondDriven",
            Mode = OdFieldMode.Driven,
            Replication = OdFieldReplicationMode.None,
            DrivenByFields = { first.Id },
        };
        actor.Fields.AddRange(new[] { first, second });

        Assert.That(OdProjectValidator.Validate(project),
            Has.None.Matches<OdIssue>(issue => issue.Code is "OD0221" or "OD0225" or "OD0226"));

        first.Replication = OdFieldReplicationMode.AllClients;
        second.DrivenByFields.Add(first.Id);
        first.DrivenByFields.Clear();
        first.DrivenByFields.Add(second.Id);
        var issues = OdProjectValidator.Validate(project);

        Assert.Multiple(() =>
        {
            Assert.That(issues, Has.Some.Matches<OdIssue>(issue => issue.Code == "OD0221"));
            Assert.That(issues, Has.Some.Matches<OdIssue>(issue => issue.Code == "OD0226"));
        });
    }

    [Test]
    public void NotNullIsOnlyValidForSingleClassInterfaceInputs()
    {
        var project = TestProject.Create();
        var contract = project.Definitions.OfType<OdInterfaceDefinition>().Single();
        var target = contract.Inputs.Single(item => item.Name == "Target");

        Assert.That(OdProjectValidator.Validate(project),
            Has.None.Matches<OdIssue>(issue => issue.Code == "OD0227"));

        target.Type.Container = OdContainerKind.List;
        var issues = OdProjectValidator.Validate(project);

        Assert.That(issues, Has.Some.Matches<OdIssue>(issue => issue.Code == "OD0227"));
    }

    [Test]
    public void ProjectRequiresAnExistingInitialScene()
    {
        OdProject project = TestProject.Create();
        OdClassDefinition actor = project.Definitions.OfType<OdClassDefinition>().Single();
        var sceneObject = new OdSceneObject { Name = "World", ClassDefinitionId = actor.Id };
        var scene = new OdScene
        {
            Name = "Main",
            OdId = project.Ods.Single().Id,
            Objects = { sceneObject },
        };
        project.Scenes.Add(scene);

        Assert.That(OdProjectValidator.Validate(project),
            Has.Some.Matches<OdIssue>(issue => issue.Code == "OD0510"));

        project.InitialSceneId = scene.Id;
        Assert.That(OdProjectValidator.Validate(project),
            Has.None.Matches<OdIssue>(issue => issue.Code is "OD0510" or "OD0511"));

        project.InitialSceneId = Guid.NewGuid();
        Assert.That(OdProjectValidator.Validate(project),
            Has.Some.Matches<OdIssue>(issue => issue.Code == "OD0511"));
    }

    [Test]
    public void InterfacesMustBeRegisteredInTheOdModel()
    {
        OdProject project = TestProject.Create();
        project.CodeFiles.Add(new OdCodeFile
        {
            Kind = OdCodeFileKind.Runtime,
            RelativePath = "Tests/OD/Runtime/Helpers.cs",
            Content = "public struct HiddenAction : global::GMCore.ICheckableInterface<Result> { }",
        });

        Assert.That(OdProjectValidator.Validate(project),
            Has.Some.Matches<OdIssue>(issue => issue.Code == "OD0700"));

        project.CodeFiles.Single().Kind = OdCodeFileKind.Interface;
        Assert.That(OdProjectValidator.Validate(project),
            Has.None.Matches<OdIssue>(issue => issue.Code == "OD0700"));
    }

    [Test]
    public void ValidatorAcceptsConcreteInterfaceCallAssignedThroughAnAbstractBase()
    {
        OdProject project = TestProject.Create();
        var movePiece = new OdInterfaceDefinition
        {
            Name = "MovePiece",
            Namespace = project.DefaultNamespace,
            Abstract = true,
            Inputs = { new OdFieldDefinition { Name = "From", Type = OdTypeReference.BuiltInType(OdBuiltInType.Int32) } },
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
        var action = new OdFieldDefinition
        {
            Name = "MoveAction",
            Type = new OdTypeReference { DefinitionId = movePiece.Id },
        };
        var resource = new OdResourceDefinition
        {
            Name = "PieceDefinition",
            Namespace = project.DefaultNamespace,
            Fields = { action },
        };
        var call = OdInterfaceCallValue.Create(moveQueen.Id);
        OdInterfaceCallValue.GetValues(call)![distance.Id.ToString("D")] = 7;
        var record = new OdDataRecord { Name = "Queen", Values = { [action.Id] = call } };
        project.Definitions.AddRange(new OdDefinition[] { movePiece, moveQueen, resource });
        project.DataSets.Add(new OdDataSet
        {
            Name = "Pieces",
            DefinitionId = resource.Id,
            Records = { record },
        });

        IReadOnlyList<OdIssue> issues = OdProjectValidator.Validate(project);

        Assert.That(issues, Has.None.Matches<OdIssue>(issue => issue.Code.StartsWith("OD052", StringComparison.Ordinal)),
            string.Join(Environment.NewLine, issues));
    }

    [Test]
    public void ValidatorRejectsInterfaceInheritanceCyclesAndWrongConfiguredTypes()
    {
        OdProject project = TestProject.Create();
        var first = new OdInterfaceDefinition { Name = "First", Namespace = project.DefaultNamespace, Abstract = true };
        var second = new OdInterfaceDefinition { Name = "Second", Namespace = project.DefaultNamespace, Abstract = true };
        var unrelated = new OdInterfaceDefinition { Name = "Unrelated", Namespace = project.DefaultNamespace };
        first.BaseInterfaceId = second.Id;
        second.BaseInterfaceId = first.Id;
        var action = new OdFieldDefinition
        {
            Name = "Action",
            Type = new OdTypeReference { DefinitionId = first.Id },
        };
        var resource = new OdResourceDefinition
        {
            Name = "Actions",
            Namespace = project.DefaultNamespace,
            Fields = { action },
        };
        var record = new OdDataRecord { Name = "Bad", Values = { [action.Id] = OdInterfaceCallValue.Create(unrelated.Id) } };
        project.Definitions.AddRange(new OdDefinition[] { first, second, unrelated, resource });
        project.DataSets.Add(new OdDataSet { Name = "Actions", DefinitionId = resource.Id, Records = { record } });

        IReadOnlyList<OdIssue> issues = OdProjectValidator.Validate(project);

        Assert.Multiple(() =>
        {
            Assert.That(issues, Has.Some.Matches<OdIssue>(issue => issue.Code == "OD0275"));
            Assert.That(issues, Has.Some.Matches<OdIssue>(issue => issue.Code == "OD0521"));
        });
    }
}
