using ClientSSH.Services.Sessions;
using Xunit;

namespace ClientSSH.Tests;

public class PaneTreeTests
{
    private static string[] Names(PaneTree<string> tree) => tree.Leaves.Select(l => l.Content).ToArray();

    [Fact]
    public void Starts_with_one_focused_pane()
    {
        var tree = new PaneTree<string>("a");
        Assert.Equal(["a"], Names(tree));
        Assert.Equal("a", tree.Focused.Content);
        Assert.IsType<PaneLeaf<string>>(tree.Root);
    }

    [Fact]
    public void Split_puts_new_pane_after_the_focused_one_and_focuses_it()
    {
        var tree = new PaneTree<string>("a");
        var layoutChanges = 0;
        tree.LayoutChanged += () => layoutChanges++;

        tree.Split(sideBySide: true, "b");
        var root = Assert.IsType<PaneSplit<string>>(tree.Root);
        Assert.True(root.SideBySide);
        Assert.Equal("b", tree.Focused.Content);

        tree.Focus(tree.Leaves[0]);
        tree.Split(sideBySide: false, "c"); // splits "a" again, stacked
        Assert.Equal(["a", "c", "b"], Names(tree));
        var inner = Assert.IsType<PaneSplit<string>>(root.First);
        Assert.False(inner.SideBySide);
        Assert.Same(root, inner.Parent);
        Assert.Equal(2, layoutChanges);
    }

    [Fact]
    public void Close_promotes_the_sibling_and_moves_focus()
    {
        var tree = new PaneTree<string>("a");
        tree.Split(true, "b");
        tree.Split(false, "c"); // b splits into b over c
        Assert.Equal(["a", "b", "c"], Names(tree));

        Assert.True(tree.Close(tree.Focused)); // close c
        Assert.Equal(["a", "b"], Names(tree));
        Assert.Equal("a", tree.Focused.Content); // next pane, wrapping around
        var root = Assert.IsType<PaneSplit<string>>(tree.Root);
        Assert.Null(root.Parent);
        Assert.Equal("b", Assert.IsType<PaneLeaf<string>>(root.Second).Content);

        Assert.True(tree.Close(tree.Leaves[0])); // close a: b becomes the root
        Assert.Equal("b", Assert.IsType<PaneLeaf<string>>(tree.Root).Content);
        Assert.Null(tree.Root.Parent);
        Assert.Equal("b", tree.Focused.Content);
    }

    [Fact]
    public void Closing_the_last_pane_is_refused()
    {
        var tree = new PaneTree<string>("a");
        Assert.False(tree.Close(tree.Focused));
        Assert.Equal(["a"], Names(tree));
    }

    [Fact]
    public void Closing_an_unfocused_pane_keeps_focus()
    {
        var tree = new PaneTree<string>("a");
        tree.Split(true, "b");
        tree.Split(true, "c");
        Assert.True(tree.Close(tree.Find("a")!));
        Assert.Equal("c", tree.Focused.Content);
    }

    [Fact]
    public void Focus_next_and_previous_wrap()
    {
        var tree = new PaneTree<string>("a");
        tree.Split(true, "b");
        tree.Split(false, "c");
        var focusChanges = 0;
        tree.FocusChanged += () => focusChanges++;

        tree.FocusNext();
        Assert.Equal("a", tree.Focused.Content);
        tree.FocusNext(-1);
        Assert.Equal("c", tree.Focused.Content);
        tree.FocusNext(-1);
        Assert.Equal("b", tree.Focused.Content);
        Assert.Equal(3, focusChanges);
    }

    [Fact]
    public void Ratio_is_clamped()
    {
        var tree = new PaneTree<string>("a");
        tree.Split(true, "b");
        var split = (PaneSplit<string>)tree.Root;
        split.Ratio = 2;
        Assert.Equal(PaneSplit<string>.MaxRatio, split.Ratio);
        split.Ratio = -1;
        Assert.Equal(PaneSplit<string>.MinRatio, split.Ratio);
        split.Ratio = double.NaN;
        Assert.Equal(0.5, split.Ratio);
    }
}
