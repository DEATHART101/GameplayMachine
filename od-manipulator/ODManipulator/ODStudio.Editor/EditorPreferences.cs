using System.Text.Json;

namespace ODStudio.Editor;

internal sealed class EditorPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string? LastOpenProjectDirectory { get; set; }
    public string? LastBuildDirectory { get; set; }
    public bool ClearGameLogOnPlay { get; set; } = true;

    public static EditorPreferences Load()
    {
        try
        {
            string path = GetFilePath();
            if (!File.Exists(path))
                return new EditorPreferences();
            return JsonSerializer.Deserialize<EditorPreferences>(File.ReadAllText(path), JsonOptions)
                ?? new EditorPreferences();
        }
        catch (Exception)
        {
            return new EditorPreferences();
        }
    }

    public async Task SaveAsync()
    {
        string path = GetFilePath();
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temporaryPath, path, true);
    }

    private static string GetFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameplayMachineEditor",
        "editor-settings.json");
}
