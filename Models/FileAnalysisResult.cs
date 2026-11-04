namespace ANUXES.Aftertrace.Models;

public sealed class FileAnalysisResult
{
    public DateTime AnalyzedAtUtc { get; init; } = DateTime.UtcNow;
    public string FileName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public string DetectedType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public string SignatureStatus { get; init; } = string.Empty;
    public string Assessment { get; init; } = string.Empty;
    public List<Finding> Findings { get; init; } = [];
}
