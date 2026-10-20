using ClientSSH.Models;
using ClientSSH.Services.Localization;

namespace ClientSSH.Services.Demo;

/// <summary>The tour's host. It lives only in memory and is never written to hosts.json.</summary>
public static class DemoHost
{
    public static readonly Guid Id = new("00000000-0000-0000-0000-00000000d3a0");

    public static Host Host { get; } = new(Id, Loc.Get("Demo_HostName"), "demo.local", 22, "demo");

    public static bool Is(Host? host) => host?.Id == Id;
}
