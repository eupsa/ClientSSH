using ClientSSH.Models;
using ClientSSH.Services.Storage;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace ClientSSH.Services.Shell;

/// <summary>
/// Opens the main window where and how big the user left it (centered the first time) and remembers changes.
/// Sizes are in physical pixels, like AppWindow.
/// </summary>
public sealed class WindowStateService(ISettingsStore settings)
{
    private AppWindow? _window;
    private DispatcherQueueTimer? _saveTimer;

    /// <summary>Call before the window is shown.</summary>
    public void Attach(Window window, int defaultWidth, int defaultHeight)
    {
        _window = window.AppWindow;
        Restore(defaultWidth, defaultHeight);

        // Moving or resizing fires many events: write once, shortly after the last one.
        _saveTimer = window.DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(800);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => Save();

        _window.Changed += (_, args) =>
        {
            if (args.DidSizeChange || args.DidPositionChange) _saveTimer.Start();
        };
        _window.Closing += (_, _) => Save();
    }

    private void Restore(int defaultWidth, int defaultHeight)
    {
        var window = _window!;
        var saved = settings.Current.Window;

        // Use the saved spot only if its title bar is still on a screen (a monitor may have been unplugged).
        if (saved is not null && DisplayArea.GetFromPoint(new PointInt32(saved.X + saved.Width / 2, saved.Y + 16), DisplayAreaFallback.None) is not null)
        {
            window.MoveAndResize(new RectInt32(saved.X, saved.Y, saved.Width, saved.Height));
            if (saved.Maximized && window.Presenter is OverlappedPresenter presenter) presenter.Maximize();
            return;
        }

        var work = DisplayArea.Primary.WorkArea;
        var width = Math.Min(defaultWidth, work.Width);
        var height = Math.Min(defaultHeight, work.Height);
        window.MoveAndResize(new RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
    }

    private void Save()
    {
        if (_window is null) return;

        var state = (_window.Presenter as OverlappedPresenter)?.State;
        if (state == OverlappedPresenterState.Minimized) return; // minimized windows report a fake position

        var previous = settings.Current.Window;
        var maximized = state == OverlappedPresenterState.Maximized;
        // Maximized: keep the normal bounds, so un-maximizing later goes back to them.
        var next = maximized && previous is not null
            ? previous with { Maximized = true }
            : new WindowState(_window.Position.X, _window.Position.Y, _window.Size.Width, _window.Size.Height, maximized);

        if (next != previous) settings.Save(settings.Current with { Window = next });
    }
}
