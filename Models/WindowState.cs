namespace ClientSSH.Models;

/// <summary>Where the main window was left, in physical pixels. Bounds are the normal (not maximized) ones.</summary>
public sealed record WindowState(int X, int Y, int Width, int Height, bool Maximized);
