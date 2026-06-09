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

    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsService.Load();
        SaveFolderTextBox.Text = _settings.SaveFolder;
    }

    private void SaveFolderTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _settings.SaveFolder = SaveFolderTextBox.Text.Trim();
        SettingsService.Save(_settings);
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择截图保存文件夹",
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

        SetCaptureButtonsEnabled(false);
        StatusTextBlock.Text = "正在截取全屏...";

        try
        {
            Hide();
            await Task.Delay(200);

            using var bitmap = ScreenshotService.CaptureFullScreen();
            var savedPath = ScreenshotService.SaveBitmap(bitmap, folder);
            ShowSaveResult(savedPath);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"截图失败：{ex.Message}";
        }
        finally
        {
            Show();
            Activate();
            SetCaptureButtonsEnabled(true);
        }
    }

    private async void RegionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateSaveFolder(out var folder))
        {
            return;
        }

        SetCaptureButtonsEnabled(false);
        StatusTextBlock.Text = "请拖动鼠标选择截图区域...";

        try
        {
            Hide();
            await Task.Delay(200);

            var overlay = new CaptureOverlayWindow();
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
            ShowSaveResult(savedPath);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"截图失败：{ex.Message}";
        }
        finally
        {
            Show();
            Activate();
            SetCaptureButtonsEnabled(true);
        }
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

    private void ShowSaveResult(string savedPath)
    {
        LastSavedTextBlock.Text = savedPath;
        StatusTextBlock.Text = "截图已保存。";
    }

    private void SetCaptureButtonsEnabled(bool enabled)
    {
        FullScreenButton.IsEnabled = enabled;
        RegionButton.IsEnabled = enabled;
    }
}
