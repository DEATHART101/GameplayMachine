using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ODStudio.Generator;

public static class OdRuntimeReferences
{
    public static IReadOnlyList<string> Resolve(string directory)
    {
        var pending = new Queue<string>(new[] { "GameplayMachineCore", "ODCore" });
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        while (pending.TryDequeue(out string? name))
        {
            if (IsFramework(name) || !visited.Add(name))
                continue;
            string path = Path.Combine(directory, name + ".dll");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Gameplay runtime dependency '{name}' is missing.", path);

            // Read metadata only: do not load application or native libraries into the editor.
            using FileStream stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader metadata = pe.GetMetadataReader();
            paths.Add(path);
            foreach (AssemblyReferenceHandle handle in metadata.AssemblyReferences)
                pending.Enqueue(metadata.GetString(metadata.GetAssemblyReference(handle).Name));
        }
        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsFramework(string name) =>
        name is "mscorlib" or "netstandard" or "System" or "Microsoft.CSharp" or
            "Microsoft.VisualBasic" or "Microsoft.VisualBasic.Core" or "WindowsBase" ||
        name.StartsWith("System.", StringComparison.Ordinal) ||
        name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal);
}
