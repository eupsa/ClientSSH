using CommunityToolkit.Mvvm.ComponentModel;

namespace ClientSSH.Services.Sessions;

/// <summary>One tab: a name the user can change and a layout of panes, each with its own session.</summary>
public sealed partial class SessionTab : ObservableObject
{
    public SessionTab(SessionPane first, string title)
    {
        Title = title;
        Layout = new PaneTree<SessionPane>(first);
        first.IsFocused = true;
        Layout.FocusChanged += OnFocusChanged;
        Layout.LayoutChanged += () => OnPropertyChanged(nameof(IsSplit));
    }

    [ObservableProperty] public partial string Title { get; set; }

    public PaneTree<SessionPane> Layout { get; }

    /// <summary>The pane that gets the keyboard, the assistant's text and new splits.</summary>
    public SessionPane Focused => Layout.Focused.Content;

    public IEnumerable<SessionPane> Panes => Layout.Leaves.Select(l => l.Content);

    public bool IsSplit => Layout.Root is PaneSplit<SessionPane>;

    public void Focus(SessionPane pane)
    {
        if (Layout.Find(pane) is { } leaf) Layout.Focus(leaf);
    }

    private void OnFocusChanged()
    {
        foreach (var pane in Panes) pane.IsFocused = pane == Focused;
        OnPropertyChanged(nameof(Focused));
    }
}
