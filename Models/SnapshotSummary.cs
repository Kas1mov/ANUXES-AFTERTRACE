namespace ANUXES.Aftertrace.Models;

/// <summary>Lightweight information about a saved snapshot.</summary>
public sealed class SnapshotSummary
{
    public Guid Id { get; init; }
    public string RootPath { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public int FileCount { get; init; }
    public int SkippedCount { get; init; }
    public long TotalSizeBytes { get; init; }
}
