using System.Security.Cryptography;
using System.Text;

namespace ODStudio.Package;

public readonly record struct OdProjectDiskSnapshot(string Fingerprint)
{
    public static OdProjectDiskSnapshot Capture(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        string root = Path.GetFullPath(projectDirectory);
        if (!Directory.Exists(root))
            return new OdProjectDiskSnapshot(string.Empty);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string path in EnumerateProjectFiles(root))
        {
            string relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
            hash.AppendData(Encoding.UTF8.GetBytes(relativePath));
            hash.AppendData(new byte[] { 0 });
            var file = new FileInfo(path);
            hash.AppendData(BitConverter.GetBytes(file.Length));
            hash.AppendData(BitConverter.GetBytes(file.LastWriteTimeUtc.Ticks));
            hash.AppendData(new byte[] { 0 });
        }
        return new OdProjectDiskSnapshot(Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static IEnumerable<string> EnumerateProjectFiles(string root)
    {
        string projectFile = Path.Combine(root, OdProjectStore.ProjectFileName);
        if (File.Exists(projectFile))
            yield return projectFile;

        foreach (string directoryName in new[] { "ods", "schemas", "data", "scenes" })
        {
            string directory = Path.Combine(root, directoryName);
            if (!Directory.Exists(directory))
                continue;
            foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
                         .OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
                yield return path;
        }

        string historyPath = Path.Combine(root, "history", "changes.jsonl");
        if (File.Exists(historyPath))
            yield return historyPath;

        string codeDirectory = Path.Combine(root, "code");
        if (!Directory.Exists(codeDirectory))
            yield break;
        foreach (string path in Directory.EnumerateFiles(codeDirectory, "*", SearchOption.AllDirectories)
                     .Where(IsProjectCodeFile)
                     .OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            yield return path;
    }

    private static bool IsProjectCodeFile(string path)
    {
        string normalized = path.Replace('\\', '/');
        if (normalized.Contains("/.gameplaymachine-editor/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/.odstudio/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            return false;
        string fileName = Path.GetFileName(path);
        return path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, ".gameplaymachine-code.json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, ".odstudio-code.json", StringComparison.OrdinalIgnoreCase);
    }
}
