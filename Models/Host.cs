namespace ClientSSH.Models;

/// <param name="Favorite">Pinned to the top of the list.</param>
/// <param name="LastConnectedUtc">From the connection history; null when history is off or the host was never used.</param>
public sealed record Host(
    Guid Id,
    string Name,
    string Address,
    int Port,
    string User,
    string? Group = null,
    bool Favorite = false,
    DateTime? LastConnectedUtc = null);
