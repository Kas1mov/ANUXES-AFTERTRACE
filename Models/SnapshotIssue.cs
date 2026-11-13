namespace ANUXES.Aftertrace.Models;

/// <summary>Describes a file or directory that could not be captured.</summary>
public sealed class SnapshotIssue
{
    public string RelativePath { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
