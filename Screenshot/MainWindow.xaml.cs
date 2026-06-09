using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using Screenshot.Services;
using Screenshot.Windows;

namespace Screenshot;

public partial class MainWindow : Window
{
    private AppSettings _settings = new();
    private readonly ScreenRecordingService _recordingService = new();
    private readonly CancellationTokenSource _windowCts = new();
    private CancellationTokenSource? _operationCts;
    private RecordingIndicatorWindow? _recordingIndicator;
    private bool _isApplyingSettings;
    private int _isStoppingRecording;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closed += (_, _) => _windowCts.Cancel();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _isApplyingSettings = true;
        try
        {
            _settings = await SettingsService.LoadAsync(_windowCts.Token).ConfigureAwait(true);
            SaveFolderTextBox.Text = _settings.SaveFolder;
            RecordAudioCheckBox.IsChecked = _settings.RecordSystemAudio;
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void SaveFolderTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsLoaded || _isApplyingSettings)
        {
            return;
        }

        _settings.SaveFolder = SaveFolderTextBox.Text.Trim();
        SettingsService.ScheduleSave(_settings);
    }

    private void RecordAudioCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isApplyingSettings)
        {
            return;
        }

        _settings.RecordSystemAudio = RecordAudioCheckBox.IsChecked == true;
        _ = SettingsService.SaveAsync(_settings);
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择保存文件夹",
            InitialDirectory = Directory.Exists(_settings.SaveFolder)
                ? _settings.SaveFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };

        if (dialog.ShowDialog() == true)
        {
            SaveFolderTextBox.Text = dialog.FolderName;
        }
    }

    private async void FullScreenButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateSaveFolder(out var folder))
        {
            return;
        }

        var operationToken = BeginOperation();
        SetActionButtonsEnabled(false);
        SetStatus("正在截取全屏...", StatusKind.Info);

        try
        {
            await PrepareForCaptureAsync().ConfigureAwait(true);
            var savedPath = await ScreenshotService.CaptureAndSaveFullScreenAsync(
                folder,
                operationToken).ConfigureAwait(true);
            ShowSaveResult(savedPath, "截图已保存。");
        }
        catch (OperationCanceledException)
        {
            SetStatus("截图已取消。", StatusKind.Warning);
        }
        catch (Exception ex)
        {
            SetStatus($"截图失败：{ex.Message}", StatusKind.Error);
        }
        finally
        {
            EndOperation();
            Show();
            Activate();
            SetActionButtonsEnabled(true);
        }
    }

    private async void RegionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateSaveFolder(out var folder))
        {
            return;
        }

        var operationToken = BeginOperation();
        SetActionButtonsEnabled(false);
        SetStatus("请拖动鼠标选择截图区域...", StatusKind.Info);

        try
        {
            await PrepareForCaptureAsync().ConfigureAwait(true);

            var overlay = new CaptureOverlayWindow("拖动鼠标选择截图区域，松开保存；Esc 取消");
            var selected = overlay.ShowDialog() == true && overlay.SelectedRegion.HasValue;

            Show();
            Activate();

            if (!selected || overlay.SelectedRegion is not { } region)
            {
                SetStatus("已取消区域截图。", StatusKind.Warning);
                return;
            }

            SetStatus("正在保存截图...", StatusKind.Info);
            var savedPath = await ScreenshotService.CaptureAndSaveRegionAsync(
                region.X,
                region.Y,
                region.Width,
                region.Height,
                folder,
                operationToken).ConfigureAwait(true);
            ShowSaveResult(savedPath, "截图已保存。");
        }
        catch (OperationCanceledException)
        {
            SetStatus("截图已取消。", StatusKind.Warning);
        }
        catch (Exception ex)
        {
            SetStatus($"截图失败：{ex.Message}", StatusKind.Error);
        }
        finally
        {
            EndOperation();
            Show();
            Activate();
            SetActionButtonsEnabled(true);
        }
    }

    private async void FullScreenRecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateSaveFolder(out var folder))
        {
            return;
        }

        await StartRecordingAsync(folder, region: null).ConfigureAwait(true);
    }

    private async void RegionRecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateSaveFolder(out var folder))
        {
            return;
        }

        var operationToken = BeginOperation();
        SetActionButtonsEnabled(false);
        SetStatus("请拖动鼠标选择录屏区域...", StatusKind.Info);

        try
        {
            await PrepareForCaptureAsync().ConfigureAwait(true);

            var overlay = new CaptureOverlayWindow("拖动鼠标选择录屏区域，松开后开始录制；Esc 取消");
            var selected = overlay.ShowDialog() == true && overlay.SelectedRegion.HasValue;

            Show();
            Activate();

            if (!selected || overlay.SelectedRegion is not { } region)
            {
                SetStatus("已取消区域录屏。", StatusKind.Warning);
                SetActionButtonsEnabled(true);
                return;
            }

            await StartRecordingAsync(folder, region, operationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            SetStatus("录屏已取消。", StatusKind.Warning);
            SetActionButtonsEnabled(true);
        }
        catch (Exception ex)
        {
            SetStatus($"录屏失败：{ex.Message}", StatusKind.Error);
            SetActionButtonsEnabled(true);
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task StartRecordingAsync(
        string folder,
        Int32Rect? region,
        CancellationToken cancellationToken = default)
    {
        SetActionButtonsEnabled(false);

        try
        {
            var expectedPath = await _recordingService.StartAsync(
                folder,
                _settings.RecordSystemAudio,
                (int)SystemParameters.VirtualScreenWidth,
                (int)SystemParameters.VirtualScreenHeight,
                region,
                cancellationToken).ConfigureAwait(true);

            ShowRecordingUi(expectedPath);
            SetStatus(
                region.HasValue ? "区域录屏已开始。" : "全屏录屏已开始。",
                StatusKind.Success);
        }
        catch (OperationCanceledException)
        {
            SetStatus("录屏已取消。", StatusKind.Warning);
            SetActionButtonsEnabled(true);
        }
        catch (Exception ex)
        {
            SetStatus($"录屏失败：{ex.Message}", StatusKind.Error);
            SetActionButtonsEnabled(true);
        }
    }

    private void ShowRecordingUi(string expectedPath)
    {
        WindowState = WindowState.Minimized;

        _recordingIndicator = new RecordingIndicatorWindow();
        _recordingIndicator.StopRequested += OnRecordingStopRequested;
        _recordingIndicator.Show();

        StopRecordButton.IsEnabled = true;
        SetStatus($"录屏进行中：{expectedPath}", StatusKind.Info);
    }

    private async void OnRecordingStopRequested(object? sender, EventArgs e)
    {
        await StopRecordingAsync().ConfigureAwait(true);
    }

    private async void StopRecordButton_Click(object sender, RoutedEventArgs e)
    {
        await StopRecordingAsync().ConfigureAwait(true);
    }

    private async Task StopRecordingAsync()
    {
        if (!_recordingService.IsRecording)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _isStoppingRecording, 1, 0) != 0)
        {
            return;
        }

        SetRecordingControlsEnabled(false);
        SetStatus("正在保存录屏文件...", StatusKind.Info);

        try
        {
            var result = await _recordingService.StopAsync(CancellationToken.None).ConfigureAwait(false);

            await Dispatcher.InvokeAsync(() =>
            {
                CloseRecordingIndicator();
                WindowState = WindowState.Normal;
                Activate();

                if (result.Success && !string.IsNullOrWhiteSpace(result.FilePath))
                {
                    ShowSaveResult(result.FilePath, "录屏已保存。");
                }
                else
                {
                    SetStatus($"录屏失败：{result.Error ?? "未知错误"}", StatusKind.Error);
                }
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                CloseRecordingIndicator();
                WindowState = WindowState.Normal;
                Activate();
                SetStatus($"录屏失败：{ex.Message}", StatusKind.Error);
            });
        }
        finally
        {
            Interlocked.Exchange(ref _isStoppingRecording, 0);
            await Dispatcher.InvokeAsync(() => SetActionButtonsEnabled(true));
        }
    }

    private void CloseRecordingIndicator()
    {
        if (_recordingIndicator is null)
        {
            return;
        }

        _recordingIndicator.StopRequested -= OnRecordingStopRequested;
        _recordingIndicator.Close();
        _recordingIndicator = null;
        StopRecordButton.IsEnabled = false;
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateSaveFolder(out var folder))
        {
            return;
        }

        _ = Task.Run(() =>
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        });
    }

    private bool ValidateSaveFolder(out string folder)
    {
        folder = SaveFolderTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(folder))
        {
            MessageBox.Show("请先选择保存文件夹。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        try
        {
            Directory.CreateDirectory(folder);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法使用该文件夹：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private async Task PrepareForCaptureAsync()
    {
        Hide();
        await Task.Delay(200, _windowCts.Token).ConfigureAwait(true);
    }

    private CancellationToken BeginOperation()
    {
        CancelOperation();
        _operationCts = CancellationTokenSource.CreateLinkedTokenSource(_windowCts.Token);
        return _operationCts.Token;
    }

    private void EndOperation()
    {
        if (_operationCts is null)
        {
            return;
        }

        _operationCts.Dispose();
        _operationCts = null;
    }

    private void CancelOperation()
    {
        if (_operationCts is null)
        {
            return;
        }

        try
        {
            if (!_operationCts.IsCancellationRequested)
            {
                _operationCts.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by a completed operation.
        }
        finally
        {
            try
            {
                _operationCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed.
            }

            _operationCts = null;
        }
    }

    private void ShowSaveResult(string savedPath, string statusMessage)
    {
        SetStatus($"{statusMessage} {savedPath}", StatusKind.Success);
    }

    private void SetStatus(string message, StatusKind kind)
    {
        StatusTextBlock.Text = message;
        StatusPanel.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;

        StatusIcon.Kind = kind switch
        {
            StatusKind.Success => PackIconKind.CheckCircle,
            StatusKind.Warning => PackIconKind.AlertCircle,
            StatusKind.Error => PackIconKind.CloseCircle,
            _ => PackIconKind.Information
        };

        StatusIcon.Foreground = kind switch
        {
            StatusKind.Success => GetThemeBrush("MaterialDesign.Brush.Secondary"),
            StatusKind.Warning => new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11)),
            StatusKind.Error => GetThemeBrush("MaterialDesign.Brush.ValidationError"),
            _ => GetThemeBrush("MaterialDesign.Brush.Primary")
        };
    }

    private System.Windows.Media.Brush GetThemeBrush(string resourceKey)
    {
        return TryFindResource(resourceKey) as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.Gray;
    }

    private void SetActionButtonsEnabled(bool enabled)
    {
        var recording = _recordingService.IsRecording;
        FullScreenButton.IsEnabled = enabled && !recording;
        RegionButton.IsEnabled = enabled && !recording;
        FullScreenRecordButton.IsEnabled = enabled && !recording;
        RegionRecordButton.IsEnabled = enabled && !recording;
        StopRecordButton.IsEnabled = recording;
        SaveFolderTextBox.IsEnabled = enabled && !recording;
        RecordAudioCheckBox.IsEnabled = enabled && !recording;
        OpenFolderButton.IsEnabled = enabled && !recording;
    }

    private void SetRecordingControlsEnabled(bool enabled)
    {
        StopRecordButton.IsEnabled = enabled;
        if (_recordingIndicator is not null)
        {
            _recordingIndicator.IsStopEnabled = enabled;
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_recordingService.IsRecording)
        {
            var result = MessageBox.Show(
                "录屏仍在进行中，确定要停止录屏并退出吗？",
                "确认退出",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            e.Cancel = true;
            _ = StopRecordingAndCloseAsync();
            return;
        }

        CancelOperation();
        _windowCts.Cancel();
        _recordingService.Dispose();
        _windowCts.Dispose();
    }

    private async Task StopRecordingAndCloseAsync()
    {
        try
        {
            await StopRecordingAsync().ConfigureAwait(false);
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                CancelOperation();
                _windowCts.Cancel();
                _recordingService.Dispose();
                _windowCts.Dispose();
                Close();
            });
        }
    }

    private enum StatusKind
    {
        Info,
        Success,
        Warning,
        Error
    }
}
