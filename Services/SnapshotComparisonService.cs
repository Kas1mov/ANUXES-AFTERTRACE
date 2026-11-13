using System.IO;
using ANUXES.Aftertrace.Models;

namespace ANUXES.Aftertrace.Services;

/// <summary>Compares snapshot inventories by relative path first and SHA-256 second.</summary>
public sealed class SnapshotComparisonService
{
    public SnapshotComparisonResult Compare(Snapshot previous, Snapshot current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var warnings = new List<string>();
        bool partial = previous.Issues.Count > 0 || current.Issues.Count > 0;
        AddSnapshotWarnings(warnings, "Previous snapshot", previous.Issues);
        AddSnapshotWarnings(warnings, "New snapshot", current.Issues);

        if (!PathsEqual(previous.RootPath, current.RootPath))
        {
            partial = true;
            warnings.Add($"The snapshots have different root paths: '{previous.RootPath}' and '{current.RootPath}'.");
        }

        var oldFiles = previous.Files
            .OrderBy(file => NormalizeRelativePath(file.RelativePath), StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToList();
        var newFiles = current.Files
            .OrderBy(file => NormalizeRelativePath(file.RelativePath), StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToList();

        var oldByPath = oldFiles.ToDictionary(file => NormalizeRelativePath(file.RelativePath), StringComparer.OrdinalIgnoreCase);
        var newByPath = newFiles.ToDictionary(file => NormalizeRelativePath(file.RelativePath), StringComparer.OrdinalIgnoreCase);
        var matchedOld = new HashSet<SnapshotFileEntry>();
        var matchedNew = new HashSet<SnapshotFileEntry>();
        var changes = new List<FileChange>();

        // Stable paths have priority over hash matches. A changed file at the same path is MODIFIED.
        foreach (string path in oldByPath.Keys.Intersect(newByPath.Keys, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            SnapshotFileEntry oldFile = oldByPath[path];
            SnapshotFileEntry newFile = newByPath[path];
            matchedOld.Add(oldFile);
            matchedNew.Add(newFile);

            if (!HashesEqual(oldFile.Sha256, newFile.Sha256))
            {
                changes.Add(new FileChange
                {
                    Type = FileChangeType.Modified,
                    OldPath = oldFile.RelativePath,
                    NewPath = newFile.RelativePath,
                    Sha256 = newFile.Sha256,
                    OldSizeBytes = oldFile.SizeBytes,
                    NewSizeBytes = newFile.SizeBytes
                });
            }
        }

        var oldRemaining = oldFiles.Where(file => !matchedOld.Contains(file)).ToList();
        var newRemaining = newFiles.Where(file => !matchedNew.Contains(file)).ToList();
        var oldByHash = oldRemaining.GroupBy(file => NormalizeHash(file.Sha256), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(file => NormalizeRelativePath(file.RelativePath), StringComparer.OrdinalIgnoreCase)
                .ThenBy(file => file.RelativePath, StringComparer.Ordinal).ToList(), StringComparer.OrdinalIgnoreCase);
        var newByHash = newRemaining.GroupBy(file => NormalizeHash(file.Sha256), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(file => NormalizeRelativePath(file.RelativePath), StringComparer.OrdinalIgnoreCase)
                .ThenBy(file => file.RelativePath, StringComparer.Ordinal).ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (string hash in newByHash.Keys.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            List<SnapshotFileEntry> newCandidates = newByHash[hash];
            if (!oldByHash.TryGetValue(hash, out List<SnapshotFileEntry>? oldCandidates))
            {
                foreach (SnapshotFileEntry newFile in newCandidates)
                {
                    matchedNew.Add(newFile);
                    changes.Add(new FileChange
                    {
                        Type = FileChangeType.Added,
                        NewPath = newFile.RelativePath,
                        Sha256 = newFile.Sha256,
                        NewSizeBytes = newFile.SizeBytes
                    });
                }

                continue;
            }

            int pairCount = Math.Min(oldCandidates.Count, newCandidates.Count);
            bool ambiguous = oldCandidates.Count > 1 || newCandidates.Count > 1;
            if (ambiguous)
            {
                warnings.Add($"SHA-256 {hash}: {oldCandidates.Count} previous and {newCandidates.Count} new files had the same hash. Stable path order was used for matching.");
            }

            for (int index = 0; index < pairCount; index++)
            {
                SnapshotFileEntry oldFile = oldCandidates[index];
                SnapshotFileEntry newFile = newCandidates[index];
                matchedOld.Add(oldFile);
                matchedNew.Add(newFile);

                FileChangeType type = SameDirectory(oldFile.RelativePath, newFile.RelativePath)
                    ? FileChangeType.Renamed
                    : FileChangeType.Moved;
                changes.Add(new FileChange
                {
                    Type = type,
                    OldPath = oldFile.RelativePath,
                    NewPath = newFile.RelativePath,
                    Sha256 = newFile.Sha256,
                    OldSizeBytes = oldFile.SizeBytes,
                    NewSizeBytes = newFile.SizeBytes,
                    IsAmbiguousMatch = ambiguous,
                    Note = ambiguous ? "Matched by SHA-256 using stable relative-path order." : null
                });
            }
        }

        foreach (SnapshotFileEntry newFile in newRemaining.Where(file => !matchedNew.Contains(file)))
        {
            changes.Add(new FileChange
            {
                Type = FileChangeType.Added,
                NewPath = newFile.RelativePath,
                Sha256 = newFile.Sha256,
                NewSizeBytes = newFile.SizeBytes
            });
        }

        foreach (SnapshotFileEntry oldFile in oldRemaining.Where(file => !matchedOld.Contains(file)))
        {
            if (IsCoveredByIssue(oldFile.RelativePath, current.Issues))
            {
                partial = true;
                warnings.Add($"Deletion suppressed for '{oldFile.RelativePath}' because the new snapshot could not fully inspect that path.");
                continue;
            }

            changes.Add(new FileChange
            {
                Type = FileChangeType.Deleted,
                OldPath = oldFile.RelativePath,
                Sha256 = oldFile.Sha256,
                OldSizeBytes = oldFile.SizeBytes
            });
        }

        changes.Sort((left, right) =>
        {
            int typeOrder = left.Type.CompareTo(right.Type);
            if (typeOrder != 0) return typeOrder;
            int pathOrder = StringComparer.OrdinalIgnoreCase.Compare(left.NewPath ?? left.OldPath, right.NewPath ?? right.OldPath);
            return pathOrder != 0 ? pathOrder : StringComparer.Ordinal.Compare(left.NewPath ?? left.OldPath, right.NewPath ?? right.OldPath);
        });

        return new SnapshotComparisonResult
        {
            RootPath = current.RootPath,
            PreviousSnapshotId = previous.Id,
            NewSnapshotId = current.Id,
            IsPartial = partial,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToList(),
            Changes = changes,
            Summary = CreateSummary(changes)
        };
    }

    private static ChangeSummary CreateSummary(IEnumerable<FileChange> changes)
    {
        var counts = changes.GroupBy(change => change.Type).ToDictionary(group => group.Key, group => group.Count());
        return new ChangeSummary
        {
            Added = counts.GetValueOrDefault(FileChangeType.Added),
            Modified = counts.GetValueOrDefault(FileChangeType.Modified),
            Deleted = counts.GetValueOrDefault(FileChangeType.Deleted),
            Moved = counts.GetValueOrDefault(FileChangeType.Moved),
            Renamed = counts.GetValueOrDefault(FileChangeType.Renamed)
        };
    }

    private static void AddSnapshotWarnings(List<string> warnings, string label, IEnumerable<SnapshotIssue> issues)
    {
        foreach (SnapshotIssue issue in issues)
            warnings.Add($"{label}: {issue.RelativePath} [{issue.Code}] {issue.Message}");
    }

    private static bool IsCoveredByIssue(string relativePath, IEnumerable<SnapshotIssue> issues)
    {
        string normalizedPath = NormalizeRelativePath(relativePath);
        foreach (SnapshotIssue issue in issues)
        {
            string issuePath = NormalizeRelativePath(issue.RelativePath);
            if (issuePath.Length == 0 || issuePath == "." || normalizedPath.Equals(issuePath, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(issuePath + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool SameDirectory(string left, string right)
    {
        string leftDirectory = GetDirectory(NormalizeRelativePath(left));
        string rightDirectory = GetDirectory(NormalizeRelativePath(right));
        return leftDirectory.Equals(rightDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDirectory(string path)
    {
        int separator = path.LastIndexOf('/');
        return separator < 0 ? string.Empty : path[..separator];
    }

    private static string NormalizeRelativePath(string path) =>
        (path ?? string.Empty).Replace('\\', '/').TrimStart('/');

    private static string NormalizeHash(string hash) => (hash ?? string.Empty).Trim();

    private static bool HashesEqual(string left, string right) =>
        !string.IsNullOrWhiteSpace(left) && left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
