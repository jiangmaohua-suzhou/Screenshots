using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Screenshot.Services;

public static class ScreenshotService
{
    public static Task<string> CaptureAndSaveFullScreenAsync(
        string folder,
        CancellationToken cancellationToken = default)
    {
        return CaptureAndSaveRegionAsync(
            (int)System.Windows.SystemParameters.VirtualScreenLeft,
            (int)System.Windows.SystemParameters.VirtualScreenTop,
            (int)System.Windows.SystemParameters.VirtualScreenWidth,
            (int)System.Windows.SystemParameters.VirtualScreenHeight,
            folder,
            cancellationToken);
    }

    public static async Task<string> CaptureAndSaveRegionAsync(
        int x,
        int y,
        int width,
        int height,
        string folder,
        CancellationToken cancellationToken = default)
    {
        var bitmap = await StaTaskRunner.RunAsync(
            () => CaptureRegion(x, y, width, height),
            cancellationToken).ConfigureAwait(false);

        try
        {
            return await SaveBitmapAsync(bitmap, folder, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    private static Bitmap CaptureRegion(int x, int y, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static Task<string> SaveBitmapAsync(
        Bitmap bitmap,
        string folder,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(folder);

            var fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            var filePath = Path.Combine(folder, fileName);

            bitmap.Save(filePath, ImageFormat.Png);
            return filePath;
        }, cancellationToken);
    }
}
