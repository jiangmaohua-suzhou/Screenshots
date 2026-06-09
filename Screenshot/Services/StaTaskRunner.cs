using System.Collections.Concurrent;

namespace Screenshot.Services;

/// <summary>
/// Runs GDI/GDI+ work on a dedicated STA thread required for reliable screen capture.
/// </summary>
internal static class StaTaskRunner
{
    private static readonly BlockingCollection<Action> WorkQueue = new();
    private static readonly Thread WorkerThread;

    static StaTaskRunner()
    {
        WorkerThread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "ScreenshotStaWorker"
        };
        WorkerThread.SetApartmentState(ApartmentState.STA);
        WorkerThread.Start();
    }

    public static Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        }

        WorkQueue.Add(() =>
        {
            if (tcs.Task.IsCompleted)
            {
                return;
            }

            try
            {
                tcs.SetResult(work());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    public static Task RunAsync(Action work, CancellationToken cancellationToken = default)
    {
        return RunAsync<object?>(() =>
        {
            work();
            return null;
        }, cancellationToken);
    }

    private static void WorkerLoop()
    {
        foreach (var work in WorkQueue.GetConsumingEnumerable())
        {
            work();
        }
    }
}
