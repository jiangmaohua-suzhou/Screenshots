using System.IO;
using System.Text.Json;

namespace Screenshot.Services;

public class AppSettings
{
    public string SaveFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "Screenshots");

    public bool RecordSystemAudio { get; set; } = true;
}

public static class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ScreenshotTool",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly SemaphoreSlim SaveLock = new(1, 1);
    private static CancellationTokenSource? _debounceCts;

    public static async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            await using var stream = new FileStream(
                SettingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await SaveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(directory);

            await using var stream = new FileStream(
                SettingsPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);

            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            SaveLock.Release();
        }
    }

    public static void ScheduleSave(AppSettings settings, int delayMilliseconds = 400)
    {
        var snapshot = new AppSettings
        {
            SaveFolder = settings.SaveFolder,
            RecordSystemAudio = settings.RecordSystemAudio
        };

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        _ = DebouncedSaveAsync(snapshot, delayMilliseconds, token);
    }

    private static async Task DebouncedSaveAsync(
        AppSettings settings,
        int delayMilliseconds,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
            await SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Debounced save superseded by a newer change.
        }
    }
}
