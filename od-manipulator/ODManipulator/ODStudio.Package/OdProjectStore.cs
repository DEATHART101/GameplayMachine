using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ODStudio.Model;

namespace ODStudio.Package;

public static class OdProjectStore
{
    public const string ProjectFileName = "project.json";
    private const string CodeManifestFileName = ".gameplaymachine-code.json";
    private const string LegacyCodeManifestFileName = ".odstudio-code.json";
    private static readonly JsonSerializerOptions CompactJsonOptions = new(OdJson.Options) { WriteIndented = false };

    public static Task SaveAsync(string projectDirectory, OdProject project, CancellationToken cancellationToken = default) =>
        SaveCoreAsync(projectDirectory, project, refreshCodeFromDisk: true, overwriteCodeFiles: false,
            cancellationToken);

    public static Task OverwriteAsync(string projectDirectory, OdProject project,
        CancellationToken cancellationToken = default) =>
        SaveCoreAsync(projectDirectory, project, refreshCodeFromDisk: false, overwriteCodeFiles: true,
            cancellationToken);

    private static async Task SaveCoreAsync(string projectDirectory, OdProject project,
        bool refreshCodeFromDisk, bool overwriteCodeFiles, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        projectDirectory = Path.GetFullPath(projectDirectory);
        Directory.CreateDirectory(projectDirectory);
        project.SourceDirectory = projectDirectory;
        project.EnsureOdScopes();

        var odDirectory = Directory.CreateDirectory(Path.Combine(projectDirectory, "ods")).FullName;
        var schemaDirectory = Directory.CreateDirectory(Path.Combine(projectDirectory, "schemas")).FullName;
        var dataDirectory = Directory.CreateDirectory(Path.Combine(projectDirectory, "data")).FullName;
        var sceneDirectory = Directory.CreateDirectory(Path.Combine(projectDirectory, "scenes")).FullName;
        var historyDirectory = Directory.CreateDirectory(Path.Combine(projectDirectory, "history")).FullName;
        var editorDirectory = Directory.CreateDirectory(Path.Combine(projectDirectory, "editor")).FullName;
        var codeDirectory = Directory.CreateDirectory(Path.Combine(projectDirectory, "code")).FullName;

        if (refreshCodeFromDisk)
            RefreshCodeFilesFromDisk(projectDirectory, project);

        var manifest = new OdProjectManifest
        {
            FormatVersion = project.FormatVersion,
            Id = project.Id,
            Name = project.Name,
            DefaultNamespace = project.DefaultNamespace,
            Description = project.Description,
            CreatedUtc = project.CreatedUtc,
            ModifiedUtc = project.ModifiedUtc,
            RootClassId = project.RootClassId,
            InitialSceneId = project.InitialSceneId,
            Runtime = project.Runtime,
            PackageReferences = project.PackageReferences,
        };
        await WriteJsonAtomicAsync(Path.Combine(projectDirectory, ProjectFileName), manifest, cancellationToken);

        await SaveEntitiesAsync(odDirectory, project.Ods, od => FileName(od.Id, od.Name), cancellationToken);
        await SaveEntitiesAsync(schemaDirectory, project.Definitions, definition => FileName(definition.Id, definition.Name), cancellationToken);
        await SaveEntitiesAsync(dataDirectory, project.DataSets, data => FileName(data.Id, data.Name), cancellationToken);
        await SaveEntitiesAsync(sceneDirectory, project.Scenes, scene => FileName(scene.Id, scene.Name), cancellationToken);
        await WriteJsonAtomicAsync(Path.Combine(editorDirectory, "state.json"), project.EditorState, cancellationToken);
        await SaveCodeFilesCoreAsync(codeDirectory, project.CodeFiles, overwriteCodeFiles,
            removeUnexpectedFiles: overwriteCodeFiles, cancellationToken);

        var historyPath = Path.Combine(historyDirectory, "changes.jsonl");
        var historyText = string.Join(Environment.NewLine,
            project.History.OrderBy(item => item.Sequence).Select(item => JsonSerializer.Serialize(item, CompactJsonOptions)));
        if (historyText.Length > 0)
            historyText += Environment.NewLine;
        await WriteTextAtomicAsync(historyPath, historyText, cancellationToken);
    }

    public static async Task<OdProject> LoadAsync(string projectDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        projectDirectory = Path.GetFullPath(projectDirectory);
        var manifestPath = Path.Combine(projectDirectory, ProjectFileName);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("The directory is not an OD project.", manifestPath);

        var manifest = OdJson.Deserialize<OdProjectManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken));
        if (manifest.FormatVersion != OdProject.CurrentFormatVersion)
            throw new InvalidDataException($"Unsupported OD project format {manifest.FormatVersion}.");

        var project = new OdProject
        {
            FormatVersion = manifest.FormatVersion,
            Id = manifest.Id,
            Name = manifest.Name,
            DefaultNamespace = manifest.DefaultNamespace,
            Description = manifest.Description,
            CreatedUtc = manifest.CreatedUtc,
            ModifiedUtc = manifest.ModifiedUtc,
            RootClassId = manifest.RootClassId,
            InitialSceneId = manifest.InitialSceneId,
            Runtime = manifest.Runtime ?? new OdRuntimeSettings(),
            PackageReferences = manifest.PackageReferences,
            SourceDirectory = projectDirectory,
            Ods = await LoadEntitiesAsync<OdScope>(Path.Combine(projectDirectory, "ods"), cancellationToken),
            Definitions = await LoadEntitiesAsync<OdDefinition>(Path.Combine(projectDirectory, "schemas"), cancellationToken),
            DataSets = await LoadEntitiesAsync<OdDataSet>(Path.Combine(projectDirectory, "data"), cancellationToken),
            Scenes = await LoadEntitiesAsync<OdScene>(Path.Combine(projectDirectory, "scenes"), cancellationToken),
            CodeFiles = await LoadCodeFilesAsync(Path.Combine(projectDirectory, "code"), cancellationToken),
        };
        OdNetworkPackage.MigrateLegacyDefinitions(project.Definitions, project.Runtime.NetworkMode);

        var editorStatePath = Path.Combine(projectDirectory, "editor", "state.json");
        if (File.Exists(editorStatePath))
            project.EditorState = OdJson.Deserialize<OdEditorState>(await File.ReadAllTextAsync(editorStatePath, cancellationToken));

        var historyPath = Path.Combine(projectDirectory, "history", "changes.jsonl");
        if (File.Exists(historyPath))
        {
            foreach (var line in await File.ReadAllLinesAsync(historyPath, cancellationToken))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    project.History.Add(OdJson.Deserialize<OdChangeRecord>(line));
            }
        }
        project.EnsureOdScopes();
        return project;
    }

    public static Task SaveEditorStateAsync(string projectDirectory, OdEditorState editorState,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        projectDirectory = Path.GetFullPath(projectDirectory);
        var editorDirectory = Directory.CreateDirectory(Path.Combine(projectDirectory, "editor")).FullName;
        return WriteJsonAtomicAsync(Path.Combine(editorDirectory, "state.json"), editorState, cancellationToken);
    }

    public static void RefreshCodeFilesFromDisk(string projectDirectory, OdProject project)
    {
        string codeDirectory = Path.Combine(Path.GetFullPath(projectDirectory), "code");
        if (!Directory.Exists(codeDirectory))
            return;

        var existing = project.CodeFiles.ToDictionary(
            item => NormalizeCodePath(item.RelativePath), StringComparer.OrdinalIgnoreCase);
        var persistedPaths = LoadCodeManifestPaths(codeDirectory);
        var refreshed = new List<OdCodeFile>();
        foreach (string path in Directory.EnumerateFiles(codeDirectory, "*.cs", SearchOption.AllDirectories)
                     .OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            string relativePath = NormalizeCodePath(Path.GetRelativePath(codeDirectory, path));
            if (IsWorkspaceInfrastructurePath(relativePath))
                continue;
            if (!existing.TryGetValue(relativePath, out OdCodeFile? file))
                file = new OdCodeFile { RelativePath = relativePath, Kind = OdCodeFileKind.Other };
            file.Content = File.ReadAllText(path);
            refreshed.Add(file);
        }

        // Newly scaffolded files have no manifest entry until their first save.
        refreshed.AddRange(project.CodeFiles.Where(item => !refreshed.Any(existingFile =>
            string.Equals(existingFile.RelativePath, item.RelativePath, StringComparison.OrdinalIgnoreCase)) &&
            !persistedPaths.Contains(NormalizeCodePath(item.RelativePath))));
        project.CodeFiles = refreshed;
    }

    public static async Task SaveCodeFilesAsync(string codeDirectory, IReadOnlyCollection<OdCodeFile> files,
        CancellationToken cancellationToken = default) =>
        await SaveCodeFilesCoreAsync(codeDirectory, files, overwriteExistingFiles: false,
            removeUnexpectedFiles: false, cancellationToken);

    private static async Task SaveCodeFilesCoreAsync(string codeDirectory, IReadOnlyCollection<OdCodeFile> files,
        bool overwriteExistingFiles, bool removeUnexpectedFiles, CancellationToken cancellationToken)
    {
        codeDirectory = Path.GetFullPath(codeDirectory);
        Directory.CreateDirectory(codeDirectory);
        var expectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (OdCodeFile file in files)
        {
            string relativePath = NormalizeCodePath(file.RelativePath);
            if (!relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"OD code file '{relativePath}' must use the .cs extension.");
            string path = Path.GetFullPath(Path.Combine(codeDirectory, relativePath));
            if (!path.StartsWith(codeDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"OD code file '{relativePath}' escapes the code directory.");
            expectedPaths.Add(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (overwriteExistingFiles || !File.Exists(path))
                await WriteTextAtomicAsync(path, file.Content, cancellationToken);
        }
        if (removeUnexpectedFiles)
        {
            foreach (string path in Directory.EnumerateFiles(codeDirectory, "*.cs", SearchOption.AllDirectories))
            {
                string relativePath = NormalizeCodePath(Path.GetRelativePath(codeDirectory, path));
                if (!IsWorkspaceInfrastructurePath(relativePath) && !expectedPaths.Contains(Path.GetFullPath(path)))
                    File.Delete(path);
            }
        }
        await WriteJsonAtomicAsync(Path.Combine(codeDirectory, CodeManifestFileName),
            files.Select(file => new OdCodeFileManifest
            {
                Id = file.Id,
                OwnerId = file.OwnerId,
                Kind = file.Kind,
                RelativePath = NormalizeCodePath(file.RelativePath),
            }).ToList(), cancellationToken);
    }

    private static async Task<List<OdCodeFile>> LoadCodeFilesAsync(string codeDirectory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(codeDirectory))
            return new List<OdCodeFile>();
        var metadata = new Dictionary<string, OdCodeFileManifest>(StringComparer.OrdinalIgnoreCase);
        string manifestPath = Path.Combine(codeDirectory, CodeManifestFileName);
        if (!File.Exists(manifestPath))
            manifestPath = Path.Combine(codeDirectory, LegacyCodeManifestFileName);
        if (File.Exists(manifestPath))
        {
            foreach (OdCodeFileManifest item in OdJson.Deserialize<List<OdCodeFileManifest>>(
                         await File.ReadAllTextAsync(manifestPath, cancellationToken)))
                metadata[NormalizeCodePath(item.RelativePath)] = item;
        }
        var result = new List<OdCodeFile>();
        foreach (string path in Directory.EnumerateFiles(codeDirectory, "*.cs", SearchOption.AllDirectories)
                     .OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            string relativePath = NormalizeCodePath(Path.GetRelativePath(codeDirectory, path));
            if (IsWorkspaceInfrastructurePath(relativePath))
                continue;
            metadata.TryGetValue(relativePath, out OdCodeFileManifest? item);
            result.Add(new OdCodeFile
            {
                Id = item?.Id ?? Guid.NewGuid(),
                OwnerId = item?.OwnerId,
                Kind = item?.Kind ?? OdCodeFileKind.Other,
                RelativePath = relativePath,
                Content = await File.ReadAllTextAsync(path, cancellationToken),
            });
        }
        return result;
    }

    private static string NormalizeCodePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static bool IsWorkspaceInfrastructurePath(string relativePath) =>
        relativePath.StartsWith(".gameplaymachine-editor/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith(".odstudio/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> LoadCodeManifestPaths(string codeDirectory)
    {
        string manifestPath = Path.Combine(codeDirectory, CodeManifestFileName);
        if (!File.Exists(manifestPath))
            manifestPath = Path.Combine(codeDirectory, LegacyCodeManifestFileName);
        if (!File.Exists(manifestPath))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return OdJson.Deserialize<List<OdCodeFileManifest>>(File.ReadAllText(manifestPath))
            .Select(item => NormalizeCodePath(item.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static OdProject Create(string name, string? defaultNamespace = null)
    {
        var safeName = string.IsNullOrWhiteSpace(name) ? "NewODProject" : name.Trim();
        var project = new OdProject
        {
            Name = safeName,
            DefaultNamespace = string.IsNullOrWhiteSpace(defaultNamespace) ? $"{safeName}.OD" : defaultNamespace.Trim(),
        };
        project.EnsureOdScopes();
        return project;
    }

    private static async Task SaveEntitiesAsync<T>(string directory, IEnumerable<T> entities,
        Func<T, string> fileName, CancellationToken cancellationToken)
    {
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entities)
        {
            var path = Path.Combine(directory, fileName(entity));
            expected.Add(Path.GetFullPath(path));
            await WriteJsonAtomicAsync(path, entity, cancellationToken);
        }

        foreach (var existing in Directory.EnumerateFiles(directory, "*.json"))
            if (!expected.Contains(Path.GetFullPath(existing)))
                File.Delete(existing);
    }

    private static async Task<List<T>> LoadEntitiesAsync<T>(string directory, CancellationToken cancellationToken)
    {
        var result = new List<T>();
        if (!Directory.Exists(directory))
            return result;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").OrderBy(path => path, StringComparer.Ordinal))
            result.Add(DeserializeEntity<T>(await File.ReadAllTextAsync(path, cancellationToken)));
        return result;
    }

    private static T DeserializeEntity<T>(string json)
    {
        if (typeof(T) != typeof(OdDefinition) || JsonNode.Parse(json) is not JsonObject definition)
            return OdJson.Deserialize<T>(json);

        bool legacyPawn = string.Equals(definition["$kind"]?.GetValue<string>(), "class", StringComparison.Ordinal) &&
                          definition["isPawn"]?.GetValue<bool>() == true;
        definition.Remove("isPawn");
        MigrateLegacyTypeJson(definition["isStructOf"] as JsonObject);
        foreach (string fieldCollection in new[] { "fields", "inputs", "outputs" })
            if (definition[fieldCollection] is JsonArray fields)
                foreach (JsonNode? field in fields)
                    MigrateLegacyTypeJson(field?["type"] as JsonObject);
        if (legacyPawn)
        {
            JsonArray isClasses = definition["isClasses"] as JsonArray ?? new JsonArray();
            string pawnId = OdNetworkPackage.PawnClassId.ToString("D");
            if (!isClasses.Any(item => string.Equals(item?.GetValue<string>(), pawnId, StringComparison.OrdinalIgnoreCase)))
                isClasses.Add(pawnId);
            definition["isClasses"] = isClasses;
        }

        if (!string.Equals(definition["$kind"]?.GetValue<string>(), "struct", StringComparison.Ordinal) ||
            definition["isResource"]?.GetValue<bool>() != true)
            return (T)(object)OdJson.Deserialize<OdDefinition>(definition.ToJsonString());

        definition["$kind"] = "resource";
        definition.Remove("isResource");
        definition.Remove("externalAssetKind");
        definition.Remove("isStructOf");
        return (T)(object)OdJson.Deserialize<OdDefinition>(definition.ToJsonString());
    }

    private static void MigrateLegacyTypeJson(JsonObject? type)
    {
        if (type is null)
            return;
        string? builtIn = type["builtIn"]?.GetValue<string>();
        if (string.Equals(builtIn, "playerController", StringComparison.OrdinalIgnoreCase))
        {
            type["builtIn"] = "none";
            type["definitionId"] = OdNetworkPackage.PlayerControllerClassId.ToString("D");
        }
        else if (string.Equals(builtIn, "pawn", StringComparison.OrdinalIgnoreCase))
        {
            type["builtIn"] = "none";
            type["definitionId"] = OdNetworkPackage.PawnClassId.ToString("D");
        }

        string? keyBuiltIn = type["dictionaryKeyBuiltIn"]?.GetValue<string>();
        if (string.Equals(keyBuiltIn, "playerController", StringComparison.OrdinalIgnoreCase))
        {
            type["dictionaryKeyBuiltIn"] = "none";
            type["dictionaryKeyDefinitionId"] = OdNetworkPackage.PlayerControllerClassId.ToString("D");
        }
        else if (string.Equals(keyBuiltIn, "pawn", StringComparison.OrdinalIgnoreCase))
        {
            type["dictionaryKeyBuiltIn"] = "none";
            type["dictionaryKeyDefinitionId"] = OdNetworkPackage.PawnClassId.ToString("D");
        }
    }

    private static string FileName(Guid id, string name)
    {
        var safeName = new string((name ?? "entity").Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character).ToArray());
        return $"{safeName}.{id:N}.json";
    }

    private static Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken) =>
        WriteTextAtomicAsync(path, OdJson.Serialize(value) + Environment.NewLine, cancellationToken);

    private static async Task WriteTextAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, content, new UTF8Encoding(false), cancellationToken);
        File.Move(temp, path, true);
    }

    private sealed class OdProjectManifest
    {
        public int FormatVersion { get; set; }
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DefaultNamespace { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public DateTime ModifiedUtc { get; set; }
        public Guid? RootClassId { get; set; }
        public Guid? InitialSceneId { get; set; }
        public OdRuntimeSettings? Runtime { get; set; }
        public List<OdPackageReference> PackageReferences { get; set; } = new();
    }

    private sealed class OdCodeFileManifest
    {
        public Guid Id { get; set; }
        public Guid? OwnerId { get; set; }
        public OdCodeFileKind Kind { get; set; }
        public string RelativePath { get; set; } = string.Empty;
    }
}
