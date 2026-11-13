namespace ANUXES.Aftertrace.Models;

/// <summary>Persisted report for an automatic check or a baseline check.</summary>
public sealed class ChangeReport
{
    public int SchemaVersion { get; init; } = 1;
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime CheckDateUtc { get; init; } = DateTime.UtcNow;
    public string RootPath { get; init; } = string.Empty;
    public Guid? PreviousSnapshotId { get; init; }
    public Guid NewSnapshotId { get; init; }
    public bool IsBaseline { get; init; }
    public bool IsPartial { get; init; }
    public ChangeSummary Summary { get; init; } = new();
    public List<FileChange> Changes { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}
