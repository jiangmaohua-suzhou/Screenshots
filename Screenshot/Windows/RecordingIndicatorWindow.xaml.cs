using System.Windows;
using System.Windows.Threading;

namespace Screenshot.Windows;

public partial class RecordingIndicatorWindow : Window
{
    private readonly DateTime _startTime = DateTime.UtcNow;
    private readonly DispatcherTimer _timer;

    public event EventHandler? StopRequested;

    public bool IsStopEnabled
    {
        get => StopButton.IsEnabled;
        set => StopButton.IsEnabled = value;
    }

    public RecordingIndicatorWindow()
    {
        InitializeComponent();

        Left = SystemParameters.WorkArea.Right - Width - 24;
        Top = SystemParameters.WorkArea.Bottom - Height - 24;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => UpdateDuration();
        _timer.Start();
        UpdateDuration();
    }

    private void UpdateDuration()
    {
        var elapsed = DateTime.UtcNow - _startTime;
        DurationTextBlock.Text = elapsed.ToString(elapsed.Hours > 0 ? @"h\:mm\:ss" : @"mm\:ss");
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        StopRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
