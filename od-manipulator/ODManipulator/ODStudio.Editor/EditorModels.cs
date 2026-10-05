using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ODStudio.Generator;
using ODStudio.Model;

namespace ODStudio.Editor;

internal enum ExplorerAddKind
{
    Od,
    Class,
    Struct,
    Resource,
    SwitchStruct,
    Enum,
    Interface,
    PlayerInput,
    Event,
    DataSet,
}

internal sealed class ExplorerItem
{
    public required string DisplayName { get; init; }
    public required string Icon { get; init; }
    public OdEntity? Entity { get; init; }
    public ExplorerAddKind? AddKind { get; init; }
    public Guid? OwnerOdId { get; init; }
    public int Indent { get; init; }
    public bool CanEditCode { get; init; }
    public IBrush Foreground { get; init; } = Brushes.White;
    public string ToolTip { get; init; } = string.Empty;
    public ContextMenu? ContextMenu { get; init; }
    public Thickness IndentMargin => new(Indent * 12, 0, 0, 0);
    public bool CanAdd => AddKind.HasValue;
    public string AddToolTip => AddKind switch
    {
        ExplorerAddKind.SwitchStruct => "Add Switch Struct",
        ExplorerAddKind.PlayerInput => "Add Player Input",
        { } kind => $"Add {kind}",
        null => string.Empty,
    };
    public FontWeight Weight => Entity is null || Entity is OdScope ? FontWeight.SemiBold : FontWeight.Normal;
}

internal sealed record IssueItem(string Severity, string Code, string Message, IBrush Color, Guid? TargetId)
{
    public static IssueItem From(OdIssue issue) => new(issue.Severity.ToString(), issue.Code, issue.Message,
        issue.Severity switch
        {
            OdIssueSeverity.Error => Brushes.IndianRed,
            OdIssueSeverity.Warning => Brushes.Goldenrod,
            _ => Brushes.LightSteelBlue,
        }, issue.TargetId);
}

internal sealed record HistoryItem(long Sequence, string Timestamp, string Summary, Guid? TargetId)
{
    public static HistoryItem From(OdChangeRecord record) =>
        new(record.Sequence, record.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), record.Summary, record.TargetId);
}

internal sealed record GameLogItem(string Timestamp, string Stream, string Message, IBrush Color)
{
    public static GameLogItem From(RunnerOutputLine line)
    {
        bool error = line.Stream == RunnerOutputStream.Error ||
                     line.Message.Contains("[Critical]", StringComparison.OrdinalIgnoreCase) ||
                     line.Message.Contains("[Error]", StringComparison.OrdinalIgnoreCase);
        bool warning = line.Message.Contains("[Warning]", StringComparison.OrdinalIgnoreCase);
        return new GameLogItem(
            line.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff"),
            line.Stream == RunnerOutputStream.Error ? "ERROR" : "GAME",
            line.Message,
            error ? Brushes.IndianRed : warning ? Brushes.Goldenrod : Brushes.LightSteelBlue);
    }
}

internal sealed record PackageReferenceItem(
    Guid ReferenceId,
    Guid PackageId,
    string Name,
    string Path,
    string Status,
    IBrush StatusColor);

internal sealed record GameplayMachineFunctionItem(
    OdGameplayMachineFunction Function,
    string Name,
    string Signature,
    string Description,
    string Status);

internal sealed record TypeChoice(
    OdBuiltInType BuiltIn,
    Guid? DefinitionId,
    string Name,
    string Category,
    string Detail = "",
    bool CanSelectResourceClass = false,
    bool IsResourceClass = false,
    bool CanSelectDirect = true)
{
    public override string ToString() => Name;
}
