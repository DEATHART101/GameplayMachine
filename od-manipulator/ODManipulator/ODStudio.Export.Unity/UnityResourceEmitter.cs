using ODStudio.Generator;
using ODStudio.Model;
using System.Globalization;
using System.Text.Json.Nodes;

namespace ODStudio.Export.Unity;

internal static class UnityResourceEmitter
{
    public static string EmitResource(OdGenerationContext context, OdResourceDefinition definition,
        UnityTypeMapper mapper)
    {
        var writer = new CodeWriter();
        Header(writer);
        using (writer.Block($"namespace {context.Namespace(definition)}"))
        {
            writer.Line($"[global::UnityEngine.CreateAssetMenu(fileName = {OdGenerationContext.StringLiteral(definition.Name)}, menuName = {OdGenerationContext.StringLiteral($"OD/{context.Scope.Name}/Resources/{definition.Name}")})]");
            writer.Line("[global::System.Serializable]");
            using (writer.Block($"public partial class {OdGenerationContext.Identifier(definition.Name)} : global::ODStudio.Unity.Runtime.ODUnityResourceAsset, global::GMCore.IODStruct"))
            {
                writer.Line($"public static readonly global::GMCore.ODStructName StructName = new global::GMCore.ODStructName {{ NameObject = {OdGenerationContext.StringLiteral(context.StructStableName(definition))} }};");
                foreach (OdFieldDefinition field in definition.Fields)
                {
                    string initializer = FieldInitializer(field);
                    writer.Line($"public {mapper.FieldTypeName(context, field)} {OdGenerationContext.Identifier(field.Name)}{initializer}");
                }
                writer.Line("public global::GMCore.ODStructName GetODStructName() => StructName;");
            }
        }
        return writer.ToString();
    }

    public static string EmitResourceExtension(OdGenerationContext context, OdResourceDefinition definition)
    {
        var writer = new CodeWriter();
        UserHeader(writer);
        using (writer.Block($"namespace {context.Namespace(definition)}"))
        using (writer.Block($"public partial class {OdGenerationContext.Identifier(definition.Name)}"))
            writer.Line("// Add Unity-only serialized fields and behavior here.");
        return writer.ToString();
    }

    public static string EmitClassResource(OdGenerationContext context, OdClassDefinition definition,
        UnityTypeMapper mapper)
    {
        var writer = new CodeWriter();
        Header(writer);
        using (writer.Block($"namespace {context.Namespace(definition)}"))
        {
            writer.Line($"[global::UnityEngine.CreateAssetMenu(fileName = {OdGenerationContext.StringLiteral(definition.Name)}, menuName = {OdGenerationContext.StringLiteral($"OD/{context.Scope.Name}/Class Resources/{definition.Name}")})]");
            writer.Line("[global::System.Serializable]");
            using (writer.Block($"public sealed partial class {OdGenerationContext.Identifier(definition.Name)}Resource : global::ODStudio.Unity.Runtime.ODUnityClassResource, global::GMCore.IODClassResource"))
            {
                foreach (OdFieldDefinition field in context.AllFields(definition).Select(item => item.Field)
                             .Where(item => item.Mode != OdFieldMode.Driven && !mapper.IsLiveClassReference(item)))
                {
                    string initializer = FieldInitializer(field);
                    writer.Line($"public {mapper.FieldTypeName(context, field)} {OdGenerationContext.Identifier(field.Name)}{initializer}");
                }
                writer.Line($"public global::GMCore.ODClassName ODClassName => {context.FullName(definition)}.ClassName;");
                using (writer.Block("public void Set(global::GMCore.IGameplayObjectOperator gameplayObject)"))
                {
                    foreach (var (owner, field) in context.AllFields(definition)
                                 .Where(item => item.Field.Mode != OdFieldMode.Driven && !mapper.IsLiveClassReference(item.Field)))
                        EmitSetField(writer, context, owner, field);
                }
            }
            writer.Line();
            using (writer.Block("public static partial class UnityResourceExtensions"))
            {
                string className = context.FullName(definition);
                string resourceName = OdGenerationContext.Identifier(definition.Name) + "Resource";
                writer.Line($"public static {className} CreateGameplayObject(this global::GMCore.GameplayMachine machine, {resourceName} resource) => global::ODStudio.Unity.Runtime.ODUnityResourceFactory.Create<{className}>(machine, resource);");
                writer.Line($"public static {className} CreateGameplayObject(this global::GMCore.GameplayMachine.GameplayMachineProxy machine, {resourceName} resource) => global::ODStudio.Unity.Runtime.ODUnityResourceFactory.Create<{className}>(machine, resource);");
            }
        }
        return writer.ToString();
    }

    public static string EmitClassResourceExtension(OdGenerationContext context, OdClassDefinition definition)
    {
        var writer = new CodeWriter();
        UserHeader(writer);
        using (writer.Block($"namespace {context.Namespace(definition)}"))
        using (writer.Block($"public sealed partial class {OdGenerationContext.Identifier(definition.Name)}Resource"))
            writer.Line("// Add Unity-only serialized fields and behavior here.");
        return writer.ToString();
    }

    public static string EmitDisplayerBase(OdGenerationContext context, OdClassDefinition definition)
    {
        var writer = new CodeWriter();
        Header(writer);
        string className = OdGenerationContext.Identifier(definition.Name);
        string fullClassName = context.FullName(definition);
        IReadOnlyList<OdFieldDefinition> fields = context.AllFields(definition)
            .Select(item => item.Field)
            .ToList();

        using (writer.Block($"namespace {context.Namespace(definition)}"))
        using (writer.Block($"public abstract class {className}DisplayerBase : global::ODStudio.Unity.Runtime.GameplayObjectDisplayer<{fullClassName}>"))
        {
            using (writer.Block($"protected sealed override void BindGameplayObjectFields({fullClassName} target)"))
            {
                foreach (OdFieldDefinition field in fields)
                {
                    string fieldName = OdGenerationContext.Identifier(field.Name);
                    if (field.Type.Container is OdContainerKind.Set or OdContainerKind.Dictionary)
                        writer.Line($"TrackBinding(target.{fieldName}Binder.BindItemChanged(change => On{fieldName}ItemChanged(change.ChangeType, change.Item)));");
                    writer.Line(field.Type.Container == OdContainerKind.Single
                        ? $"BindAndTrigger(target.After{fieldName}Changed, _ => On{fieldName}Changed(Target.{fieldName}));"
                        : $"BindAndTrigger(target.After{fieldName}Changed, _ => On{fieldName}Changed());");
                }
            }

            foreach (OdFieldDefinition field in fields)
            {
                string fieldName = OdGenerationContext.Identifier(field.Name);
                writer.Line(field.Type.Container == OdContainerKind.Single
                    ? $"protected virtual void On{fieldName}Changed({context.FieldTypeName(field, gameplayCollection: true)} value) {{ }}"
                    : $"protected virtual void On{fieldName}Changed() {{ }}");
                if (field.Type.Container is OdContainerKind.Set or OdContainerKind.Dictionary)
                    writer.Line($"protected virtual void On{fieldName}ItemChanged(global::GMCore.CollectionItemChangeType changeType, {context.CollectionItemTypeName(field.Type)} item) {{ }}");
            }
        }
        return writer.ToString();
    }

    private static void EmitSetField(CodeWriter writer, OdGenerationContext context, OdClassDefinition owner,
        OdFieldDefinition field)
    {
        string member = OdGenerationContext.Identifier(field.Name);
        string fieldReference = $"{context.FullName(owner)}.Fields.{member}";
        if (field.Type.Container == OdContainerKind.Single)
        {
            writer.Line($"gameplayObject.Machine.SetGameplayObjectValue(gameplayObject.ObjectID, {fieldReference}, {member});");
            return;
        }

        string target = $"target_{field.Id:N}";
        writer.Line($"var {target} = new {context.FieldTypeName(field, gameplayCollection: true)} {{ Machine = gameplayObject.Machine, ObjectID = gameplayObject.ObjectID, FieldName = {fieldReference} }};");
        writer.Line($"{target}.Clear();");
        if (field.Type.Container == OdContainerKind.Dictionary)
        {
            using (writer.Block($"foreach (var item in {member})"))
                writer.Line($"{target}.Add(item.Key, item.Value);");
        }
        else
        {
            using (writer.Block($"foreach (var item in {member})"))
                writer.Line($"{target}.Add(item);");
        }
    }

    private static string FieldInitializer(OdFieldDefinition field)
    {
        if (field.Type.Container != OdContainerKind.Single)
            return " = new();";
        return field.Type.DefinitionId is null &&
               field.Type.BuiltIn == OdBuiltInType.String &&
               !field.Type.Nullable
            ? " = global::System.String.Empty;"
            : ";";
    }

    public static string EmitRuntimeSupport()
    {
        string runtime = """
        // <auto-generated />
        #nullable enable
        namespace ODStudio.Unity.Runtime
        {
            public abstract class ODGameplaySceneAsset : global::UnityEngine.ScriptableObject
            {
                public abstract string SceneName { get; }
                public abstract global::GMCore.IODScene Scene { get; }
            }

            public interface IODUnityResourceIdentity
            {
                string ODResourceID { get; }
                void SetODResourceID(string value);
            }

            [global::System.Serializable]
            public abstract class ODUnityResourceAsset : global::UnityEngine.ScriptableObject, IODUnityResourceIdentity
            {
                [global::UnityEngine.SerializeField]
                [global::UnityEngine.HideInInspector]
                private string odResourceId = global::System.Guid.NewGuid().ToString("N");
                public string ODResourceID => odResourceId;
                public void SetODResourceID(string value) => odResourceId = value;
            }

            [global::System.Serializable]
            public abstract class ODUnityClassResource : global::UnityEngine.ScriptableObject, IODUnityResourceIdentity
            {
                [global::UnityEngine.SerializeField]
                [global::UnityEngine.HideInInspector]
                private string odResourceId = global::System.Guid.NewGuid().ToString("N");
                public string ODResourceID => odResourceId;
                public void SetODResourceID(string value) => odResourceId = value;
            }

            public static class ODUnityResourceFactory
            {
                public static T Create<T>(global::GMCore.GameplayMachine machine, ODUnityClassResource resource)
                    where T : struct, global::GMCore.IGameplayObjectOperator, global::GMCore.IODClass =>
                    machine.CreateGameplayObject<T>(new global::GMCore.IODClassResource[] { (global::GMCore.IODClassResource)resource });

                public static T Create<T>(global::GMCore.GameplayMachine.GameplayMachineProxy machine, ODUnityClassResource resource)
                    where T : struct, global::GMCore.IGameplayObjectOperator, global::GMCore.IODClass =>
                    machine.CreateGameplayObject<T>(new global::GMCore.IODClassResource[] { (global::GMCore.IODClassResource)resource });
            }

            [global::System.Serializable]
            public sealed class SerializableSet<T> : global::System.Collections.Generic.IEnumerable<T>, global::UnityEngine.ISerializationCallbackReceiver
            {
                [global::UnityEngine.SerializeField]
                private global::System.Collections.Generic.List<T> values = new();
                public int Count => values.Count;
                public void ReplaceWith(global::System.Collections.Generic.IEnumerable<T> source)
                {
                    values.Clear();
                    values.AddRange(source);
                }
                public void OnBeforeSerialize() { }
                public void OnAfterDeserialize() { }
                public global::System.Collections.Generic.IEnumerator<T> GetEnumerator() => values.GetEnumerator();
                global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }

            [global::System.Serializable]
            public sealed class SerializableDictionary<TKey, TValue> : global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<TKey, TValue>>, global::UnityEngine.ISerializationCallbackReceiver
                where TKey : notnull
            {
                #pragma warning disable CS0649
                [global::System.Serializable]
                private struct Entry
                {
                    public TKey Key;
                    public TValue Value;
                }
                #pragma warning restore CS0649
                [global::UnityEngine.SerializeField]
                private global::System.Collections.Generic.List<Entry> values = new();
                public int Count => values.Count;
                public void ReplaceWith(global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<TKey, TValue>> source)
                {
                    values.Clear();
                    foreach (global::System.Collections.Generic.KeyValuePair<TKey, TValue> item in source)
                        values.Add(new Entry { Key = item.Key, Value = item.Value });
                }
                public void OnBeforeSerialize() { }
                public void OnAfterDeserialize() { }
                public global::System.Collections.Generic.IEnumerator<global::System.Collections.Generic.KeyValuePair<TKey, TValue>> GetEnumerator()
                {
                    foreach (Entry value in values)
                        yield return new global::System.Collections.Generic.KeyValuePair<TKey, TValue>(value.Key, value.Value);
                }
                global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }
        }
        """;
        return runtime;
    }

    public static string EmitSceneAsset(OdGenerationContext context, OdScene scene)
    {
        string sceneName = OdGenerationContext.SafeIdentifier(scene.Name);
        var writer = new CodeWriter();
        writer.Line("// <auto-generated />");
        writer.Line("#nullable enable");
        using (writer.Block($"namespace {context.RootNamespace}"))
        {
            writer.Line($"[global::UnityEngine.CreateAssetMenu(menuName = \"OD/{OdGenerationContext.SafeIdentifier(context.Scope.Name)}/Scenes/{sceneName}\")]");
            using (writer.Block($"public sealed class {sceneName}SceneAsset : global::ODStudio.Unity.Runtime.ODGameplaySceneAsset"))
            {
                writer.Line($"public override string SceneName => {OdGenerationContext.StringLiteral(scene.Name)};");
                writer.Line($"public override global::GMCore.IODScene Scene => global::{context.RootNamespace}.{sceneName}Scene.Instance;");
            }
        }
        return writer.ToString();
    }

    public static string EmitResourceRegistry() => """
        // <auto-generated />
        #nullable enable
        namespace ODStudio.Unity.Runtime
        {
            public sealed class ODUnityResourceRegistry : global::UnityEngine.ScriptableObject
            {
                [global::UnityEngine.SerializeField]
                private global::System.Collections.Generic.List<global::UnityEngine.Object> objects = new();
                [global::UnityEngine.SerializeField]
                private global::System.Collections.Generic.List<string> ids = new();
                [global::UnityEngine.SerializeField]
                private ulong catalogId;
                private static ODUnityResourceRegistry? instance;
                private global::System.Collections.Generic.Dictionary<global::UnityEngine.Object, global::GMCore.ODResourceID>? objectIds;
                private global::System.Collections.Generic.Dictionary<global::GMCore.ODResourceID, global::UnityEngine.Object>? idObjects;
                private global::System.Collections.Generic.Dictionary<global::UnityEngine.Object, uint>? networkIds;

                private static ODUnityResourceRegistry Instance => instance ??=
                    global::UnityEngine.Resources.Load<ODUnityResourceRegistry>("GameplayMachine/ODUnityResourceRegistry") ??
                    throw new global::System.InvalidOperationException("OD Unity resource registry is missing. Run Assets/OD/Rebuild Resource Registry before entering play mode or building.");

                public static ulong CatalogID => Instance.catalogId;

                public static global::GMCore.ODResourceID GetId(global::UnityEngine.Object? value)
                {
                    if (value == null)
                        return default;
                    Instance.EnsureMaps();
                    return Instance.objectIds!.TryGetValue(value, out global::GMCore.ODResourceID id) ? id :
                        throw new global::System.InvalidOperationException($"Unity resource '{value.name}' is not registered.");
                }

                public static T? Resolve<T>(global::GMCore.ODResourceID id) where T : global::UnityEngine.Object
                {
                    if (!id.IsValid)
                        return null;
                    Instance.EnsureMaps();
                    if (!Instance.idObjects!.TryGetValue(id, out global::UnityEngine.Object? value))
                        throw new global::System.IO.InvalidDataException($"OD resource '{id}' is missing from the Unity resource registry.");
                    return value as T ?? throw new global::System.IO.InvalidDataException(
                        $"OD resource '{id}' is '{value.GetType()}', expected '{typeof(T)}'.");
                }

                public static uint GetNetworkId(global::UnityEngine.Object? value)
                {
                    if (value == null)
                        return 0;
                    Instance.EnsureMaps();
                    return Instance.networkIds!.TryGetValue(value, out uint id) ? id :
                        throw new global::System.InvalidOperationException($"Unity resource '{value.name}' is not registered.");
                }

                public static T? ResolveNetwork<T>(uint id) where T : global::UnityEngine.Object
                {
                    if (id == 0)
                        return null;
                    Instance.EnsureMaps();
                    if (id > Instance.objects.Count)
                        throw new global::System.IO.InvalidDataException($"OD network resource index '{id}' is outside the resource catalog.");
                    global::UnityEngine.Object value = Instance.objects[checked((int)id - 1)];
                    return value as T ?? throw new global::System.IO.InvalidDataException(
                        $"OD network resource index '{id}' is '{value.GetType()}', expected '{typeof(T)}'.");
                }

                public static void Write(global::GMCore.GameplaySaveWriter writer, global::UnityEngine.Object? value)
                {
                    if (writer.IsNetwork)
                        writer.WriteVarUInt32(GetNetworkId(value));
                    else
                        writer.WriteResourceID(GetId(value));
                }

                public static T? Read<T>(global::GMCore.GameplaySaveReader reader) where T : global::UnityEngine.Object
                {
                    return reader.IsNetwork
                        ? ResolveNetwork<T>(reader.ReadVarUInt32())
                        : Resolve<T>(reader.ReadResourceID());
                }

                public void ReplaceEntries(global::System.Collections.Generic.List<global::UnityEngine.Object> newObjects,
                    global::System.Collections.Generic.List<string> newIds, ulong newCatalogId)
                {
                    objects = newObjects;
                    ids = newIds;
                    catalogId = newCatalogId;
                    objectIds = null;
                    idObjects = null;
                    networkIds = null;
                    instance = this;
                }

                private void EnsureMaps()
                {
                    if (objectIds != null)
                        return;
                    if (objects.Count != ids.Count)
                        throw new global::System.IO.InvalidDataException("OD Unity resource registry object and ID counts do not match.");
                    objectIds = new();
                    idObjects = new();
                    networkIds = new();
                    for (int index = 0; index < objects.Count; index++)
                    {
                        if (objects[index] == null || !global::GMCore.ODResourceID.TryParse(ids[index], out global::GMCore.ODResourceID id))
                            throw new global::System.IO.InvalidDataException($"OD Unity resource registry entry {index} is invalid.");
                        if (!idObjects.TryAdd(id, objects[index]))
                            throw new global::System.IO.InvalidDataException($"OD Unity resource registry contains duplicate ID '{id}'.");
                        objectIds[objects[index]] = id;
                        networkIds[objects[index]] = checked((uint)index + 1);
                    }
                }
            }
        }
        """;

    public static string EmitEditorSupport(OdProject project, string resourceCatalogSignature)
    {
        var writer = new CodeWriter();
        writer.Line("// <auto-generated />");
        writer.Line("#nullable enable");
        writer.Line("#if UNITY_EDITOR");
        using (writer.Block("namespace ODStudio.Unity.Editor"))
        {
            using (writer.Block("public static class ODUnityResourceEditor"))
            {
                writer.Line("[global::UnityEditor.MenuItem(\"Assets/OD/Rebuild Resource Registry\", false, 1)]");
                using (writer.Block("public static void RebuildResourceRegistry()"))
                {
                    writer.Line("MaterializeResources();");
                    writer.Line("const string folder = \"Assets/Resources/GameplayMachine\";");
                    writer.Line("const string path = folder + \"/ODUnityResourceRegistry.asset\";");
                    using (writer.Block("if (!global::UnityEditor.AssetDatabase.IsValidFolder(\"Assets/Resources\"))"))
                        writer.Line("global::UnityEditor.AssetDatabase.CreateFolder(\"Assets\", \"Resources\");");
                    using (writer.Block("if (!global::UnityEditor.AssetDatabase.IsValidFolder(folder))"))
                        writer.Line("global::UnityEditor.AssetDatabase.CreateFolder(\"Assets/Resources\", \"GameplayMachine\");");
                    writer.Line("var registry = global::UnityEditor.AssetDatabase.LoadAssetAtPath<global::ODStudio.Unity.Runtime.ODUnityResourceRegistry>(path);");
                    using (writer.Block("if (registry == null)"))
                    {
                        writer.Line("if (global::UnityEditor.AssetDatabase.LoadMainAssetAtPath(path) != null) global::UnityEditor.AssetDatabase.DeleteAsset(path);");
                        writer.Line("registry = global::UnityEngine.ScriptableObject.CreateInstance<global::ODStudio.Unity.Runtime.ODUnityResourceRegistry>();");
                        writer.Line("global::UnityEditor.AssetDatabase.CreateAsset(registry, path);");
                    }
                    writer.Line("var usedIds = new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.Ordinal);");
                    writer.Line("var entries = new global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<string, global::UnityEngine.Object>>();");
                    foreach (var item in ResourceRecords(project))
                    {
                        string path = ResourceAssetPath(item.Folder, item.Record);
                        string stableId = item.Record.Id.ToString("N");
                        writer.Line($"global::UnityEngine.Object asset_{stableId} = global::UnityEditor.AssetDatabase.LoadAssetAtPath<global::UnityEngine.Object>({OdGenerationContext.StringLiteral(path)});");
                        using (writer.Block($"if (asset_{stableId} is not global::ODStudio.Unity.Runtime.IODUnityResourceIdentity identity_{stableId})"))
                        {
                            writer.Line($"throw new global::System.IO.InvalidDataException({OdGenerationContext.StringLiteral($"Generated OD resource asset '{path}' is missing or invalid.")});");
                        }
                        writer.Line($"if (!usedIds.Add({OdGenerationContext.StringLiteral(stableId)})) throw new global::System.IO.InvalidDataException({OdGenerationContext.StringLiteral($"Duplicate generated OD resource ID '{stableId}'.")});");
                        writer.Line($"identity_{stableId}.SetODResourceID({OdGenerationContext.StringLiteral(stableId)});");
                        writer.Line($"global::UnityEditor.EditorUtility.SetDirty(asset_{stableId});");
                        writer.Line($"entries.Add(new global::System.Collections.Generic.KeyValuePair<string, global::UnityEngine.Object>({OdGenerationContext.StringLiteral(stableId)}, asset_{stableId}));");
                    }
                    writer.Line("entries.Sort((left, right) => global::System.StringComparer.Ordinal.Compare(left.Key, right.Key));");
                    writer.Line("var objects = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Select(entries, entry => entry.Value));");
                    writer.Line("var ids = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Select(entries, entry => entry.Key));");
                    writer.Line($"ulong catalogId = global::GMCore.ODSaveHash.Compute({OdGenerationContext.StringLiteral(resourceCatalogSignature)});");
                    writer.Line("registry.ReplaceEntries(objects, ids, catalogId);");
                    writer.Line("global::UnityEditor.EditorUtility.SetDirty(registry);");
                    writer.Line("global::UnityEditor.AssetDatabase.SaveAssets();");
                }
                EmitResourceMaterializer(writer, project);
                writer.Line("[global::UnityEditor.InitializeOnLoadMethod]");
                using (writer.Block("private static void RebuildRegistryAfterScriptReload()"))
                    writer.Line("global::UnityEditor.EditorApplication.delayCall += RebuildResourceRegistry;");
            }
            using (writer.Block("public sealed class ODUnityRegistryBuildProcessor : global::UnityEditor.Build.IPreprocessBuildWithReport"))
            {
                writer.Line("public int callbackOrder => 0;");
                writer.Line("public void OnPreprocessBuild(global::UnityEditor.Build.Reporting.BuildReport report) => ODUnityResourceEditor.RebuildResourceRegistry();");
            }
            using (writer.Block("public sealed class ODUnityResourceRegistryPostprocessor : global::UnityEditor.AssetPostprocessor"))
            {
                writer.Line("private const string RegistryPath = \"Assets/Resources/GameplayMachine/ODUnityResourceRegistry.asset\";");
                writer.Line("private static bool scheduled;");
                using (writer.Block("private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)"))
                {
                    writer.Line("if (!TouchesResourceAsset(importedAssets) && !TouchesResourceAsset(deletedAssets) && !TouchesResourceAsset(movedAssets) && !TouchesResourceAsset(movedFromAssetPaths)) return;");
                    writer.Line("if (scheduled) return;");
                    writer.Line("scheduled = true;");
                    writer.Line("global::UnityEditor.EditorApplication.delayCall += RebuildDelayed;");
                }
                using (writer.Block("private static bool TouchesResourceAsset(string[] paths)"))
                {
                    using (writer.Block("foreach (string path in paths)"))
                    {
                        writer.Line("if (path.StartsWith(\"Assets/ODGeneratedResources/\", global::System.StringComparison.OrdinalIgnoreCase)) continue;");
                        writer.Line("if (path.StartsWith(\"Assets/ODGeneratedScenes/\", global::System.StringComparison.OrdinalIgnoreCase)) continue;");
                        writer.Line("if (!string.Equals(path, RegistryPath, global::System.StringComparison.Ordinal) && (path.EndsWith(\".asset\", global::System.StringComparison.OrdinalIgnoreCase) || path.EndsWith(\".prefab\", global::System.StringComparison.OrdinalIgnoreCase))) return true;");
                    }
                    writer.Line("return false;");
                }
                using (writer.Block("private static void RebuildDelayed()"))
                {
                    writer.Line("scheduled = false;");
                    writer.Line("ODUnityResourceEditor.RebuildResourceRegistry();");
                }
            }
        }
        writer.Line("#endif");
        return writer.ToString();
    }

    private static void EmitResourceMaterializer(CodeWriter writer, OdProject project)
    {
        var mapper = new UnityTypeMapper(project);
        var options = new OdCSharpGenerationOptions
        {
            TypeNameOverride = mapper.DefinitionTypeName,
            ClassResourceTypeName = mapper.ClassResourceTypeName,
            IsReferenceType = definition => definition is OdResourceDefinition,
            ResourceReferenceExpression = (definition, id) =>
                $"ResolveGeneratedResource<{(definition is OdClassDefinition classDefinition ? mapper.ClassResourceTypeName(classDefinition) : mapper.DefinitionTypeName(definition) ?? $"global::{OdGenerationContext.NamespaceIdentifier(definition.Namespace)}.{OdGenerationContext.Identifier(definition.Name)}")}>(\"{id:N}\")!",
        };
        var issues = new List<OdIssue>();
        var records = project.DataSets
            .SelectMany(folder => folder.Records.Select(record => (Folder: folder, Record: record,
                Definition: project.Definitions.FirstOrDefault(definition => definition.Id == folder.DefinitionId))))
            .Where(item => item.Definition is OdClassDefinition or OdResourceDefinition)
            .OrderBy(item => item.Folder.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Record.Name, StringComparer.Ordinal)
            .ToList();
        var scenes = project.Scenes
            .Select(scene => (Scene: scene, Scope: project.FindOd(scene)))
            .Where(item => item.Scope is not null)
            .OrderBy(item => item.Scope!.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Scene.Name, StringComparer.Ordinal)
            .ToList();

        using (writer.Block("private static void MaterializeResources()"))
        {
            writer.Line("const string root = \"Assets/ODGeneratedResources\";");
            writer.Line("EnsureFolder(root);");
            foreach (var item in records)
            {
                string typeName = ResourceTypeName(item.Definition!, mapper);
                string path = ResourceAssetPath(item.Folder, item.Record);
                writer.Line($"EnsureResourceAsset<{typeName}>({OdGenerationContext.StringLiteral(path)}, {OdGenerationContext.StringLiteral(item.Record.Id.ToString("N"))});");
            }
            foreach (var item in scenes)
            {
                string sceneName = OdGenerationContext.SafeIdentifier(item.Scene.Name);
                string sceneType = $"global::{OdGenerationContext.NamespaceIdentifier(item.Scope!.Namespace)}.{sceneName}SceneAsset";
                writer.Line($"EnsureSceneAsset<{sceneType}>({OdGenerationContext.StringLiteral(SceneAssetPath(item.Scope, item.Scene))});");
            }

            foreach (IGrouping<OdScope?, (OdDataSet Folder, OdDataRecord Record, OdDefinition? Definition)> group in
                     records.GroupBy(item => project.FindOd(item.Definition!)))
            {
                if (group.Key is null)
                    continue;
                var context = new OdGenerationContext(project, group.Key, issues, options);
                foreach (var item in group)
                    EmitResourceAssignments(writer, context, mapper, item.Folder, item.Record, item.Definition!);
            }
            writer.Line("global::UnityEditor.AssetDatabase.SaveAssets();");
        }

        using (writer.Block("private static T EnsureResourceAsset<T>(string path, string stableId) where T : global::UnityEngine.ScriptableObject, global::ODStudio.Unity.Runtime.IODUnityResourceIdentity"))
        {
            writer.Line("EnsureFolder(global::System.IO.Path.GetDirectoryName(path)!.Replace('\\\\', '/'));");
            writer.Line("T asset = global::UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);");
            using (writer.Block("if (asset == null)"))
            {
                writer.Line("if (global::UnityEditor.AssetDatabase.LoadMainAssetAtPath(path) != null) global::UnityEditor.AssetDatabase.DeleteAsset(path);");
                writer.Line("asset = global::UnityEngine.ScriptableObject.CreateInstance<T>();");
                writer.Line("global::UnityEditor.AssetDatabase.CreateAsset(asset, path);");
            }
            writer.Line("asset.SetODResourceID(stableId);");
            writer.Line("global::UnityEditor.EditorUtility.SetDirty(asset);");
            writer.Line("return asset;");
        }

        using (writer.Block("private static T EnsureSceneAsset<T>(string path) where T : global::ODStudio.Unity.Runtime.ODGameplaySceneAsset"))
        {
            writer.Line("EnsureFolder(global::System.IO.Path.GetDirectoryName(path)!.Replace('\\\\', '/'));");
            writer.Line("T asset = global::UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);");
            using (writer.Block("if (asset == null)"))
            {
                writer.Line("if (global::UnityEditor.AssetDatabase.LoadMainAssetAtPath(path) != null) global::UnityEditor.AssetDatabase.DeleteAsset(path);");
                writer.Line("asset = global::UnityEngine.ScriptableObject.CreateInstance<T>();");
                writer.Line("global::UnityEditor.AssetDatabase.CreateAsset(asset, path);");
            }
            writer.Line("return asset;");
        }

        using (writer.Block("private static T? ResolveGeneratedResource<T>(string stableId) where T : global::UnityEngine.Object"))
        {
            using (writer.Block("foreach (string guid in global::UnityEditor.AssetDatabase.FindAssets(\"t:ScriptableObject\", new[] { \"Assets/ODGeneratedResources\" }))"))
            {
                writer.Line("T asset = global::UnityEditor.AssetDatabase.LoadAssetAtPath<T>(global::UnityEditor.AssetDatabase.GUIDToAssetPath(guid));");
                writer.Line("if (asset is global::ODStudio.Unity.Runtime.IODUnityResourceIdentity identity && global::System.StringComparer.Ordinal.Equals(identity.ODResourceID, stableId)) return asset;");
            }
            writer.Line("return null;");
        }

        using (writer.Block("private static void EnsureFolder(string path)"))
        {
            writer.Line("path = path.Replace('\\\\', '/').TrimEnd('/');");
            writer.Line("if (global::UnityEditor.AssetDatabase.IsValidFolder(path)) return;");
            writer.Line("string parent = global::System.IO.Path.GetDirectoryName(path)!.Replace('\\\\', '/');");
            writer.Line("EnsureFolder(parent);");
            writer.Line("global::UnityEditor.AssetDatabase.CreateFolder(parent, global::System.IO.Path.GetFileName(path));");
        }
    }

    private static IEnumerable<(OdDataSet Folder, OdDataRecord Record, OdDefinition Definition)> ResourceRecords(OdProject project)
    {
        Dictionary<Guid, OdDefinition> definitions = project.Definitions
            .Where(definition => definition is OdClassDefinition or OdResourceDefinition)
            .ToDictionary(definition => definition.Id);
        return project.DataSets
            .Where(folder => definitions.ContainsKey(folder.DefinitionId))
            .SelectMany(folder => folder.Records.Select(record => (Folder: folder, Record: record,
                Definition: definitions[folder.DefinitionId])))
            .OrderBy(item => item.Record.Id)
            .ToList();
    }

    private static void EmitResourceAssignments(CodeWriter writer, OdGenerationContext context, UnityTypeMapper mapper,
        OdDataSet folder, OdDataRecord record, OdDefinition definition)
    {
        string variable = "resource_" + record.Id.ToString("N");
        string typeName = ResourceTypeName(definition, mapper);
        writer.Line($"var {variable} = global::UnityEditor.AssetDatabase.LoadAssetAtPath<{typeName}>({OdGenerationContext.StringLiteral(ResourceAssetPath(folder, record))});");
        IEnumerable<OdFieldDefinition> fields = definition switch
        {
            OdClassDefinition classDefinition => context.AllFields(classDefinition).Select(item => item.Field),
            OdResourceDefinition resourceDefinition => resourceDefinition.Fields,
            _ => Array.Empty<OdFieldDefinition>(),
        };
        foreach (OdFieldDefinition field in fields.Where(field => field.Mode != OdFieldMode.Driven && !mapper.IsLiveClassReference(field)))
        {
            JsonNode? value = record.Values.TryGetValue(field.Id, out JsonNode? configured)
                ? configured
                : field.DefaultValue;
            EmitUnityFieldAssignment(writer, context, mapper, variable, field, value, record.Id);
        }
        writer.Line($"global::UnityEditor.EditorUtility.SetDirty({variable});");
    }

    private static void EmitUnityFieldAssignment(CodeWriter writer, OdGenerationContext context, UnityTypeMapper mapper,
        string variable, OdFieldDefinition field, JsonNode? value, Guid targetId)
    {
        string member = $"{variable}.{OdGenerationContext.Identifier(field.Name)}";
        if (field.Type.Container == OdContainerKind.Single)
        {
            writer.Line($"{member} = {(value is null ? context.DefaultExpression(field) : context.JsonLiteral(field.Type, value, targetId))};");
            return;
        }

        var elementType = new OdTypeReference
        {
            BuiltIn = field.Type.BuiltIn,
            DefinitionId = field.Type.DefinitionId,
            IsResourceClass = field.Type.IsResourceClass,
        };
        if (field.Type.Container is OdContainerKind.List or OdContainerKind.Set)
        {
            JsonArray array = value as JsonArray ?? new JsonArray();
            string elements = string.Join(", ", array.Select(item => context.JsonLiteral(elementType, item, targetId)));
            string elementName = context.TypeName(elementType, includeNullable: false);
            if (field.Type.Container == OdContainerKind.List)
                writer.Line($"{member} = new global::System.Collections.Generic.List<{elementName}> {{ {elements} }};");
            else
                writer.Line($"{member}.ReplaceWith(new {elementName}[] {{ {elements} }});");
            return;
        }

        var keyType = new OdTypeReference
        {
            BuiltIn = field.Type.DictionaryKeyBuiltIn,
            DefinitionId = field.Type.DictionaryKeyDefinitionId,
            IsResourceClass = field.Type.DictionaryKeyIsResourceClass,
        };
        JsonObject dictionary = value as JsonObject ?? new JsonObject();
        string keyName = context.TypeName(keyType, includeNullable: false);
        string valueName = context.TypeName(elementType, includeNullable: false);
        string entries = string.Join(", ", dictionary.Select(item =>
            $"new global::System.Collections.Generic.KeyValuePair<{keyName}, {valueName}>({DictionaryKeyLiteral(context, keyType, item.Key, targetId)}, {context.JsonLiteral(elementType, item.Value, targetId)})"));
        writer.Line($"{member}.ReplaceWith(new global::System.Collections.Generic.KeyValuePair<{keyName}, {valueName}>[] {{ {entries} }});");
    }

    private static string DictionaryKeyLiteral(OdGenerationContext context, OdTypeReference type, string value,
        Guid targetId)
    {
        if (type.DefinitionId is { } definitionId && context.Definitions.TryGetValue(definitionId, out OdDefinition? definition))
        {
            if (definition is OdEnumDefinition enumDefinition)
            {
                OdEnumMember? member = enumDefinition.Members.FirstOrDefault(item => item.Name == value);
                if (member is not null)
                    return $"{context.FullName(enumDefinition)}.{OdGenerationContext.Identifier(member.Name)}";
            }
            if (Guid.TryParse(value, out Guid resourceId))
                return context.ResourceReferenceExpression(definition, resourceId);
        }
        JsonNode node = type.BuiltIn switch
        {
            OdBuiltInType.Int32 => JsonValue.Create(int.Parse(value, CultureInfo.InvariantCulture)),
            OdBuiltInType.Int64 => JsonValue.Create(long.Parse(value, CultureInfo.InvariantCulture)),
            _ => JsonValue.Create(value),
        };
        return context.JsonLiteral(type, node, targetId);
    }

    private static string ResourceTypeName(OdDefinition definition, UnityTypeMapper mapper) =>
        definition is OdClassDefinition classDefinition
            ? mapper.ClassResourceTypeName(classDefinition)
            : mapper.DefinitionTypeName(definition) ??
              $"global::{OdGenerationContext.NamespaceIdentifier(definition.Namespace)}.{OdGenerationContext.Identifier(definition.Name)}";

    private static string ResourceAssetPath(OdDataSet folder, OdDataRecord record) =>
        $"Assets/ODGeneratedResources/{OdGenerationContext.SafeIdentifier(folder.Name)}/{OdGenerationContext.SafeIdentifier(record.Name)}.asset";

    private static string SceneAssetPath(OdScope scope, OdScene scene) =>
        $"Assets/ODGeneratedScenes/{OdGenerationContext.SafeIdentifier(scope.Name)}/{OdGenerationContext.SafeIdentifier(scene.Name)}.asset";

    public static string EmitSwitchStructDrawers(OdProject project)
    {
        var writer = new CodeWriter();
        writer.Line("// <auto-generated />");
        writer.Line("#if UNITY_EDITOR");
        using (writer.Block("namespace ODStudio.Unity.Editor"))
        {
            foreach (OdSwitchStructDefinition definition in project.Definitions.OfType<OdSwitchStructDefinition>()
                         .OrderBy(item => item.Namespace).ThenBy(item => item.Name))
            {
                string typeName = $"global::{OdGenerationContext.NamespaceIdentifier(definition.Namespace)}.{OdGenerationContext.Identifier(definition.Name)}";
                writer.Line($"[global::UnityEditor.CustomPropertyDrawer(typeof({typeName}))]");
                using (writer.Block($"public sealed class SwitchDrawer_{definition.Id:N} : global::UnityEditor.PropertyDrawer"))
                {
                    using (writer.Block("public override void OnGUI(global::UnityEngine.Rect position, global::UnityEditor.SerializedProperty property, global::UnityEngine.GUIContent label)"))
                    {
                        writer.Line("global::UnityEditor.EditorGUI.BeginProperty(position, label, property);");
                        writer.Line("var discriminator = property.FindPropertyRelative(\"__switchType\");");
                        writer.Line("var first = new global::UnityEngine.Rect(position.x, position.y, position.width, global::UnityEditor.EditorGUIUtility.singleLineHeight);");
                        writer.Line("global::UnityEditor.EditorGUI.PropertyField(first, discriminator, label);");
                        using (writer.Block("switch (discriminator.enumValueIndex)"))
                        {
                            for (int index = 0; index < definition.Fields.Count; index++)
                            {
                                string backing = $"__value_{definition.Fields[index].Id:N}";
                                writer.Line($"case {index}: global::UnityEditor.EditorGUI.PropertyField(new global::UnityEngine.Rect(position.x, first.yMax + 2, position.width, global::UnityEditor.EditorGUI.GetPropertyHeight(property.FindPropertyRelative(\"{backing}\"), true)), property.FindPropertyRelative(\"{backing}\"), new global::UnityEngine.GUIContent({OdGenerationContext.StringLiteral(definition.Fields[index].Name)}), true); break;");
                            }
                        }
                        writer.Line("global::UnityEditor.EditorGUI.EndProperty();");
                    }
                    using (writer.Block("public override float GetPropertyHeight(global::UnityEditor.SerializedProperty property, global::UnityEngine.GUIContent label)"))
                    {
                        writer.Line("var discriminator = property.FindPropertyRelative(\"__switchType\");");
                        using (writer.Block("switch (discriminator.enumValueIndex)"))
                        {
                            for (int index = 0; index < definition.Fields.Count; index++)
                            {
                                string backing = $"__value_{definition.Fields[index].Id:N}";
                                writer.Line($"case {index}: return global::UnityEditor.EditorGUIUtility.singleLineHeight + 2 + global::UnityEditor.EditorGUI.GetPropertyHeight(property.FindPropertyRelative(\"{backing}\"), true);");
                            }
                        }
                        writer.Line("return global::UnityEditor.EditorGUIUtility.singleLineHeight;");
                    }
                }
            }
        }
        writer.Line("#endif");
        return writer.ToString();
    }

    public static string EmitMeta(string guid) => $$"""
        fileFormatVersion: 2
        guid: {{guid}}
        MonoImporter:
          externalObjects: {}
          serializedVersion: 2
          defaultReferences: []
          executionOrder: 0
          icon: {instanceID: 0}
          userData:
          assetBundleName:
          assetBundleVariant:
        """ + "\n";

    private static void Header(CodeWriter writer)
    {
        writer.Line("// <auto-generated />");
        writer.Line("#nullable enable");
        writer.Line();
    }

    private static void UserHeader(CodeWriter writer)
    {
        writer.Line("#nullable enable");
        writer.Line("// This file is created once and is never overwritten by GameplayMachine Editor.");
        writer.Line();
    }
}
