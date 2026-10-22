namespace ClientSSH.Services.Sessions;

/// <summary>A node of a tab's pane layout: either one pane (<see cref="PaneLeaf{T}"/>) or a split in two.</summary>
public abstract class PaneNode<T>
{
    public PaneSplit<T>? Parent { get; internal set; }
}

public sealed class PaneLeaf<T>(T content) : PaneNode<T>
{
    public T Content { get; } = content;
}

/// <summary>Two panes side by side (<see cref="SideBySide"/>) or stacked. <see cref="Ratio"/> is the first one's share.</summary>
public sealed class PaneSplit<T>(bool sideBySide, PaneNode<T> first, PaneNode<T> second) : PaneNode<T>
{
    public const double MinRatio = 0.1, MaxRatio = 0.9;
    private double _ratio = 0.5;

    public bool SideBySide { get; } = sideBySide;
    public PaneNode<T> First { get; internal set; } = first;
    public PaneNode<T> Second { get; internal set; } = second;

    public double Ratio
    {
        get => _ratio;
        set => _ratio = double.IsNaN(value) ? 0.5 : Math.Clamp(value, MinRatio, MaxRatio);
    }
}

/// <summary>
/// The pane layout of one tab, without UI types: split the focused pane, close a pane, move focus.
/// The tree is never empty: closing the last pane is refused, the caller closes the tab instead.
/// </summary>
public sealed class PaneTree<T>
{
    public PaneTree(T first)
    {
        Focused = new PaneLeaf<T>(first);
        Root = Focused;
    }

    public PaneNode<T> Root { get; private set; }
    public PaneLeaf<T> Focused { get; private set; }

    /// <summary>The layout changed shape (split or close). Focus and ratio changes do not raise it.</summary>
    public event Action? LayoutChanged;

    /// <summary>The focused pane changed.</summary>
    public event Action? FocusChanged;

    /// <summary>Panes in reading order (left to right, top to bottom).</summary>
    public IReadOnlyList<PaneLeaf<T>> Leaves
    {
        get
        {
            var list = new List<PaneLeaf<T>>();
            Collect(Root, list);
            return list;
        }
    }

    /// <summary>Splits the focused pane in two; the new pane goes right (side by side) or below, and takes focus.</summary>
    public PaneLeaf<T> Split(bool sideBySide, T content)
    {
        var target = Focused;
        var leaf = new PaneLeaf<T>(content);
        var parent = target.Parent;
        var split = new PaneSplit<T>(sideBySide, target, leaf) { Parent = parent };
        Replace(parent, target, split);
        target.Parent = split;
        leaf.Parent = split;
        Focused = leaf; // before LayoutChanged, so a redraw already knows which pane has focus
        LayoutChanged?.Invoke();
        FocusChanged?.Invoke();
        return leaf;
    }

    /// <summary>Removes a pane; its sibling takes the parent's place. Returns false (and changes nothing) for the last pane.</summary>
    public bool Close(PaneLeaf<T> leaf)
    {
        if (leaf.Parent is not { } parent) return false;
        var next = Neighbor(leaf, +1);
        var sibling = parent.First == leaf ? parent.Second : parent.First;
        Replace(parent.Parent, parent, sibling);
        sibling.Parent = parent.Parent;
        leaf.Parent = null;
        var focusMoves = Focused == leaf;
        if (focusMoves) Focused = next;
        LayoutChanged?.Invoke();
        if (focusMoves) FocusChanged?.Invoke();
        return true;
    }

    public void Focus(PaneLeaf<T> leaf)
    {
        if (Focused == leaf) return;
        Focused = leaf;
        FocusChanged?.Invoke();
    }

    /// <summary>Moves focus to the next (+1) or previous (-1) pane, wrapping around.</summary>
    public void FocusNext(int step = 1) => Focus(Neighbor(Focused, step));

    public PaneLeaf<T>? Find(T content) => Leaves.FirstOrDefault(l => EqualityComparer<T>.Default.Equals(l.Content, content));

    private PaneLeaf<T> Neighbor(PaneLeaf<T> leaf, int step)
    {
        var leaves = Leaves;
        var i = Math.Max(0, IndexOf(leaves, leaf));
        return leaves[((i + step) % leaves.Count + leaves.Count) % leaves.Count];
    }

    private static int IndexOf(IReadOnlyList<PaneLeaf<T>> leaves, PaneLeaf<T> leaf)
    {
        for (var i = 0; i < leaves.Count; i++) if (leaves[i] == leaf) return i;
        return -1;
    }

    private void Replace(PaneSplit<T>? parent, PaneNode<T> old, PaneNode<T> node)
    {
        if (parent is null) Root = node;
        else if (parent.First == old) parent.First = node;
        else parent.Second = node;
    }

    private static void Collect(PaneNode<T> node, List<PaneLeaf<T>> list)
    {
        if (node is PaneLeaf<T> leaf)
        {
            list.Add(leaf);
            return;
        }

        var split = (PaneSplit<T>)node;
        Collect(split.First, list);
        Collect(split.Second, list);
    }
}
