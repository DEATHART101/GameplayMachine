using System.Diagnostics;
using System.Security;
using ODStudio.Export;
using ODStudio.Export.Unity;
using ODStudio.Model;
using ODStudio.Package;

namespace ODStudio.Tests;

public sealed class UnityExportTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "odstudio-unity-export-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [Test]
    public void UnityPlanGeneratesResourcesAssetMappingsAndStableMetaFiles()
    {
        OdProject project = CreateUnityProject();
        var profile = new OdExportProfile { Name = "test", Target = "unity" };
        var plan = new UnityExportTarget().BuildPlan(new OdExportModel(project), profile);

        Assert.That(plan.Succeeded, Is.True, string.Join(Environment.NewLine, plan.Issues));
        string types = File(plan, "Runtime/Generated/Tests/OD/ODTypes.g.cs");
        string module = File(plan, "Runtime/Generated/Tests/OD/ODModule.g.cs");
        string config = File(plan, "Runtime/Unity/Resources/Config.g.cs");
        string configExtension = File(plan, "User/Tests/OD/Resources/Config.cs");
        string actor = File(plan, "Runtime/Unity/ClassResources/ActorResource.g.cs");
        string actorDisplayer = File(plan, "Runtime/Unity/Displayers/ActorDisplayerBase.g.cs");
        string scene = File(plan, "Runtime/Unity/Scenes/TestArenaSceneAsset.g.cs");
        string runtime = File(plan, "Runtime/Unity/ODUnityRuntime.g.cs");
        string registry = File(plan, "Runtime/Unity/ODUnityResourceRegistry.cs");
        string editor = File(plan, "Editor/ODUnityResourceEditor.g.cs");
        Assert.Multiple(() =>
        {
            Assert.That(plan.Issues, Has.None.Matches<OdIssue>(issue => issue.Code == "ODU0101"));
            Assert.That(types, Does.Not.Contain("partial class Config"));
            Assert.That(types, Does.Not.Contain("partial class SpriteAsset"));
            Assert.That(types, Does.Contain("public enum Mood : int"));
            Assert.That(types, Does.Contain("[global::UnityEngine.SerializeField]"));
            Assert.That(types, Does.Contain("public global::System.String Name = global::System.String.Empty;"));
            Assert.That(config, Does.Contain("partial class Config : global::ODStudio.Unity.Runtime.ODUnityResourceAsset"));
            Assert.That(config, Does.Contain("menuName = \"OD/TestGame/Resources/Config\""));
            Assert.That(configExtension, Does.Contain("partial class Config"));
            Assert.That(configExtension, Does.Contain("never overwritten by GameplayMachine Editor"));
            Assert.That(config, Does.Contain("public global::Tests.OD.SpriteAsset? Icon;"));
            Assert.That(config, Does.Contain("public global::Tests.OD.ActorResource? PlayerHero;"));
            Assert.That(config, Does.Contain("public global::System.String DisplayName = global::System.String.Empty;"));
            Assert.That(config, Does.Contain("SerializableSet<global::System.String> Labels"));
            Assert.That(config, Does.Contain("SerializableDictionary<global::System.String, global::System.Int32> Scores"));
            Assert.That(actor, Does.Contain("sealed partial class ActorResource"));
            Assert.That(actor, Does.Contain("ODUnityClassResource"));
            Assert.That(actor, Does.Contain("public global::System.String Name = global::System.String.Empty;"));
            Assert.That(actor, Does.Contain("public global::Tests.OD.ActorResource? Template;"));
            Assert.That(actor, Does.Contain("SerializableDictionary<global::Tests.OD.ActorResource, global::Tests.OD.ActorResource> ResourceMap"));
            Assert.That(actor, Does.Contain("CreateGameplayObject(this global::GMCore.GameplayMachine machine, ActorResource resource)"));
            Assert.That(actor, Does.Contain("public global::Tests.OD.Config? Config;"));
            Assert.That(actor, Does.Contain("public global::Tests.OD.SpriteAsset? Portrait;"));
            Assert.That(actor, Does.Contain("public global::Tests.OD.Mood Mood;"));
            Assert.That(actorDisplayer, Does.Contain("abstract class ActorDisplayerBase : global::ODStudio.Unity.Runtime.GameplayObjectDisplayer<global::Tests.OD.Actor>"));
            Assert.That(actorDisplayer, Does.Contain("protected sealed override void BindGameplayObjectFields(global::Tests.OD.Actor target)"));
            Assert.That(actorDisplayer, Does.Not.Contain("OnActorBound"));
            Assert.That(actorDisplayer, Does.Contain("BindAndTrigger(target.AfterHealthChanged, _ => OnHealthChanged(Target.Health));"));
            Assert.That(actorDisplayer, Does.Contain("protected virtual void OnHealthChanged(global::System.Int32 value)"));
            Assert.That(actorDisplayer, Does.Contain("TrackBinding(target.TagsBinder.BindItemChanged(change => OnTagsItemChanged(change.ChangeType, change.Item)));"));
            Assert.That(actorDisplayer, Does.Contain("BindAndTrigger(target.AfterTagsChanged, _ => OnTagsChanged());"));
            Assert.That(actorDisplayer, Does.Contain("protected virtual void OnTagsChanged()"));
            Assert.That(actorDisplayer, Does.Contain("protected virtual void OnTagsItemChanged(global::GMCore.CollectionItemChangeType changeType, global::System.String item)"));
            Assert.That(actorDisplayer, Does.Contain("TrackBinding(target.ResourceMapBinder.BindItemChanged(change => OnResourceMapItemChanged(change.ChangeType, change.Item)));"));
            Assert.That(actorDisplayer, Does.Contain("BindAndTrigger(target.AfterResourceMapChanged, _ => OnResourceMapChanged());"));
            Assert.That(actorDisplayer, Does.Contain("protected virtual void OnResourceMapChanged()"));
            Assert.That(actorDisplayer, Does.Contain("protected virtual void OnResourceMapItemChanged(global::GMCore.CollectionItemChangeType changeType, global::Tests.OD.ActorResource item)"));
            Assert.That(module, Does.Contain("ODUnityResourceRegistry.Write(writer, value)"));
            Assert.That(module, Does.Contain("ODUnityResourceRegistry.Read<global::Tests.OD.Config>(reader)"));
            Assert.That(module, Does.Contain("ODUnityResourceRegistry.Read<global::Tests.OD.ActorResource>(reader)"));
            Assert.That(module, Does.Contain("Required resource 'global::Tests.OD.ActorResource' cannot be null."));
            Assert.That(module, Does.Contain("IODResourceCatalogNetworkModule"));
            Assert.That(module, Does.Contain("ResourceCatalogID => global::GMCore.ODSaveHash.Compute(\"od-resource-catalog:2"));
            Assert.That(module, Does.Not.Contain("ODUnityResourceRegistry.CatalogID"));
            Assert.That(module, Does.Contain("global::Tests.OD.SpriteAsset.StructName"));
            Assert.That(runtime, Does.Not.Contain("class ODUnityResourceRegistry"));
            Assert.That(runtime, Does.Contain("abstract class ODGameplaySceneAsset"));
            Assert.That(scene, Does.Contain("class TestArenaSceneAsset"));
            Assert.That(scene, Does.Contain("TestArenaScene.Instance"));
            Assert.That(registry, Does.Contain("class ODUnityResourceRegistry"));
            Assert.That(runtime, Does.Contain("[global::UnityEngine.HideInInspector]"));
            Assert.That(runtime, Does.Contain("#pragma warning disable CS0649"));
            Assert.That(registry, Does.Contain("writer.WriteVarUInt32(GetNetworkId(value))"));
            Assert.That(registry, Does.Contain("writer.WriteResourceID(GetId(value))"));
            Assert.That(editor, Does.Contain("IPreprocessBuildWithReport"));
            Assert.That(editor, Does.Contain("RebuildResourceRegistry();"));
            Assert.That(editor, Does.Contain("od-resource-catalog:2"));
            Assert.That(editor, Does.Contain("ODUnityResourceRegistryPostprocessor"));
            Assert.That(editor, Does.Contain("FindAssets(\"t:ScriptableObject\", new[] { \"Assets/ODGeneratedResources\" })"));
            Assert.That(editor, Does.Contain("path.StartsWith(\"Assets/ODGeneratedResources/\""));
            Assert.That(editor, Does.Contain("path.StartsWith(\"Assets/ODGeneratedScenes/\""));
            Assert.That(editor, Does.Contain("InitializeOnLoadMethod"));
            Assert.That(editor, Does.Contain("DeleteAsset(path)"));
            Assert.That(editor, Does.Contain("MaterializeResources();"));
            Assert.That(editor, Does.Contain("EnsureResourceAsset<global::Tests.OD.ActorResource>"));
            Assert.That(editor, Does.Contain("EnsureSceneAsset<global::Tests.OD.TestArenaSceneAsset>"));
            Assert.That(editor, Does.Contain("Assets/ODGeneratedScenes/TestGame/TestArena.asset"));
            Assert.That(editor, Does.Not.Contain("SaveAsPrefabAsset"));
            Assert.That(runtime, Does.Not.Contain("IsMainResource"));
            Assert.That(plan.Artifacts, Has.Some.Matches<OdExportArtifact>(item =>
                item.RelativePath == "Runtime/Unity/Resources/Config.g.cs.meta" &&
                item.Content.Contains("guid:") && item.Content.EndsWith('\n')));
            Assert.That(plan.Artifacts, Has.Some.Matches<OdExportArtifact>(item =>
                item.RelativePath == "User/Tests/OD/Resources/Config.cs" && !item.OverwriteExisting &&
                item.LegacyRelativePaths!.Contains("User/Tests/OD/Unity/Resources/Config.cs") &&
                item.LegacyRelativePaths!.Contains("Runtime/Unity/Resources/Config.cs")));
            Assert.That(plan.Artifacts, Has.None.Matches<OdExportArtifact>(item =>
                item.RelativePath == "Runtime/Unity/Resources/Config.cs.meta"));
            Assert.That(plan.Artifacts, Has.None.Matches<OdExportArtifact>(item =>
                item.RelativePath.Contains("ClassResources/ActorResource.cs") && !item.OverwriteExisting));
        });
    }

    [Test]
    public async Task UnityExportPublishesManifestRemovesRenamedGeneratedFileAndCompilesWithStubs()
    {
        OdProject project = CreateUnityProject();
        string packagePath = Path.Combine(_directory, "test.odpkg");
        string outputPath = Path.Combine(_directory, "UnityExport");
        await OdPackageCompiler.BuildAsync(project, packagePath);
        var profile = new OdExportProfile { Name = "test", Target = "unity" };
        var first = await OdExportPipeline.ExportAsync(new UnityExportTarget(),
            new OdExportRequest(packagePath, outputPath, profile));
        Assert.That(first.Succeeded, Is.True, string.Join(Environment.NewLine, first.Plan.Issues));
        string oldResource = Path.Combine(outputPath, "Runtime", "Unity", "Resources", "Config.g.cs");
        string resourceExtension = Path.Combine(outputPath, "User", "Tests", "OD", "Resources", "Config.cs");
        string oldMeta = oldResource + ".meta";
        string stableMeta = await System.IO.File.ReadAllTextAsync(oldMeta);
        const string customResourceCode = """
            #nullable enable
            namespace Tests.OD
            {
                public partial class Config
                {
                    public global::UnityEngine.AddressableAssets.AssetReferenceT<global::UnityEngine.Sprite>? Front;
                }
            }
            """;
        await System.IO.File.WriteAllTextAsync(resourceExtension, customResourceCode);
        string userFile = Path.Combine(outputPath, "UserOwned.cs");
        await System.IO.File.WriteAllTextAsync(userFile, "// user owned");
        Assert.That(System.IO.File.Exists(Path.Combine(outputPath, ".gameplaymachine-export.json")), Is.True);

        var config = project.Definitions.OfType<OdResourceDefinition>().Single(item => item.Name == "Config");
        config.Name = "GameConfig";
        await OdPackageCompiler.BuildAsync(project, packagePath);
        var second = await OdExportPipeline.ExportAsync(new UnityExportTarget(),
            new OdExportRequest(packagePath, outputPath, profile));
        string newResource = Path.Combine(outputPath, "Runtime", "Unity", "Resources", "GameConfig.g.cs");
        Assert.Multiple(() =>
        {
            Assert.That(System.IO.File.Exists(oldResource), Is.False);
            Assert.That(System.IO.File.Exists(oldMeta), Is.False);
            Assert.That(System.IO.File.Exists(newResource), Is.True);
            Assert.That(System.IO.File.ReadAllText(newResource + ".meta"), Is.EqualTo(stableMeta));
            Assert.That(System.IO.File.ReadAllText(resourceExtension), Is.EqualTo(customResourceCode));
            Assert.That(System.IO.File.ReadAllText(userFile), Is.EqualTo("// user owned"));
        });

        await System.IO.File.WriteAllTextAsync(Path.Combine(_directory, "UnityStubs.cs"), UnityStubs);
        string projectFile = Path.Combine(_directory, "UnityGeneratedCompile.csproj");
        string coreProject = FindGameplayMachineCoreProject();
        await System.IO.File.WriteAllTextAsync(projectFile, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <WarningsAsErrors>CS8600;CS8601;CS8602;CS8603;CS8604;CS8618;CS8619;CS8625</WarningsAsErrors>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{SecurityElement.Escape(coreProject)}}" />
              </ItemGroup>
            </Project>
            """);
        using var process = Process.Start(new ProcessStartInfo("dotnet", $"build \"{projectFile}\" --nologo --disable-build-servers -nodeReuse:false -p:UseSharedCompilation=false")
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
    }

    [Test]
    public void UnityDependencyInstallerAddsNewtonsoftPackageAndIsIdempotent()
    {
        string projectDirectory = Path.Combine(_directory, "UnityProject");
        string outputDirectory = Path.Combine(projectDirectory, "Assets", "GameplayMachine");
        string packagesDirectory = Path.Combine(projectDirectory, "Packages");
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(packagesDirectory);
        string manifestPath = Path.Combine(packagesDirectory, "manifest.json");
        System.IO.File.WriteAllText(manifestPath, """
            {
              "dependencies": {
                "com.unity.ugui": "2.0.0"
              }
            }
            """);

        string? first = UnityExportTarget.EnsureUnityProjectDependencies(outputDirectory);
        string afterFirst = System.IO.File.ReadAllText(manifestPath);
        string? second = UnityExportTarget.EnsureUnityProjectDependencies(outputDirectory);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(manifestPath));
            Assert.That(second, Is.EqualTo(manifestPath));
            Assert.That(System.IO.File.ReadAllText(manifestPath), Is.EqualTo(afterFirst));
            Assert.That(afterFirst, Does.Contain($"\"{UnityExportTarget.NewtonsoftPackageName}\": \"{UnityExportTarget.NewtonsoftPackageVersion}\""));
        });
    }

    [Test]
    public async Task UnityExportMigratesLegacyUserPartialsAndMetaFiles()
    {
        OdProject project = CreateUnityProject();
        string packagePath = Path.Combine(_directory, "test.odpkg");
        string outputPath = Path.Combine(_directory, "UnityExport");
        string legacyInterfacePath = Path.Combine(outputPath, "Runtime", "Generated", "Tests", "OD", "Interfaces", "DamageParam.cs");
        string legacyResourcePath = Path.Combine(outputPath, "User", "Tests", "OD", "Unity", "Resources", "Config.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyInterfacePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyResourcePath)!);
        await System.IO.File.WriteAllTextAsync(legacyInterfacePath, "// legacy interface partial");
        await System.IO.File.WriteAllTextAsync(legacyResourcePath, "// legacy resource partial");
        await System.IO.File.WriteAllTextAsync(legacyResourcePath + ".meta", "fileFormatVersion: 2\nguid: legacy\n");
        await OdPackageCompiler.BuildAsync(project, packagePath);

        OdExportResult result = await OdExportPipeline.ExportAsync(new UnityExportTarget(),
            new OdExportRequest(packagePath, outputPath, new OdExportProfile { Name = "test", Target = "unity" }));

        string interfacePath = Path.Combine(outputPath, "Runtime", "Generated", "Tests", "OD", "Interfaces", "DamageParam.cs");
        string resourcePath = Path.Combine(outputPath, "User", "Tests", "OD", "Resources", "Config.cs");
        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, string.Join(Environment.NewLine, result.Plan.Issues));
            Assert.That(System.IO.File.Exists(legacyInterfacePath), Is.True);
            Assert.That(System.IO.File.Exists(legacyResourcePath), Is.False);
            Assert.That(System.IO.File.Exists(legacyResourcePath + ".meta"), Is.False);
            Assert.That(Directory.Exists(Path.Combine(outputPath, "User", "Tests", "OD", "Unity")), Is.False);
            Assert.That(System.IO.File.ReadAllText(interfacePath), Is.Not.EqualTo("// legacy interface partial"));
            Assert.That(System.IO.File.ReadAllText(resourcePath), Is.EqualTo("// legacy resource partial"));
            Assert.That(System.IO.File.ReadAllText(resourcePath + ".meta"), Is.EqualTo("fileFormatVersion: 2\nguid: legacy\n"));
        });
    }

    [Test]
    public void ExportRejectsArtifactsOutsideOutputDirectory()
    {
        var plan = new OdExportPlan
        {
            TargetId = "test",
            TargetVersion = "1",
            Issues = Array.Empty<OdIssue>(),
            Artifacts = new[] { new OdExportArtifact("../outside.cs", "unsafe") },
        };

        Assert.That(async () => await OdExportPipeline.PublishAsync(plan, _directory),
            Throws.TypeOf<InvalidDataException>());
    }

    private static OdProject CreateUnityProject()
    {
        OdProject project = TestProject.Create();
        var actor = project.Definitions.OfType<OdClassDefinition>().Single();
        actor.Fields.Add(new OdFieldDefinition
        {
            Name = "Name",
            Type = OdTypeReference.BuiltInType(OdBuiltInType.String),
        });
        var sprite = new OdResourceDefinition
        {
            Name = "SpriteAsset",
            Namespace = "Tests.OD",
        };
        var config = new OdResourceDefinition
        {
            Name = "Config",
            Namespace = "Tests.OD",
            Fields =
            {
                new OdFieldDefinition { Name = "DisplayName", Type = OdTypeReference.BuiltInType(OdBuiltInType.String) },
                new OdFieldDefinition { Name = "Icon", Type = new OdTypeReference { DefinitionId = sprite.Id } },
                new OdFieldDefinition { Name = "PlayerHero", Type = new OdTypeReference { DefinitionId = actor.Id, IsResourceClass = true } },
                new OdFieldDefinition { Name = "Names", Type = new OdTypeReference { BuiltIn = OdBuiltInType.String, Container = OdContainerKind.List } },
                new OdFieldDefinition { Name = "Labels", Type = new OdTypeReference { BuiltIn = OdBuiltInType.String, Container = OdContainerKind.Set } },
                new OdFieldDefinition
                {
                    Name = "Scores",
                    Type = new OdTypeReference
                    {
                        BuiltIn = OdBuiltInType.Int32,
                        Container = OdContainerKind.Dictionary,
                        DictionaryKeyBuiltIn = OdBuiltInType.String,
                    },
                },
            },
        };
        var choice = new OdSwitchStructDefinition
        {
            Name = "Choice",
            Namespace = "Tests.OD",
            Fields =
            {
                new OdFieldDefinition { Name = "Text", Type = OdTypeReference.BuiltInType(OdBuiltInType.String) },
                new OdFieldDefinition { Name = "Number", Type = OdTypeReference.BuiltInType(OdBuiltInType.Int32) },
            },
        };
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
        actor.Fields.Add(new OdFieldDefinition { Name = "Config", Type = new OdTypeReference { DefinitionId = config.Id } });
        actor.Fields.Add(new OdFieldDefinition { Name = "Template", Type = new OdTypeReference { DefinitionId = actor.Id, IsResourceClass = true } });
        actor.Fields.Add(new OdFieldDefinition
        {
            Name = "Tags",
            Type = new OdTypeReference
            {
                BuiltIn = OdBuiltInType.String,
                Container = OdContainerKind.Set,
            },
        });
        actor.Fields.Add(new OdFieldDefinition
        {
            Name = "ResourceMap",
            Type = new OdTypeReference
            {
                DefinitionId = actor.Id,
                IsResourceClass = true,
                Container = OdContainerKind.Dictionary,
                DictionaryKeyBuiltIn = OdBuiltInType.None,
                DictionaryKeyDefinitionId = actor.Id,
                DictionaryKeyIsResourceClass = true,
            },
        });
        actor.Fields.Add(new OdFieldDefinition { Name = "Portrait", Type = new OdTypeReference { DefinitionId = sprite.Id } });
        actor.Fields.Add(new OdFieldDefinition { Name = "Choice", Type = new OdTypeReference { DefinitionId = choice.Id } });
        actor.Fields.Add(new OdFieldDefinition { Name = "Mood", Type = new OdTypeReference { DefinitionId = mood.Id } });
        project.Definitions.AddRange(new OdDefinition[] { sprite, config, choice, mood });
        var sceneActor = new OdClassDefinition { Name = "SceneActor", Namespace = project.DefaultNamespace };
        project.Definitions.Add(sceneActor);
        var sceneObject = new OdSceneObject { Name = "Player", ClassDefinitionId = sceneActor.Id };
        var testScene = new OdScene
        {
            Name = "TestArena",
            OdId = project.Ods.Single().Id,
            Objects = { sceneObject },
        };
        project.Scenes.Add(testScene);
        project.InitialSceneId = testScene.Id;
        return project;
    }

    private static string File(OdExportPlan plan, string path) =>
        plan.Artifacts.Single(item => item.RelativePath == path).Content;

    private static string FindGameplayMachineCoreProject()
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current is not null && !string.Equals(current.Name, "od-manipulator", StringComparison.OrdinalIgnoreCase))
            current = current.Parent;
        if (current?.Parent is null)
            throw new DirectoryNotFoundException("Could not locate gameplaymachine_core.");
        return Path.Combine(current.Parent.FullName, "gameplaymachine_core", "main", "GameplayMachineCore", "GameplayMachineCore.csproj");
    }

    private const string UnityStubs = """
        namespace UnityEngine
        {
            public class Object
            {
                public string name = string.Empty;
                public int GetInstanceID() => 0;
                public static bool operator ==(Object? left, Object? right) => global::System.Object.ReferenceEquals(left, right);
                public static bool operator !=(Object? left, Object? right) => !(left == right);
                public override bool Equals(object? value) => base.Equals(value);
                public override int GetHashCode() => base.GetHashCode();
            }
            public class ScriptableObject : Object { }
            public class Component : Object { public T[] GetComponents<T>() => global::System.Array.Empty<T>(); }
            public class MonoBehaviour : Component { }
            public class GameObject : Object { }
            public class Sprite : Object { }
            public class Texture2D : Object { }
            public class AudioClip : Object { }
            public class Material : Object { }
            public static class Resources { public static T? Load<T>(string path) where T : Object => null; }
            public interface ISerializationCallbackReceiver { void OnBeforeSerialize(); void OnAfterDeserialize(); }
            [global::System.AttributeUsage(global::System.AttributeTargets.Field)]
            public sealed class SerializeField : global::System.Attribute { }
            [global::System.AttributeUsage(global::System.AttributeTargets.Field)]
            public sealed class HideInInspector : global::System.Attribute { }
            [global::System.AttributeUsage(global::System.AttributeTargets.Class)]
            public sealed class CreateAssetMenuAttribute : global::System.Attribute
            {
                public string fileName = string.Empty;
                public string menuName = string.Empty;
            }
            [global::System.AttributeUsage(global::System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class RequireComponent : global::System.Attribute { public RequireComponent(global::System.Type type) { } }
        }
        namespace UnityEngine.AddressableAssets
        {
            public class AssetReference
            {
                public AssetReference(string guid) { AssetGUID = guid; }
                public string AssetGUID { get; }
            }
            public class AssetReferenceT<T> : AssetReference where T : global::UnityEngine.Object
            {
                public AssetReferenceT(string guid) : base(guid) { }
            }
        }
        namespace ODStudio.Unity.Runtime
        {
            public abstract class GameplayObjectDisplayer<T> : global::UnityEngine.MonoBehaviour
                where T : struct, global::GMCore.IGameplayObjectOperator, global::GMCore.IODClass
            {
                public T Target { get; private set; }
                protected abstract void BindGameplayObjectFields(T target);
                protected void BindAndTrigger(global::GMCore.FieldChangeEventBinder field, global::System.Action<object?> callback) { }
                protected void TrackBinding(global::XLifetimeObject.IReturnableHandle binding) { }
            }
        }
        """;
}
