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
    private string? _selectedFilePath;
    private FileAnalysisResult? _lastResult;
    private int _filesAnalyzed;
    private int _filesNeedingReview;
    private readonly ScaleTransform _frameScale = new(1, 1);
    private readonly SkewTransform _frameSkew = new(0, 0);

    public MainWindow()
    {
        InitializeComponent();
        FindingsList.ItemsSource = _findings;

        var frameTransform = new TransformGroup();
        frameTransform.Children.Add(_frameScale);
        frameTransform.Children.Add(_frameSkew);
        WindowFrame.RenderTransform = frameTransform;
        WindowFrame.RenderTransformOrigin = new Point(0.5, 0.5);

        StateChanged += MainWindow_StateChanged;
        Loaded += (_, _) => UpdateWindowFrame();
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
