using System.Security.Cryptography;
using System.Text;
using ODStudio.Model;

namespace ODStudio.Package;

public sealed record OdPackageBuildResult(string OutputPath, Guid PackageId, long Size, IReadOnlyList<OdIssue> Issues);

public static class OdPackageCompiler
{
    private static readonly byte[] Magic = "ODPK"u8.ToArray();
    private const ushort MinimumReadablePackageFormatVersion = 25;
    private const ushort PackageFormatVersion = 37;
    private const int EntrySize = 24;
    private const int HeaderSize = 4 + 2 + 2 + 16 + 4;
    private const int HashSize = 32;

    public static async Task<OdPackageBuildResult> BuildAsync(OdProject project, string outputPath,
        CancellationToken cancellationToken = default)
    {
        project.EnsureOdScopes();
        if (!string.IsNullOrWhiteSpace(project.SourceDirectory))
            OdProjectStore.RefreshCodeFilesFromDisk(project.SourceDirectory, project);
        var fullOutputPath = Path.GetFullPath(outputPath);
        OdPackageGraph graph = await OdPackageGraph.LoadForProjectAsync(project, cancellationToken);
        var issues = graph.Issues.Concat(OdProjectValidator.Validate(project,
                graph.Projects.Skip(1).SelectMany(item => item.Definitions)))
            .Distinct().ToList();
        if (issues.Any(issue => issue.Severity == OdIssueSeverity.Error))
            return new(outputPath, project.Id, 0, issues);

        List<OdPackageReference> packageReferences = NormalizePackageReferences(
            project.PackageReferences, project.SourceDirectory, Path.GetDirectoryName(fullOutputPath)!);

        var chunks = new[]
        {
            new Chunk("MANF", OdBinaryModelCodec.WriteManifest(project)),
            new Chunk("ODSC", OdBinaryModelCodec.WriteScopes(project.Ods)),
            new Chunk("SCHM", OdBinaryModelCodec.WriteDefinitions(project.Definitions)),
            new Chunk("DATA", OdBinaryModelCodec.WriteDataSets(project.DataSets)),
            new Chunk("SCNE", OdBinaryModelCodec.WriteScenes(project.Scenes)),
            new Chunk("DEPS", OdBinaryModelCodec.WritePackageReferences(packageReferences)),
            new Chunk("CODE", OdBinaryModelCodec.WriteCodeFiles(project.CodeFiles)),
        };

        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        var tempPath = fullOutputPath + ".tmp";
        await using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, true))
        {
            using var writer = new BinaryWriter(file, Encoding.UTF8, true);
            writer.Write(Magic);
            writer.Write(PackageFormatVersion);
            writer.Write((ushort)0);
            writer.Write(project.Id.ToByteArray());
            writer.Write(chunks.Length);

            var offset = HeaderSize + chunks.Length * EntrySize;
            foreach (var chunk in chunks)
            {
                writer.Write(Encoding.ASCII.GetBytes(chunk.Id));
                writer.Write((long)offset);
                writer.Write(chunk.Data.Length);
                writer.Write(Crc32.Compute(chunk.Data));
                writer.Write((byte)0);
                writer.Write(new byte[3]);
                offset += chunk.Data.Length;
            }
            foreach (var chunk in chunks)
                writer.Write(chunk.Data);
            writer.Flush();

            file.Position = 0;
            var hash = await SHA256.HashDataAsync(file, cancellationToken);
            file.Position = file.Length;
            await file.WriteAsync(hash, cancellationToken);
        }
        File.Move(tempPath, fullOutputPath, true);
        return new(fullOutputPath, project.Id, new FileInfo(fullOutputPath).Length, issues);
    }

    public static async Task<OdProject> LoadAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(packagePath, cancellationToken);
        if (bytes.Length < HeaderSize + HashSize)
            throw new InvalidDataException("OD package is truncated.");

        var content = bytes[..^HashSize];
        var expectedHash = bytes[^HashSize..];
        var actualHash = SHA256.HashData(content);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
            throw new InvalidDataException("OD package SHA-256 check failed.");

        using var reader = new BinaryReader(new MemoryStream(content, false), Encoding.UTF8, false);
        if (!reader.ReadBytes(4).SequenceEqual(Magic))
            throw new InvalidDataException("Not an OD package.");
        var version = reader.ReadUInt16();
        if (version is < MinimumReadablePackageFormatVersion or > PackageFormatVersion)
            throw new InvalidDataException($"Unsupported OD package format {version}.");
        _ = reader.ReadUInt16();
        var packageId = new Guid(reader.ReadBytes(16));
        var chunkCount = reader.ReadInt32();
        if (chunkCount is < 0 or > 1024)
            throw new InvalidDataException("Invalid OD package chunk count.");

        var chunks = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (var index = 0; index < chunkCount; index++)
        {
            var id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var offset = reader.ReadInt64();
            var length = reader.ReadInt32();
            var crc = reader.ReadUInt32();
            var compression = reader.ReadByte();
            _ = reader.ReadBytes(3);
            if (compression != 0 || offset < HeaderSize || length < 0 || offset + length > content.Length)
                throw new InvalidDataException($"Invalid chunk '{id}'.");
            var data = content.AsSpan((int)offset, length).ToArray();
            if (Crc32.Compute(data) != crc)
                throw new InvalidDataException($"Chunk '{id}' CRC check failed.");
            chunks.Add(id, data);
        }

        foreach (var required in new[] { "MANF", "ODSC", "SCHM", "DATA" })
            if (!chunks.ContainsKey(required))
                throw new InvalidDataException($"OD package does not contain required chunk '{required}'.");

        var project = new OdProject { Id = packageId };
        OdBinaryModelCodec.ReadManifest(chunks["MANF"], project, version >= 29, version >= 30,
            version >= 33, version >= 35, version >= 36, version >= 37);
        if (project.Id != packageId)
            throw new InvalidDataException("OD package ID does not match its manifest.");
        project.Ods = OdBinaryModelCodec.ReadScopes(chunks["ODSC"]);
        project.Definitions = OdBinaryModelCodec.ReadDefinitions(chunks["SCHM"], version >= 14, version >= 15,
            version <= 15, version >= 16, version >= 18, version >= 21, version >= 23, version >= 24,
            version >= 26, version >= 27, version >= 31, version >= 32, version >= 34);
        OdNetworkPackage.MigrateLegacyDefinitions(project.Definitions, project.Runtime.NetworkMode);
        project.DataSets = OdBinaryModelCodec.ReadDataSets(chunks["DATA"]);
        if (chunks.TryGetValue("SCNE", out byte[]? scenes))
            project.Scenes = OdBinaryModelCodec.ReadScenes(scenes, version is >= 20 and < 29);
        if (chunks.TryGetValue("DEPS", out byte[]? dependencies))
            project.PackageReferences = OdBinaryModelCodec.ReadPackageReferences(dependencies);
        if (chunks.TryGetValue("CODE", out byte[]? code))
            project.CodeFiles = OdBinaryModelCodec.ReadCodeFiles(code);
        project.SourceDirectory = Path.GetDirectoryName(Path.GetFullPath(packagePath))!;
        return project;
    }

    private static List<OdPackageReference> NormalizePackageReferences(
        IEnumerable<OdPackageReference> references, string sourceDirectory, string outputDirectory)
    {
        string baseDirectory = string.IsNullOrWhiteSpace(sourceDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(sourceDirectory);
        return references.Select(packageReference =>
        {
            string absolutePath = Path.IsPathRooted(packageReference.PackagePath)
                ? Path.GetFullPath(packageReference.PackagePath)
                : Path.GetFullPath(Path.Combine(baseDirectory, packageReference.PackagePath));
            return new OdPackageReference
            {
                Id = packageReference.Id,
                Name = packageReference.Name,
                Description = packageReference.Description,
                PackageId = packageReference.PackageId,
                PackagePath = Path.GetRelativePath(outputDirectory, absolutePath),
            };
        }).ToList();
    }

    private sealed record Chunk(string Id, byte[] Data);
}

internal static class Crc32
{
    private static readonly uint[] Table = CreateTable();

    public static uint Compute(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
            crc = Table[(crc ^ value) & 0xff] ^ (crc >> 8);
        return ~crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xedb88320u ^ (value >> 1) : value >> 1;
            table[index] = value;
        }
        return table;
    }
}
