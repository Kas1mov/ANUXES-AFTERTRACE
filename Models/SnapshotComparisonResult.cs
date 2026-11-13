namespace ANUXES.Aftertrace.Models;

/// <summary>Deterministic comparison output for two snapshots.</summary>
public sealed class SnapshotComparisonResult
{
    public DateTime ComparedAtUtc { get; init; } = DateTime.UtcNow;
    public string RootPath { get; init; } = string.Empty;
    public Guid PreviousSnapshotId { get; init; }
    public Guid NewSnapshotId { get; init; }
    public bool IsPartial { get; init; }
    public List<string> Warnings { get; init; } = [];
    public List<FileChange> Changes { get; init; } = [];
    public ChangeSummary Summary { get; init; } = new();
}
