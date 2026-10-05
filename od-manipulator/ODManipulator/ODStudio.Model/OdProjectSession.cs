namespace ODStudio.Model;

public sealed class OdProjectSession
{
    private readonly Stack<SnapshotCommand> _undo = new();
    private readonly Stack<SnapshotCommand> _redo = new();

    public OdProjectSession(OdProject project)
    {
        Project = project;
    }

    public OdProject Project { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event Action? ProjectChanged;

    public void Execute(OdChangeKind kind, Guid? targetId, string targetType, string summary, Action<OdProject> action,
        IReadOnlyDictionary<string, string?>? details = null)
    {
        var before = OdJson.Serialize(Project);
        action(Project);
        Project.ModifiedUtc = DateTime.UtcNow;
        AppendHistory(Project, kind, targetId, targetType, summary, details);
        var after = OdJson.Serialize(Project);
        _undo.Push(new SnapshotCommand(before, after, targetId, targetType, summary));
        _redo.Clear();
        ProjectChanged?.Invoke();
    }

    public bool Undo()
    {
        if (!_undo.TryPop(out var command))
            return false;

        var currentHistory = Project.History;
        Project = OdJson.Deserialize<OdProject>(command.Before);
        Project.History = currentHistory;
        AppendHistory(Project, OdChangeKind.Undo, command.TargetId, command.TargetType, $"Undo: {command.Summary}");
        _redo.Push(command);
        ProjectChanged?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (!_redo.TryPop(out var command))
            return false;

        var currentHistory = Project.History;
        Project = OdJson.Deserialize<OdProject>(command.After);
        Project.History = currentHistory;
        AppendHistory(Project, OdChangeKind.Redo, command.TargetId, command.TargetType, $"Redo: {command.Summary}");
        _undo.Push(command);
        ProjectChanged?.Invoke();
        return true;
    }

    private static void AppendHistory(OdProject project, OdChangeKind kind, Guid? targetId, string targetType,
        string summary, IReadOnlyDictionary<string, string?>? details = null)
    {
        var record = new OdChangeRecord
        {
            Sequence = project.History.Count == 0 ? 1 : project.History.Max(item => item.Sequence) + 1,
            Kind = kind,
            TargetId = targetId,
            TargetType = targetType,
            Summary = summary,
        };
        if (details is not null)
            foreach (var detail in details)
                record.Details[detail.Key] = detail.Value;
        project.History.Add(record);
        project.ModifiedUtc = DateTime.UtcNow;
    }

    private sealed record SnapshotCommand(string Before, string After, Guid? TargetId, string TargetType, string Summary);
}
