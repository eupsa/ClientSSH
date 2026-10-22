using ClientSSH.Terminal;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace ClientSSH.Views;

// Keyboard, clipboard and mouse wheel: everything typed goes straight to the session.
public sealed partial class TerminalView
{
    private char _highSurrogate;

    // Right click pastes (like PuTTY and Windows Terminal); left click only takes focus.
    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        e.Handled = true;
        _ = PasteAsync();
    }

    private async Task PasteAsync()
    {
        try
        {
            var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (!content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)) return;
            Send(PasteText.Clean(await content.GetTextAsync()));
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // The clipboard can be locked by another app for a moment: ignore, the user can paste again.
        }
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void Send(string text)
    {
        if (_session is null) return;
        lock (_session.Screen.Sync) _session.Screen.ViewOffset = 0;
        _session.Write(text);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = IsDown(VirtualKey.Control);
        if (ctrl && e.Key == VirtualKey.V) // also Ctrl+Shift+V
        {
            _ = PasteAsync();
            e.Handled = true;
            return;
        }

        var sequence = TerminalKeys.Sequence(e.Key, ctrl, IsDown(VirtualKey.Shift), _session?.Screen.AppCursorKeys ?? false);
        if (sequence is null) return;
        Send(sequence);
        e.Handled = true;
    }

    // shortcut: no Alt combinations; add with selection support.
    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        var c = e.Character;
        if (c < ' ' || c == '\x7f' || IsDown(VirtualKey.Control)) return;

        if (char.IsHighSurrogate(c))
        {
            _highSurrogate = c;
        }
        else if (char.IsLowSurrogate(c) && _highSurrogate != 0)
        {
            Send(new string([_highSurrogate, c]));
            _highSurrogate = '\0';
        }
        else
        {
            Send(c.ToString());
        }

        e.Handled = true;
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_session is null) return;
        var lines = e.GetCurrentPoint(this).Properties.MouseWheelDelta / 120 * 3;
        lock (_session.Screen.Sync)
            _session.Screen.ViewOffset = Math.Clamp(_session.Screen.ViewOffset + lines, 0, _session.Screen.ScrollbackCount);
        _canvas?.Invalidate();
    }
}
