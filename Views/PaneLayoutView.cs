using ClientSSH.Services.Sessions;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ClientSSH.Views;

/// <summary>
/// Renders the selected tab's pane tree: one <see cref="SessionView"/> per pane, with draggable dividers between them.
/// Only the selected tab is built, so only visible panes hold a Win2D canvas. The layout state lives in the tab's
/// <see cref="PaneTree{T}"/>; this view only draws it and writes divider drags back to the split's ratio.
/// </summary>
public sealed partial class PaneLayoutView : UserControl
{
    private const double DividerSize = 8;

    public static readonly DependencyProperty TabProperty = DependencyProperty.Register(
        nameof(Tab), typeof(SessionTab), typeof(PaneLayoutView),
        new PropertyMetadata(null, (d, e) => ((PaneLayoutView)d).OnTabChanged((SessionTab?)e.OldValue, (SessionTab?)e.NewValue)));

    private readonly Dictionary<SessionPane, SessionView> _views = [];

    public SessionTab? Tab
    {
        get => (SessionTab?)GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    public PaneLayoutView() => IsTabStop = false;

    /// <summary>Gives the keyboard to the focused pane's terminal.</summary>
    public void FocusTerminal()
    {
        if (Tab is { } tab && _views.TryGetValue(tab.Focused, out var view)) view.FocusTerminal();
    }

    private void OnTabChanged(SessionTab? oldTab, SessionTab? newTab)
    {
        if (oldTab is not null)
        {
            oldTab.Layout.LayoutChanged -= Rebuild;
            oldTab.Layout.FocusChanged -= FocusTerminal;
        }

        if (newTab is not null)
        {
            newTab.Layout.LayoutChanged += Rebuild;
            newTab.Layout.FocusChanged += FocusTerminal;
        }

        Rebuild();
    }

    // New views every time: moving a terminal between parents can fire Loaded before Unloaded and drop its canvas.
    private void Rebuild()
    {
        _views.Clear();
        Content = Tab is { } tab ? Build(tab, tab.Layout.Root) : null;
    }

    private UIElement Build(SessionTab tab, PaneNode<SessionPane> node)
    {
        if (node is PaneLeaf<SessionPane> leaf)
        {
            var view = new SessionView { Pane = leaf.Content, ShowFocus = tab.IsSplit, FocusOnLoad = leaf == tab.Layout.Focused };
            view.PaneActivated += () => tab.Focus(leaf.Content);
            _views[leaf.Content] = view;
            return view;
        }

        var split = (PaneSplit<SessionPane>)node;
        var grid = new Grid();
        var divider = new PaneDivider(split, grid);
        if (split.SideBySide)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DividerSize) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(DividerSize) });
            grid.RowDefinitions.Add(new RowDefinition());
        }

        var first = Build(tab, split.First);
        var second = Build(tab, split.Second);
        Place(first, split.SideBySide, 0);
        Place(divider, split.SideBySide, 1);
        Place(second, split.SideBySide, 2);
        grid.Children.Add(first);
        grid.Children.Add(divider);
        grid.Children.Add(second);
        divider.Apply();
        return grid;
    }

    private static void Place(UIElement element, bool sideBySide, int index)
    {
        if (sideBySide) Grid.SetColumn((FrameworkElement)element, index);
        else Grid.SetRow((FrameworkElement)element, index);
    }

    /// <summary>The gap between two panes; drag it to resize them.</summary>
    // shortcut: mouse only (the terminal keeps Tab, so a divider could not be reached by keyboard anyway); add resize shortcuts if asked.
    private sealed partial class PaneDivider : Grid
    {
        private readonly PaneSplit<SessionPane> _split;
        private readonly Grid _owner;

        public PaneDivider(PaneSplit<SessionPane> split, Grid owner)
        {
            _split = split;
            _owner = owner;
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent); // hit-testable
            ProtectedCursor = InputSystemCursor.Create(split.SideBySide ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.SizeNorthSouth);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, Services.Localization.Loc.Get("Sessions_Divider"));
            PointerPressed += OnPressed;
            PointerMoved += OnMoved;
            PointerReleased += (_, e) => ReleasePointerCapture(e.Pointer);
        }

        public void Apply()
        {
            var first = new GridLength(_split.Ratio, GridUnitType.Star);
            var second = new GridLength(1 - _split.Ratio, GridUnitType.Star);
            if (_split.SideBySide)
            {
                _owner.ColumnDefinitions[0].Width = first;
                _owner.ColumnDefinitions[2].Width = second;
            }
            else
            {
                _owner.RowDefinitions[0].Height = first;
                _owner.RowDefinitions[2].Height = second;
            }
        }

        private void OnPressed(object sender, PointerRoutedEventArgs e)
        {
            CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void OnMoved(object sender, PointerRoutedEventArgs e)
        {
            if (PointerCaptures is not { Count: > 0 }) return;
            var point = e.GetCurrentPoint(_owner).Position;
            var total = (_split.SideBySide ? _owner.ActualWidth : _owner.ActualHeight) - DividerSize;
            if (total <= 0) return;
            _split.Ratio = ((_split.SideBySide ? point.X : point.Y) - DividerSize / 2) / total;
            Apply();
        }
    }
}
