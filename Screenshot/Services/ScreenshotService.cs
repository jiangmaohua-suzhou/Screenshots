using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;

namespace Screenshot.Services;

public static class ScreenshotService
{
    public static Bitmap CaptureFullScreen()
    {
        var left = (int)SystemParameters.VirtualScreenLeft;
        var top = (int)SystemParameters.VirtualScreenTop;
        var width = (int)SystemParameters.VirtualScreenWidth;
        var height = (int)SystemParameters.VirtualScreenHeight;

        return CaptureRegion(left, top, width, height);
    }

    public static Bitmap CaptureRegion(int x, int y, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    public static string SaveBitmap(Bitmap bitmap, string folder)
    {
        Directory.CreateDirectory(folder);

        var fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
        var filePath = Path.Combine(folder, fileName);

        bitmap.Save(filePath, ImageFormat.Png);
        return filePath;
    }
}
