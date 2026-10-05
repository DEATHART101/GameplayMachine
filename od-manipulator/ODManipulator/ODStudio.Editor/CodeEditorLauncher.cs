using System.Diagnostics;

namespace ODStudio.Editor;

internal static class CodeEditorLauncher
{
    public static void Open(string workspacePath, string filePath, int line = 1)
    {
        workspacePath = Path.GetFullPath(workspacePath);
        filePath = Path.GetFullPath(filePath);
        string? executable = FindVisualStudioCode();
        if (executable is null)
            throw new FileNotFoundException("Visual Studio Code was not found. Install VS Code or add the 'code' command to PATH.");

        Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = true,
            ArgumentList = { "--reuse-window", workspacePath, "--goto", $"{filePath}:{Math.Max(1, line)}" },
        });
    }

    private static string? FindVisualStudioCode()
    {
        string? localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string? programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        foreach (string candidate in new[]
                 {
                     Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe"),
                     Path.Combine(programFiles, "Microsoft VS Code", "Code.exe"),
                     Path.Combine(programFilesX86, "Microsoft VS Code", "Code.exe"),
                 })
            if (File.Exists(candidate))
                return candidate;

        return "code";
    }
}
