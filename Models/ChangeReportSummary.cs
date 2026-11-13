namespace ANUXES.Aftertrace.Models;

/// <summary>Lightweight item used by the report history list.</summary>
public sealed class ChangeReportSummary
{
    public Guid Id { get; init; }
    public DateTime CheckDateUtc { get; init; }
    public string RootPath { get; init; } = string.Empty;
    public Guid? PreviousSnapshotId { get; init; }
    public Guid NewSnapshotId { get; init; }
    public bool IsBaseline { get; init; }
    public bool IsPartial { get; init; }
    public int ChangeCount { get; init; }
    public int WarningCount { get; init; }
}
