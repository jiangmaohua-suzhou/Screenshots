using System.IO;
using System.Windows;
using ScreenRecorderLib;

namespace Screenshot.Services;

public sealed class RecordingResult
{
    public bool Success { get; init; }
    public string? FilePath { get; init; }
    public string? Error { get; init; }
}

public sealed class ScreenRecordingService : IDisposable
{
    private readonly object _sync = new();
    private Recorder? _recorder;
    private TaskCompletionSource<RecordingResult>? _completionSource;

    public bool IsRecording { get; private set; }

    public Task<string> StartAsync(
        string outputFolder,
        bool includeAudio,
        int virtualScreenWidth,
        int virtualScreenHeight,
        Int32Rect? region = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (IsRecording)
            {
                throw new InvalidOperationException("已有录屏任务正在进行。");
            }

            Directory.CreateDirectory(outputFolder);
            var outputPath = Path.Combine(outputFolder, $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
            var options = BuildOptions(includeAudio, region, virtualScreenWidth, virtualScreenHeight);

            _recorder = Recorder.CreateRecorder(options);
            _completionSource = new TaskCompletionSource<RecordingResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _recorder.OnRecordingComplete += OnRecordingComplete;
            _recorder.OnRecordingFailed += OnRecordingFailed;

            IsRecording = true;
            _recorder.Record(outputPath);

            return Task.FromResult(outputPath);
        }
    }

    public async Task<RecordingResult> StopAsync(CancellationToken cancellationToken = default)
    {
        Recorder? recorder;
        Task<RecordingResult> completionTask;

        lock (_sync)
        {
            if (_recorder is null || !IsRecording)
            {
                return new RecordingResult
                {
                    Success = false,
                    Error = "当前没有进行中的录屏。"
                };
            }

            recorder = _recorder;
            completionTask = _completionSource!.Task;
        }

        try
        {
            await Task.Run(recorder.Stop, cancellationToken).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(2));
            return await completionTask.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new RecordingResult
            {
                Success = false,
                Error = "等待录屏结束超时。"
            };
        }
        finally
        {
            lock (_sync)
            {
                CleanupRecorder();
            }
        }
    }

    private void OnRecordingComplete(object? sender, RecordingCompleteEventArgs e)
    {
        Finish(new RecordingResult
        {
            Success = true,
            FilePath = e.FilePath
        });
    }

    private void OnRecordingFailed(object? sender, RecordingFailedEventArgs e)
    {
        Finish(new RecordingResult
        {
            Success = false,
            Error = e.Error
        });
    }

    private void Finish(RecordingResult result)
    {
        lock (_sync)
        {
            IsRecording = false;
            _completionSource?.TrySetResult(result);
        }
    }

    private void CleanupRecorder()
    {
        if (_recorder is null)
        {
            return;
        }

        _recorder.OnRecordingComplete -= OnRecordingComplete;
        _recorder.OnRecordingFailed -= OnRecordingFailed;
        _recorder.Dispose();
        _recorder = null;
        _completionSource = null;
    }

    private static RecorderOptions BuildOptions(
        bool includeAudio,
        Int32Rect? region,
        int virtualScreenWidth,
        int virtualScreenHeight)
    {
        var sources = Recorder.GetDisplays().Cast<RecordingSourceBase>().ToList();
        if (sources.Count == 0)
        {
            sources.Add(new DisplayRecordingSource(DisplayRecordingSource.MainMonitor));
        }

        var outputOptions = new OutputOptions
        {
            RecorderMode = RecorderMode.Video,
            Stretch = StretchMode.Uniform
        };

        if (region is { } selectedRegion)
        {
            var width = MakeEven(selectedRegion.Width);
            var height = MakeEven(selectedRegion.Height);
            outputOptions.SourceRect = new ScreenRect(
                selectedRegion.X,
                selectedRegion.Y,
                selectedRegion.X + width,
                selectedRegion.Y + height);
            outputOptions.OutputFrameSize = new ScreenSize(width, height);
        }
        else
        {
            outputOptions.OutputFrameSize = new ScreenSize(
                MakeEven(virtualScreenWidth),
                MakeEven(virtualScreenHeight));
        }

        return new RecorderOptions
        {
            SourceOptions = new SourceOptions
            {
                RecordingSources = sources
            },
            OutputOptions = outputOptions,
            AudioOptions = new AudioOptions
            {
                IsAudioEnabled = includeAudio,
                IsOutputDeviceEnabled = includeAudio,
                IsInputDeviceEnabled = false,
                Bitrate = AudioBitrate.bitrate_128kbps,
                Channels = AudioChannels.Stereo
            },
            VideoEncoderOptions = new VideoEncoderOptions
            {
                Bitrate = 6000 * 1000,
                Framerate = 30,
                IsFixedFramerate = true,
                IsHardwareEncodingEnabled = true,
                IsFragmentedMp4Enabled = true,
                Encoder = new H264VideoEncoder
                {
                    BitrateMode = H264BitrateControlMode.CBR,
                    EncoderProfile = H264Profile.Main
                }
            },
            MouseOptions = new MouseOptions
            {
                IsMousePointerEnabled = true
            }
        };
    }

    private static int MakeEven(int value) => value % 2 == 0 ? value : value - 1;

    public void Dispose()
    {
        lock (_sync)
        {
            if (IsRecording)
            {
                _recorder?.Stop();
            }

            CleanupRecorder();
        }
    }
}
