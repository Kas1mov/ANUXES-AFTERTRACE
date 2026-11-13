namespace ANUXES.Aftertrace.Models;

/// <summary>Metadata and SHA-256 hash captured for one file.</summary>
public sealed class SnapshotFileEntry
{
    public string RelativePath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }
    public int Attributes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}
