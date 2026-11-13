using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ANUXES.Aftertrace.Models;
using ANUXES.Aftertrace.Services;
using Microsoft.Win32;

namespace ANUXES.Aftertrace;

public partial class MainWindow : Window
{
    private readonly FileAnalyzer _analyzer = new();
    private readonly ObservableCollection<Finding> _findings = [];
    private readonly SnapshotService _snapshotService = new();
    private readonly SnapshotComparisonService _comparisonService = new();
    private readonly ChangeReportService _changeReportService = new();
    private readonly ObservableCollection<SnapshotSummary> _snapshotHistory = [];
    private readonly ObservableCollection<ChangeReportSummary> _reportHistory = [];
    private readonly ObservableCollection<FileChange> _visibleChanges = [];
    private readonly SemaphoreSlim _snapshotOperationGate = new(1, 1);
    private CancellationTokenSource? _snapshotCancellationSource;
    private string? _selectedSnapshotRootPath;
    private Snapshot? _loadedSnapshot;
    private string? _selectedFilePath;
    private FileAnalysisResult? _lastResult;
    private ChangeReport? _lastChangeReport;
    private int _filesAnalyzed;
    private int _filesNeedingReview;
    private readonly ScaleTransform _frameScale = new(1, 1);
    private readonly SkewTransform _frameSkew = new(0, 0);

    public MainWindow()
    {
        InitializeComponent();
        FindingsList.ItemsSource = _findings;
        SnapshotHistoryList.ItemsSource = _snapshotHistory;
        ReportHistoryList.ItemsSource = _reportHistory;
        ChangeList.ItemsSource = _visibleChanges;

        var frameTransform = new TransformGroup();
        frameTransform.Children.Add(_frameScale);
        frameTransform.Children.Add(_frameSkew);
        WindowFrame.RenderTransform = frameTransform;
        WindowFrame.RenderTransformOrigin = new Point(0.5, 0.5);

        StateChanged += MainWindow_StateChanged;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateWindowFrame();
        await RefreshSnapshotHistoryAsync(showErrors: false);
        await RefreshReportsAsync(showErrors: false);
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e) => UpdateWindowFrame();

    private void UpdateWindowFrame()
    {
        bool maximized = WindowState == WindowState.Maximized;
        Thickness frameMargin = maximized ? new Thickness(0) : new Thickness(8);
        CornerRadius frameRadius = maximized ? new CornerRadius(0) : new CornerRadius(14);
        WindowFrame.Margin = frameMargin;
        WindowFrame.CornerRadius = frameRadius;
        WindowShadow.Margin = frameMargin;
        WindowShadow.CornerRadius = frameRadius;
        WindowShadow.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;

        if (MaximizeButton is not null)
            MaximizeButton.Content = maximized ? "❐" : "□";

        UpdateWindowClip();
    }

    private void WindowRoot_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateWindowClip();

    private void UpdateWindowClip()
    {
        if (WindowRoot is null || WindowRoot.ActualWidth <= 0 || WindowRoot.ActualHeight <= 0)
            return;

        double radius = WindowState == WindowState.Maximized ? 0 : 13;
        WindowRoot.Clip = new RectangleGeometry(
            new Rect(0, 0, WindowRoot.ActualWidth, WindowRoot.ActualHeight), radius, radius);
    }

    private void AnimateWindowGrab()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _frameScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new System.Windows.Media.Animation.DoubleAnimation(1.006, TimeSpan.FromMilliseconds(130)) { EasingFunction = ease });
        _frameScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0.994, TimeSpan.FromMilliseconds(130)) { EasingFunction = ease });
        _frameSkew.BeginAnimation(SkewTransform.AngleXProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0.35, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
        _frameSkew.BeginAnimation(SkewTransform.AngleYProperty,
            new System.Windows.Media.Animation.DoubleAnimation(-0.22, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
    }

    private void AnimateWindowRelease()
    {
        var spring = new System.Windows.Media.Animation.ElasticEase
        {
            EasingMode = EasingMode.EaseOut,
            Oscillations = 2,
            Springiness = 5
        };
        _frameScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(420)) { EasingFunction = spring });
        _frameScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(420)) { EasingFunction = spring });
        _frameSkew.BeginAnimation(SkewTransform.AngleXProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(460)) { EasingFunction = spring });
        _frameSkew.BeginAnimation(SkewTransform.AngleYProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(460)) { EasingFunction = spring });
    }

    private void SelectFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a file for inspection",
            Filter = "All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
            SetSelectedFile(dialog.FileName);
    }

    private void SetSelectedFile(string path)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show(this, "The selected file could not be found.", "ANUXES AFTERTRACE", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _selectedFilePath = path;
        SelectedFileText.Text = path;
        ScannerStatusText.Text = "FILE READY · Start analysis when you are ready.";
        ScanButton.IsEnabled = true;
        ExportReportButton.IsEnabled = false;
        _lastResult = null;
        _findings.Clear();
        EmptyFindingsText.Text = "Run an inspection to see detected indicators and explanations.";
        EmptyFindingsText.Visibility = Visibility.Visible;
        ResultTypeText.Text = "—";
        ResultSizeText.Text = "—";
        ResultSignatureText.Text = "—";
        ResultHashText.Text = "—";
        AssessmentText.Text = "AWAITING FILE";
        AssessmentBadge.Background = new SolidColorBrush(Color.FromRgb(21, 26, 54));
        AssessmentBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(69, 64, 106));
    }

    private void DropArea_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropArea_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            SetSelectedFile(files[0]);
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedFilePath))
            return;

        ScanButton.IsEnabled = false;
        ExportReportButton.IsEnabled = false;
        ScannerStatusText.Text = "ANALYZING · Calculating hash and inspecting file metadata…";
        AssessmentText.Text = "ANALYZING";
        _findings.Clear();
        EmptyFindingsText.Text = "Inspection is in progress…";

        try
        {
            FileAnalysisResult result = await _analyzer.AnalyzeAsync(_selectedFilePath);
            _lastResult = result;
            _filesAnalyzed++;
            if (result.Assessment == "Needs review")
                _filesNeedingReview++;

            FilesAnalyzedText.Text = _filesAnalyzed.ToString();
            FlaggedCountText.Text = _filesNeedingReview.ToString();
            LastScanText.Text = DateTime.Now.ToString("HH:mm");
            ResultTypeText.Text = result.DetectedType;
            ResultSizeText.Text = FormatSize(result.SizeBytes);
            ResultSignatureText.Text = result.SignatureStatus;
            ResultHashText.Text = result.Sha256;
            AssessmentText.Text = result.Assessment.ToUpperInvariant();
            AssessmentBadge.BorderBrush = result.Assessment == "Needs review"
                ? new SolidColorBrush(Color.FromRgb(217, 119, 6))
                : new SolidColorBrush(Color.FromRgb(91, 65, 139));
            AssessmentBadge.Background = result.Assessment == "Needs review"
                ? new SolidColorBrush(Color.FromRgb(54, 35, 30))
                : new SolidColorBrush(Color.FromRgb(21, 26, 54));

            foreach (Finding finding in result.Findings)
                _findings.Add(finding);

            EmptyFindingsText.Visibility = Visibility.Collapsed;
            ScannerStatusText.Text = $"COMPLETE · {result.FileName} · {DateTime.Now:HH:mm:ss}";
            ExportReportButton.IsEnabled = true;
        }
        catch (UnauthorizedAccessException)
        {
            ScannerStatusText.Text = "ACCESS DENIED · The file could not be read.";
            EmptyFindingsText.Text = "Access to this file was denied. Try selecting a file you can read.";
            EmptyFindingsText.Visibility = Visibility.Visible;
            AssessmentText.Text = "INCOMPLETE";
            MessageBox.Show(this, "Windows denied access to this file. Try another file or check its permissions.",
                "Inspection could not be completed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            ScannerStatusText.Text = "ERROR · Inspection could not be completed.";
            EmptyFindingsText.Text = "No results are available because the inspection failed.";
            EmptyFindingsText.Visibility = Visibility.Visible;
            AssessmentText.Text = "ERROR";
            MessageBox.Show(this, $"The file could not be inspected.\n\n{ex.Message}",
                "ANUXES AFTERTRACE", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ScanButton.IsEnabled = !string.IsNullOrWhiteSpace(_selectedFilePath) && File.Exists(_selectedFilePath);
        }
    }

    private void SnapshotsNav_Click(object sender, RoutedEventArgs e)
    {
        SnapshotCard.BringIntoView();
    }

    private void ChooseSnapshotFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder or drive to snapshot",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FolderName))
            return;

        _selectedSnapshotRootPath = Path.GetFullPath(dialog.FolderName);
        SnapshotFolderText.Text = _selectedSnapshotRootPath;
        ChangeTrackingFolderText.Text = _selectedSnapshotRootPath;
        SnapshotStatusText.Text = "FOLDER READY · Create a snapshot to capture its current state.";
        ChangeTrackingStatusText.Text = "FOLDER READY · Run AUTO CHECK to compare it with the latest snapshot.";
        UpdateSnapshotTargetControls();
    }

    private void ChooseTrackingFolder_Click(object sender, RoutedEventArgs e)
    {
        ChooseSnapshotFolder_Click(sender, e);
    }

    private async void CreateSnapshot_Click(object sender, RoutedEventArgs e)
    {
        await RunSnapshotOperationAsync(autoCheck: false);
    }

    private async void AutoCheck_Click(object sender, RoutedEventArgs e)
    {
        await RunSnapshotOperationAsync(autoCheck: true);
    }

    private async Task RunSnapshotOperationAsync(bool autoCheck)
    {
        if (string.IsNullOrWhiteSpace(_selectedSnapshotRootPath) || !Directory.Exists(_selectedSnapshotRootPath))
            return;

        if (!await _snapshotOperationGate.WaitAsync(0))
        {
            SnapshotStatusText.Text = "BUSY · A snapshot operation is already running.";
            ChangeTrackingStatusText.Text = "BUSY · Wait for the current operation to finish or cancel it.";
            return;
        }

        try
        {
            _snapshotCancellationSource?.Dispose();
            _snapshotCancellationSource = new CancellationTokenSource();
            SetSnapshotOperationUi(running: true, autoCheck);

            if (autoCheck)
                await ExecuteAutoCheckAsync(_selectedSnapshotRootPath, _snapshotCancellationSource.Token);
            else
                await ExecuteCreateSnapshotAsync(_selectedSnapshotRootPath, _snapshotCancellationSource.Token);
        }
        catch (OperationCanceledException)
        {
            SnapshotStatusText.Text = "CANCELLED · No snapshot was saved.";
            SnapshotProgressDetailsText.Text = "The scan was cancelled by the user.";
            ChangeTrackingStatusText.Text = "CANCELLED · No new snapshot or report was saved.";
        }
        catch (Exception ex)
        {
            SnapshotStatusText.Text = "ERROR · Snapshot could not be created.";
            SnapshotProgressDetailsText.Text = ex.Message;
            ChangeTrackingStatusText.Text = $"ERROR · {ex.Message}";
            MessageBox.Show(this, $"The snapshot could not be created.\n\n{ex.Message}",
                "ANUXES AFTERTRACE", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _snapshotCancellationSource?.Dispose();
            _snapshotCancellationSource = null;
            SetSnapshotOperationUi(running: false, autoCheck);
            _snapshotOperationGate.Release();
        }
    }

    private async Task ExecuteCreateSnapshotAsync(string rootPath, CancellationToken cancellationToken)
    {
        Snapshot snapshot = await CreateSnapshotAsync(rootPath, cancellationToken);
        SnapshotStatusText.Text = $"SNAPSHOT SAVED · {snapshot.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        ChangeTrackingStatusText.Text = $"SNAPSHOT SAVED · {snapshot.Files.Count:N0} files · {snapshot.Issues.Count:N0} skipped";
    }

    private async Task ExecuteAutoCheckAsync(string rootPath, CancellationToken cancellationToken)
    {
        string normalizedRoot = NormalizeRootPath(rootPath);
        IReadOnlyList<SnapshotSummary> summaries = await _snapshotService.ListSnapshotsAsync(cancellationToken);
        SnapshotSummary? previousSummary = summaries.FirstOrDefault(summary => PathsEqual(summary.RootPath, normalizedRoot));

        Snapshot? previous = null;
        if (previousSummary is not null)
        {
            ChangeTrackingStatusText.Text = "AUTO CHECK · Loading the latest snapshot…";
            previous = await _snapshotService.LoadSnapshotAsync(previousSummary.Id, cancellationToken);
        }
        else
        {
            ChangeTrackingStatusText.Text = "AUTO CHECK · No previous snapshot found; creating a baseline…";
        }

        Snapshot current = await CreateSnapshotAsync(rootPath, cancellationToken);
        ChangeReport report;
        if (previous is null)
        {
            report = new ChangeReport
            {
                RootPath = current.RootPath,
                PreviousSnapshotId = null,
                NewSnapshotId = current.Id,
                IsBaseline = true,
                IsPartial = current.Issues.Count > 0,
                Summary = new ChangeSummary(),
                Changes = [],
                Warnings = BuildBaselineWarnings(current)
            };
            ChangeTrackingStatusText.Text = "BASELINE CREATED · The next AUTO CHECK will compare against this snapshot.";
        }
        else
        {
            SnapshotComparisonResult comparison = _comparisonService.Compare(previous, current);
            report = new ChangeReport
            {
                RootPath = comparison.RootPath,
                PreviousSnapshotId = comparison.PreviousSnapshotId,
                NewSnapshotId = comparison.NewSnapshotId,
                IsBaseline = false,
                IsPartial = comparison.IsPartial,
                Summary = comparison.Summary,
                Changes = comparison.Changes,
                Warnings = comparison.Warnings
            };
            ChangeTrackingStatusText.Text = $"CHECK COMPLETE · {comparison.Summary.Total:N0} changes" + (comparison.IsPartial ? " · PARTIAL" : string.Empty);
        }

        await _changeReportService.SaveReportAsync(report, cancellationToken);
        ApplyChangeReport(report);
        await RefreshReportsAsync(showErrors: false);
        ReportHistoryList.SelectedItem = _reportHistory.FirstOrDefault(item => item.Id == report.Id);
    }

    private async Task<Snapshot> CreateSnapshotAsync(string rootPath, CancellationToken cancellationToken)
    {
        var progress = new Progress<SnapshotProgress>(update =>
        {
            string status = $"SCANNING · {update.FilesScanned:N0} files captured · {update.FilesSkipped:N0} skipped";
            SnapshotStatusText.Text = status;
            ChangeTrackingStatusText.Text = status;
            SnapshotProgressDetailsText.Text = update.CurrentPath;
            ChangeTrackingProgressDetailsText.Text = update.CurrentPath;
        });

        Snapshot snapshot = await _snapshotService.CreateSnapshotAsync(rootPath, progress, cancellationToken);
        _loadedSnapshot = snapshot;
        SnapshotFilesList.ItemsSource = snapshot.Files;
        SnapshotFilesEmptyText.Visibility = snapshot.Files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSnapshotStatistics(snapshot.Files.Count, snapshot.TotalSizeBytes, snapshot.Issues.Count);
        SnapshotProgressDetailsText.Text = $"ID: {snapshot.Id:D}";
        await RefreshSnapshotHistoryAsync(showErrors: false);
        SnapshotHistoryList.SelectedItem = _snapshotHistory.FirstOrDefault(item => item.Id == snapshot.Id);
        return snapshot;
    }

    private void SetSnapshotOperationUi(bool running, bool autoCheck)
    {
        SnapshotProgressBar.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        SnapshotProgressBar.IsIndeterminate = running;
        SnapshotProgressDetailsText.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        ChangeTrackingProgressBar.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        ChangeTrackingProgressDetailsText.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        CancelSnapshotButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        CancelChangeTrackingButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        ScanButton.IsEnabled = !running && !string.IsNullOrWhiteSpace(_selectedFilePath) && File.Exists(_selectedFilePath);
        UpdateSnapshotTargetControls(running);
    }

    private void UpdateSnapshotTargetControls(bool running = false)
    {
        bool targetReady = !string.IsNullOrWhiteSpace(_selectedSnapshotRootPath) && Directory.Exists(_selectedSnapshotRootPath);
        CreateSnapshotButton.IsEnabled = targetReady && !running;
        CreateTrackingSnapshotButton.IsEnabled = targetReady && !running;
        AutoCheckButton.IsEnabled = targetReady && !running;
    }

    private static List<string> BuildBaselineWarnings(Snapshot snapshot)
    {
        var warnings = snapshot.Issues
            .Select(issue => $"Baseline snapshot: {issue.RelativePath} [{issue.Code}] {issue.Message}")
            .ToList();
        warnings.Insert(0, "No previous snapshot was found for this RootPath. This report records the baseline only.");
        return warnings;
    }

    private void CancelSnapshot_Click(object sender, RoutedEventArgs e)
    {
        _snapshotCancellationSource?.Cancel();
        SnapshotStatusText.Text = "CANCELLING · Finishing the current file operation…";
        ChangeTrackingStatusText.Text = "CANCELLING · Finishing the current file operation…";
    }

    private async void RefreshSnapshots_Click(object sender, RoutedEventArgs e)
    {
        await RefreshSnapshotHistoryAsync();
    }

    private async Task RefreshSnapshotHistoryAsync(bool showErrors = true)
    {
        try
        {
            IReadOnlyList<SnapshotSummary> snapshots = await _snapshotService.ListSnapshotsAsync();
            _snapshotHistory.Clear();
            foreach (SnapshotSummary snapshot in snapshots)
                _snapshotHistory.Add(snapshot);

            SnapshotHistoryEmptyText.Visibility = _snapshotHistory.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            SnapshotStatusText.Text = "ERROR · Snapshot history could not be loaded.";
            if (showErrors)
            {
                MessageBox.Show(this, $"Snapshot history could not be loaded.\n\n{ex.Message}",
                    "ANUXES AFTERTRACE", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void SnapshotHistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadSnapshotButton.IsEnabled = SnapshotHistoryList.SelectedItem is SnapshotSummary;
    }

    private async void LoadSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (SnapshotHistoryList.SelectedItem is not SnapshotSummary summary)
            return;

        LoadSnapshotButton.IsEnabled = false;
        SnapshotStatusText.Text = "LOADING · Reading saved snapshot…";
        try
        {
            _loadedSnapshot = await _snapshotService.LoadSnapshotAsync(summary.Id);
            SnapshotFilesList.ItemsSource = _loadedSnapshot.Files;
            SnapshotFilesEmptyText.Visibility = _loadedSnapshot.Files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateSnapshotStatistics(_loadedSnapshot.Files.Count, _loadedSnapshot.TotalSizeBytes, _loadedSnapshot.Issues.Count);
            SnapshotStatusText.Text = $"LOADED · {_loadedSnapshot.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
            SnapshotProgressDetailsText.Visibility = Visibility.Visible;
            SnapshotProgressDetailsText.Text = $"ID: {_loadedSnapshot.Id:D}";
        }
        catch (Exception ex)
        {
            SnapshotStatusText.Text = "ERROR · Snapshot could not be loaded.";
            MessageBox.Show(this, $"The selected snapshot could not be loaded.\n\n{ex.Message}",
                "ANUXES AFTERTRACE", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            LoadSnapshotButton.IsEnabled = SnapshotHistoryList.SelectedItem is SnapshotSummary;
        }
    }

    private void UpdateSnapshotStatistics(int fileCount, long totalSizeBytes, int skippedCount)
    {
        SnapshotFilesText.Text = fileCount.ToString("N0");
        SnapshotSizeText.Text = FormatSize(totalSizeBytes);
        SnapshotSkippedText.Text = skippedCount.ToString("N0");
    }

    private async void RefreshReports_Click(object sender, RoutedEventArgs e)
    {
        await RefreshReportsAsync();
    }

    private async Task RefreshReportsAsync(bool showErrors = true)
    {
        try
        {
            IReadOnlyList<ChangeReportSummary> reports = await _changeReportService.ListReportsAsync();
            _reportHistory.Clear();
            foreach (ChangeReportSummary report in reports)
                _reportHistory.Add(report);

            ReportHistoryEmptyText.Visibility = _reportHistory.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ChangeTrackingStatusText.Text = "ERROR · Check history could not be loaded.";
            if (showErrors)
            {
                MessageBox.Show(this, $"Check history could not be loaded.\n\n{ex.Message}",
                    "ANUXES AFTERTRACE", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private async void ReportHistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReportHistoryList.SelectedItem is not ChangeReportSummary summary)
            return;

        try
        {
            ChangeReport report = await _changeReportService.LoadReportAsync(summary.Id);
            ApplyChangeReport(report);
        }
        catch (Exception ex)
        {
            ChangeTrackingStatusText.Text = "ERROR · Report could not be loaded.";
            MessageBox.Show(this, $"The selected report could not be loaded.\n\n{ex.Message}",
                "ANUXES AFTERTRACE", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyChangeReport(ChangeReport report)
    {
        _lastChangeReport = report;
        ChangeAllCountText.Text = report.Summary.Total.ToString("N0");
        ChangeAddedCountText.Text = report.Summary.Added.ToString("N0");
        ChangeModifiedCountText.Text = report.Summary.Modified.ToString("N0");
        ChangeDeletedCountText.Text = report.Summary.Deleted.ToString("N0");
        ChangeMovedRenamedCountText.Text = (report.Summary.Moved + report.Summary.Renamed).ToString("N0");
        ExportChangeReportButton.IsEnabled = true;
        ApplyChangeFilter();
        ChangeDetailTypeText.Text = report.IsBaseline
            ? "BASELINE · No previous snapshot"
            : report.IsPartial ? "PARTIAL CHECK · Select a change" : "CHECK RESULT · Select a change";
        ChangeDetailPathsText.Text = report.Warnings.Count > 0
            ? $"{report.Warnings.Count:N0} warning(s) · {report.RootPath}"
            : report.RootPath;
        ChangeDetailHashText.Text = string.Empty;
        ChangeDetailSizeText.Text = string.Empty;
        ChangeDetailNoteText.Text = report.Warnings.Count > 0 ? string.Join("\n", report.Warnings.Take(3)) : string.Empty;
        ChangeListEmptyText.Visibility = _visibleChanges.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChangeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyChangeFilter();
    }

    private void ApplyChangeFilter()
    {
        _visibleChanges.Clear();
        // SelectionChanged can be raised while InitializeComponent is still parsing
        // the XAML. The result controls declared later in the document are not ready yet.
        if (ChangeListEmptyText is null || ChangeFilterList is null)
            return;

        if (_lastChangeReport is null)
        {
            ChangeListEmptyText.Visibility = Visibility.Visible;
            return;
        }

        string selectedFilter = (ChangeFilterList.SelectedItem as ListBoxItem)?.Content?.ToString() ?? "ALL";
        IEnumerable<FileChange> changes = _lastChangeReport.Changes;
        if (!selectedFilter.Equals("ALL", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse(selectedFilter, ignoreCase: true, out FileChangeType filter))
            changes = changes.Where(change => change.Type == filter);

        foreach (FileChange change in changes)
            _visibleChanges.Add(change);
        ChangeListEmptyText.Visibility = _visibleChanges.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChangeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChangeList.SelectedItem is not FileChange change)
        {
            if (_lastChangeReport is not null)
            {
                ChangeDetailTypeText.Text = _lastChangeReport.IsPartial ? "PARTIAL CHECK · Select a change" : "CHECK RESULT · Select a change";
                ChangeDetailPathsText.Text = _lastChangeReport.RootPath;
            }
            return;
        }

        ChangeDetailTypeText.Text = change.IsAmbiguousMatch ? $"{change.Type.ToString().ToUpperInvariant()} · AMBIGUOUS HASH MATCH" : change.Type.ToString().ToUpperInvariant();
        ChangeDetailPathsText.Text = $"OLD: {change.OldPath ?? "—"}\nNEW: {change.NewPath ?? "—"}";
        ChangeDetailHashText.Text = $"SHA-256: {change.Sha256}";
        ChangeDetailSizeText.Text = $"OLD SIZE: {FormatNullableSize(change.OldSizeBytes)}   NEW SIZE: {FormatNullableSize(change.NewSizeBytes)}";
        ChangeDetailNoteText.Text = change.Note ?? string.Empty;
    }

    private async void ExportChangeReport_Click(object sender, RoutedEventArgs e)
    {
        if (_lastChangeReport is null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Export AFTERTRACE change report",
            FileName = $"AFTERTRACE-CHECK-{_lastChangeReport.CheckDateUtc.ToLocalTime():yyyyMMdd-HHmmss}.json",
            Filter = "JSON report (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            await _changeReportService.ExportReportAsync(_lastChangeReport, dialog.FileName);
            ChangeTrackingStatusText.Text = $"REPORT EXPORTED · {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"The change report could not be exported.\n\n{ex.Message}",
                "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string NormalizeRootPath(string path) => new DirectoryInfo(path).FullName;

    private static bool PathsEqual(string left, string right) =>
        string.Equals(NormalizeRootPath(left), NormalizeRootPath(right), StringComparison.OrdinalIgnoreCase);

    private static string FormatNullableSize(long? bytes) => bytes.HasValue ? FormatSize(bytes.Value) : "—";

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult is null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Export AFTERTRACE report",
            FileName = $"AFTERTRACE-{Path.GetFileNameWithoutExtension(_lastResult.FileName)}.json",
            Filter = "JSON report (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_lastResult, options);
            File.WriteAllText(dialog.FileName, json);
            ScannerStatusText.Text = $"REPORT EXPORTED · {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"The report could not be saved.\n\n{ex.Message}",
                "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
                ToggleMaximize();
            else
            {
                AnimateWindowGrab();
                try
                {
                    DragMove();
                }
                finally
                {
                    AnimateWindowRelease();
                }
            }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateWindowFrame();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
