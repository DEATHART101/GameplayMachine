#nullable enable

using GMCore;

namespace ODStudio.Godot.Runtime;

public sealed class GodotGameplayLogger : GameplayLogger
{
    public GodotGameplayLogger() : base(new GameplayLoggerOptions
    {
        DirectoryPath = global::Godot.ProjectSettings.GlobalizePath("user://logs"),
        FilePrefix = "gameplaymachine-godot",
        ConsoleMinimumImportance = GameplayLogImportance.Important,
        ConsoleOutput = static (importance, content) =>
        {
            if (importance >= GameplayLogImportance.Error)
                global::Godot.GD.PushError(content);
            else if (importance == GameplayLogImportance.Warning)
                global::Godot.GD.PushWarning(content);
            else
                global::Godot.GD.Print(content);
        },
    }) { }
}
