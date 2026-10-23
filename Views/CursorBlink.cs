namespace ClientSSH.Views;

/// <summary>The terminal cursor's on/off phase, flipped by a UI-thread timer.</summary>
internal sealed class CursorBlink
{
    private readonly Microsoft.UI.Xaml.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(530) };

    public bool On { get; private set; } = true;
    public event Action? Tick;

    public CursorBlink()
    {
        _timer.Tick += (_, _) =>
        {
            On = !On;
            Tick?.Invoke();
        };
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();
}
