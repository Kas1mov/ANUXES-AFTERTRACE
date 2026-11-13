namespace ANUXES.Aftertrace.Models;

/// <summary>One file-level difference between two snapshots.</summary>
public sealed class FileChange
{
    public FileChangeType Type { get; init; }
    public string? OldPath { get; init; }
    public string? NewPath { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public long? OldSizeBytes { get; init; }
    public long? NewSizeBytes { get; init; }
    public bool IsAmbiguousMatch { get; init; }
    public string? Note { get; init; }
}
