using System.IO;
using System.Security;
using System.Text.Json;
using ANUXES.Aftertrace.Models;

namespace ANUXES.Aftertrace.Services;

/// <summary>Stores check reports separately from the immutable snapshot archive.</summary>
public sealed class ChangeReportService
{
    private const int ReportSchemaVersion = 1;
    private const int BufferSize = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _storageDirectory;

    public ChangeReportService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ANUXES", "AFTERTRACE", "Reports"))
    {
    }

    public ChangeReportService(string storageDirectory)
    {
        if (string.IsNullOrWhiteSpace(storageDirectory))
            throw new ArgumentException("A report storage directory is required.", nameof(storageDirectory));

        _storageDirectory = Path.GetFullPath(storageDirectory);
    }

    public async Task<ChangeReport> SaveReportAsync(ChangeReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Id == Guid.Empty || report.NewSnapshotId == Guid.Empty || string.IsNullOrWhiteSpace(report.RootPath))
            throw new InvalidDataException("The report is missing required data.");
        if (report.SchemaVersion != ReportSchemaVersion)
            throw new InvalidDataException($"Unsupported report schema version: {report.SchemaVersion}.");

        Directory.CreateDirectory(_storageDirectory);
        string destinationPath = GetReportPath(report.Id);
        string temporaryPath = destinationPath + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, report, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, destinationPath, overwrite: false);
            return report;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public async Task<ChangeReport> LoadReportAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        if (reportId == Guid.Empty)
            throw new ArgumentException("A valid report ID is required.", nameof(reportId));

        await using var stream = new FileStream(GetReportPath(reportId), FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        ChangeReport? report = await JsonSerializer.DeserializeAsync<ChangeReport>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (report is null || report.Id != reportId || report.SchemaVersion != ReportSchemaVersion
            || report.Summary is null || report.Changes is null || report.Warnings is null)
            throw new InvalidDataException("The report file is empty, incomplete, or its ID does not match its filename.");
        return report;
    }

    public async Task<IReadOnlyList<ChangeReportSummary>> ListReportsAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_storageDirectory))
            return Array.Empty<ChangeReportSummary>();

        var reports = new List<ChangeReportSummary>();
        foreach (string filePath in Directory.EnumerateFiles(_storageDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(filePath), "N", out Guid reportId))
                continue;

            try
            {
                ChangeReport report = await LoadReportAsync(reportId, cancellationToken).ConfigureAwait(false);
                reports.Add(new ChangeReportSummary
                {
                    Id = report.Id,
                    CheckDateUtc = report.CheckDateUtc,
                    RootPath = report.RootPath,
                    PreviousSnapshotId = report.PreviousSnapshotId,
                    NewSnapshotId = report.NewSnapshotId,
                    IsBaseline = report.IsBaseline,
                    IsPartial = report.IsPartial,
                    ChangeCount = report.Summary.Total,
                    WarningCount = report.Warnings.Count
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or SecurityException or InvalidDataException)
            {
                // One invalid report must not hide the rest of the history.
            }
        }

        return reports.OrderByDescending(item => item.CheckDateUtc).ToArray();
    }

    public async Task ExportReportAsync(ChangeReport report, string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("An export path is required.", nameof(destinationPath));

        string? directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var stream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await JsonSerializer.SerializeAsync(stream, report, JsonOptions, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private string GetReportPath(Guid reportId) => Path.Combine(_storageDirectory, $"{reportId:N}.json");
}
