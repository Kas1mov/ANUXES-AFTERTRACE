using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using ANUXES.Aftertrace.Models;

namespace ANUXES.Aftertrace.Services;

/// <summary>
/// Creates and stores point-in-time inventories of selected folders.
/// The service only reads source files. It never executes, edits, moves, or deletes them.
/// </summary>
public sealed class SnapshotService
{
    private const int SnapshotSchemaVersion = 1;
    private const int BufferSize = 128 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _storageDirectory;

    public SnapshotService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ANUXES", "AFTERTRACE", "Snapshots"))
    {
    }

    /// <summary>Allows a custom storage directory, primarily for tests.</summary>
    public SnapshotService(string storageDirectory)
    {
        if (string.IsNullOrWhiteSpace(storageDirectory))
            throw new ArgumentException("A snapshot storage directory is required.", nameof(storageDirectory));

        _storageDirectory = NormalizePath(storageDirectory);
    }

    /// <summary>Scans a folder, hashes readable files, then saves the snapshot as JSON.</summary>
    public async Task<Snapshot> CreateSnapshotAsync(
        string rootPath,
        IProgress<SnapshotProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string normalizedRoot = NormalizePath(rootPath);
        if (!Directory.Exists(normalizedRoot))
            throw new DirectoryNotFoundException($"The selected directory does not exist: {normalizedRoot}");

        FileAttributes rootAttributes = File.GetAttributes(normalizedRoot);
        if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The selected folder is a symbolic link or junction. Choose its actual target folder instead.");

        // Prevent the snapshot archive from being included in its own inventory.
        if (IsSameOrInside(normalizedRoot, _storageDirectory))
            throw new InvalidOperationException("The AFTERTRACE snapshot storage folder cannot be selected as a scan target.");

        var snapshot = new Snapshot
        {
            SchemaVersion = SnapshotSchemaVersion,
            Id = Guid.NewGuid(),
            RootPath = normalizedRoot,
            CreatedAtUtc = DateTime.UtcNow
        };

        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(normalizedRoot);
        int filesScanned = 0;
        int filesSkipped = 0;
        long bytesHashed = 0;

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string currentDirectory = pendingDirectories.Pop();

            try
            {
                // Keep enumeration inside the try: Directory.EnumerateFileSystemEntries is lazy
                // and can throw while the iterator is being consumed.
                foreach (string entryPath in Directory.EnumerateFileSystemEntries(currentDirectory))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Do not inventory AFTERTRACE's own JSON archive when scanning a parent folder/drive.
                    if (IsSameOrInside(entryPath, _storageDirectory))
                        continue;

                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entryPath);
                    }
                    catch (Exception ex) when (IsExpectedFileSystemException(ex))
                    {
                        AddIssue(snapshot, normalizedRoot, entryPath, "MetadataReadFailed", ex.Message);
                        filesSkipped++;
                        ReportProgress(progress, entryPath, filesScanned, filesSkipped, bytesHashed);
                        continue;
                    }

                    // Never follow symlinks or junctions: they may leave the chosen tree or create cycles.
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        AddIssue(snapshot, normalizedRoot, entryPath, "ReparsePointSkipped",
                            "Symbolic links and junctions are not followed by this snapshot version.");
                        filesSkipped++;
                        ReportProgress(progress, entryPath, filesScanned, filesSkipped, bytesHashed);
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pendingDirectories.Push(entryPath);
                        continue;
                    }

                    try
                    {
                        SnapshotFileEntry fileEntry = await CaptureFileAsync(
                            entryPath, normalizedRoot, attributes, cancellationToken).ConfigureAwait(false);

                        snapshot.Files.Add(fileEntry);
                        filesScanned++;
                        bytesHashed += fileEntry.SizeBytes;
                    }
                    catch (Exception ex) when (IsExpectedFileSystemException(ex))
                    {
                        AddIssue(snapshot, normalizedRoot, entryPath,
                            ex is FileChangedDuringSnapshotException ? "FileChangedDuringScan" : "FileReadFailed",
                            ex.Message);
                        filesSkipped++;
                    }

                    ReportProgress(progress, entryPath, filesScanned, filesSkipped, bytesHashed);
                }
            }
            catch (Exception ex) when (IsExpectedFileSystemException(ex))
            {
                AddIssue(snapshot, normalizedRoot, currentDirectory, "DirectoryEnumerationFailed", ex.Message);
                filesSkipped++;
                ReportProgress(progress, currentDirectory, filesScanned, filesSkipped, bytesHashed);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        snapshot.Files.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath));
        snapshot.Issues.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath));
        await SaveSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    /// <summary>Saves a snapshot atomically and never overwrites an existing snapshot.</summary>
    public async Task SaveSnapshotAsync(Snapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Id == Guid.Empty)
            throw new ArgumentException("The snapshot must have a valid ID.", nameof(snapshot));
        if (snapshot.SchemaVersion != SnapshotSchemaVersion)
            throw new InvalidDataException($"Unsupported snapshot schema version: {snapshot.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(snapshot.RootPath) || snapshot.Files is null || snapshot.Issues is null)
            throw new InvalidDataException("The snapshot is missing required data.");

        Directory.CreateDirectory(_storageDirectory);
        string destinationPath = GetSnapshotPath(snapshot.Id);
        string temporaryPath = destinationPath + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, destinationPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    /// <summary>Loads one saved snapshot by its ID.</summary>
    public async Task<Snapshot> LoadSnapshotAsync(Guid snapshotId, CancellationToken cancellationToken = default)
    {
        if (snapshotId == Guid.Empty)
            throw new ArgumentException("A valid snapshot ID is required.", nameof(snapshotId));

        string path = GetSnapshotPath(snapshotId);
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

        Snapshot? snapshot = await JsonSerializer.DeserializeAsync<Snapshot>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is null || snapshot.Id != snapshotId || snapshot.Files is null || snapshot.Issues is null
            || string.IsNullOrWhiteSpace(snapshot.RootPath))
            throw new InvalidDataException("The snapshot file is empty, incomplete, or its ID does not match its filename.");
        if (snapshot.SchemaVersion != SnapshotSchemaVersion)
            throw new InvalidDataException($"Unsupported snapshot schema version: {snapshot.SchemaVersion}.");

        return snapshot;
    }

    /// <summary>Lists valid saved snapshots, newest first.</summary>
    public async Task<IReadOnlyList<SnapshotSummary>> ListSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_storageDirectory))
            return Array.Empty<SnapshotSummary>();

        var summaries = new List<SnapshotSummary>();
        foreach (string filePath in Directory.EnumerateFiles(_storageDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(filePath), "N", out Guid fileId))
                continue;

            try
            {
                await using var stream = new FileStream(
                    filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                    BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

                Snapshot? snapshot = await JsonSerializer.DeserializeAsync<Snapshot>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);

                if (snapshot is null || snapshot.Id != fileId || snapshot.SchemaVersion != SnapshotSchemaVersion
                    || snapshot.Files is null || snapshot.Issues is null || string.IsNullOrWhiteSpace(snapshot.RootPath))
                    continue;

                summaries.Add(new SnapshotSummary
                {
                    Id = snapshot.Id,
                    RootPath = snapshot.RootPath,
                    CreatedAtUtc = snapshot.CreatedAtUtc,
                    FileCount = snapshot.Files.Count,
                    SkippedCount = snapshot.Issues.Count,
                    TotalSizeBytes = snapshot.TotalSizeBytes
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or SecurityException)
            {
                // A damaged or inaccessible snapshot must not hide the rest of the history.
            }
        }

        return summaries.OrderByDescending(item => item.CreatedAtUtc).ToArray();
    }

    private static async Task<SnapshotFileEntry> CaptureFileAsync(
        string filePath,
        string rootPath,
        FileAttributes attributes,
        CancellationToken cancellationToken)
    {
        var before = new FileInfo(filePath);
        before.Refresh();
        if (!before.Exists)
            throw new FileNotFoundException("The file disappeared before it could be captured.", filePath);

        long initialSize = before.Length;
        DateTime initialWriteTimeUtc = before.LastWriteTimeUtc;

        byte[] hash;
        await using (var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        var after = new FileInfo(filePath);
        after.Refresh();
        if (!after.Exists || after.Length != initialSize || after.LastWriteTimeUtc != initialWriteTimeUtc)
        {
            throw new FileChangedDuringSnapshotException(
                "The file changed while its hash was being calculated. Run the snapshot again to capture a stable state.");
        }

        return new SnapshotFileEntry
        {
            RelativePath = Path.GetRelativePath(rootPath, filePath),
            SizeBytes = initialSize,
            LastWriteTimeUtc = initialWriteTimeUtc,
            Attributes = (int)attributes,
            Sha256 = Convert.ToHexString(hash)
        };
    }

    private string GetSnapshotPath(Guid snapshotId) => Path.Combine(_storageDirectory, $"{snapshotId:N}.json");

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A valid folder path is required.", nameof(path));

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static bool IsSameOrInside(string path, string parentPath)
    {
        string normalizedPath = NormalizePath(path);
        string normalizedParent = NormalizePath(parentPath);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (normalizedPath.Equals(normalizedParent, comparison))
            return true;

        string parentWithSeparator = normalizedParent.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedParent
            : normalizedParent + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(parentWithSeparator, comparison);
    }

    private static void AddIssue(Snapshot snapshot, string rootPath, string fullPath, string code, string message)
    {
        string relativePath;
        try
        {
            relativePath = Path.GetRelativePath(rootPath, fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            relativePath = fullPath;
        }

        snapshot.Issues.Add(new SnapshotIssue
        {
            RelativePath = relativePath,
            Code = code,
            Message = message
        });
    }

    private static void ReportProgress(
        IProgress<SnapshotProgress>? progress,
        string currentPath,
        int filesScanned,
        int filesSkipped,
        long bytesHashed)
    {
        progress?.Report(new SnapshotProgress
        {
            CurrentPath = currentPath,
            FilesScanned = filesScanned,
            FilesSkipped = filesSkipped,
            BytesHashed = bytesHashed
        });
    }

    private static bool IsExpectedFileSystemException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;

    private sealed class FileChangedDuringSnapshotException(string message) : IOException(message)
    {
    }
}
