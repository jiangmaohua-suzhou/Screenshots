using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Screenshot.Services;
using Screenshot.Windows;

namespace Screenshot;

public partial class MainWindow : Window
{
    private AppSettings _settings;
    private readonly ScreenRecordingService _recordingService = new();
    private RecordingIndicatorWindow? _recordingIndicator;

    public MainWindow()
    {
        _settings = SettingsService.Load();
        InitializeComponent();
        SaveFolderTextBox.Text = _settings.SaveFolder;
        RecordAudioCheckBox.IsChecked = _settings.RecordSystemAudio;
    }

    private void SaveFolderTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _settings.SaveFolder = SaveFolderTextBox.Text.Trim();
        SettingsService.Save(_settings);
    }

    private void RecordAudioCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _settings.RecordSystemAudio = RecordAudioCheckBox.IsChecked == true;
        SettingsService.Save(_settings);
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

        SetActionButtonsEnabled(false);
        StatusTextBlock.Text = "正在截取全屏...";

        try
        {
            Hide();
            await Task.Delay(200);

            using var bitmap = ScreenshotService.CaptureFullScreen();
            var savedPath = ScreenshotService.SaveBitmap(bitmap, folder);
            ShowSaveResult(savedPath, "截图已保存。");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"截图失败：{ex.Message}";
        }
        finally
        {
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

        SetActionButtonsEnabled(false);
        StatusTextBlock.Text = "请拖动鼠标选择截图区域...";

        try
        {
            Hide();
            await Task.Delay(200);

            var overlay = new CaptureOverlayWindow("拖动鼠标选择截图区域，松开保存；Esc 取消");
            var selected = overlay.ShowDialog() == true && overlay.SelectedRegion.HasValue;

            Show();
            Activate();

            if (!selected || overlay.SelectedRegion is not { } region)
            {
                StatusTextBlock.Text = "已取消区域截图。";
                return;
            }

            StatusTextBlock.Text = "正在保存截图...";
            using var bitmap = ScreenshotService.CaptureRegion(
                region.X,
                region.Y,
                region.Width,
                region.Height);
            var savedPath = ScreenshotService.SaveBitmap(bitmap, folder);
            ShowSaveResult(savedPath, "截图已保存。");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"截图失败：{ex.Message}";
        }
        finally
        {
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

        await StartRecordingAsync(folder, region: null);
    }

    private async void RegionRecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateSaveFolder(out var folder))
        {
            return;
        }

        SetActionButtonsEnabled(false);
        StatusTextBlock.Text = "请拖动鼠标选择录屏区域...";

        try
        {
            Hide();
            await Task.Delay(200);

            var overlay = new CaptureOverlayWindow("拖动鼠标选择录屏区域，松开后开始录制；Esc 取消");
            var selected = overlay.ShowDialog() == true && overlay.SelectedRegion.HasValue;

            Show();
            Activate();

            if (!selected || overlay.SelectedRegion is not { } region)
            {
                StatusTextBlock.Text = "已取消区域录屏。";
                SetActionButtonsEnabled(true);
                return;
            }

            await StartRecordingAsync(folder, region);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"录屏失败：{ex.Message}";
            SetActionButtonsEnabled(true);
        }
    }

    private Task StartRecordingAsync(string folder, Int32Rect? region)
    {
        SetActionButtonsEnabled(false);

        try
        {
            var expectedPath = _recordingService.Start(folder, _settings.RecordSystemAudio, region);
            ShowRecordingUi(expectedPath);
            StatusTextBlock.Text = region.HasValue ? "区域录屏已开始。" : "全屏录屏已开始。";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"录屏失败：{ex.Message}";
            SetActionButtonsEnabled(true);
        }

        return Task.CompletedTask;
    }

    private void ShowRecordingUi(string expectedPath)
    {
        WindowState = WindowState.Minimized;

        _recordingIndicator = new RecordingIndicatorWindow();
        _recordingIndicator.StopRequested += OnRecordingStopRequested;
        _recordingIndicator.Show();

        StopRecordButton.IsEnabled = true;
        LastSavedTextBlock.Text = $"录制中：{expectedPath}";
        StatusTextBlock.Text = "录屏进行中，点击“停止录屏”结束。";
    }

    private async void OnRecordingStopRequested(object? sender, EventArgs e)
    {
        await StopRecordingAsync();
    }

    private async void StopRecordButton_Click(object sender, RoutedEventArgs e)
    {
        await StopRecordingAsync();
    }

    private async Task StopRecordingAsync()
    {
        if (!_recordingService.IsRecording)
        {
            return;
        }

        SetRecordingControlsEnabled(false);
        StatusTextBlock.Text = "正在保存录屏文件...";

        try
        {
            var result = await _recordingService.StopAsync();
            CloseRecordingIndicator();

            WindowState = WindowState.Normal;
            Activate();

            if (result.Success && !string.IsNullOrWhiteSpace(result.FilePath))
            {
                ShowSaveResult(result.FilePath, "录屏已保存。");
            }
            else
            {
                StatusTextBlock.Text = $"录屏失败：{result.Error ?? "未知错误"}";
            }
        }
        catch (Exception ex)
        {
            CloseRecordingIndicator();
            WindowState = WindowState.Normal;
            Activate();
            StatusTextBlock.Text = $"录屏失败：{ex.Message}";
        }
        finally
        {
            SetActionButtonsEnabled(true);
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

        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
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

    private void ShowSaveResult(string savedPath, string statusMessage)
    {
        LastSavedTextBlock.Text = savedPath;
        StatusTextBlock.Text = statusMessage;
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
    }

    private void SetRecordingControlsEnabled(bool enabled)
    {
        StopRecordButton.IsEnabled = enabled;
        if (_recordingIndicator is not null)
        {
            _recordingIndicator.IsStopEnabled = enabled;
        }
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_recordingService.IsRecording)
        {
            _recordingService.Dispose();
            return;
        }

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

        await StopRecordingAsync();
        _recordingService.Dispose();
    }
}
