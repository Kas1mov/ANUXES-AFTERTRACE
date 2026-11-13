using System.Text.Json.Serialization;

namespace ANUXES.Aftertrace.Models;

/// <summary>A point-in-time inventory of a selected directory tree.</summary>
public sealed class Snapshot
{
    public int SchemaVersion { get; init; } = 1;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string RootPath { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public List<SnapshotFileEntry> Files { get; init; } = [];
    public List<SnapshotIssue> Issues { get; init; } = [];

    [JsonIgnore]
    public long TotalSizeBytes => Files.Sum(file => file.SizeBytes);
}
