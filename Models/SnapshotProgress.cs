namespace ANUXES.Aftertrace.Models;

/// <summary>Progress information reported while a snapshot is being created.</summary>
public sealed class SnapshotProgress
{
    public string CurrentPath { get; init; } = string.Empty;
    public int FilesScanned { get; init; }
    public int FilesSkipped { get; init; }
    public long BytesHashed { get; init; }
}
