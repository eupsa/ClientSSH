using ClientSSH.Services.Remote;

// ClientSSH.Rendezvous.exe [port]   (default 47800, TCP). Open that port in the firewall and router of this PC.
var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : RendezvousServer.DefaultPort;
using var server = new RendezvousServer(port);
Console.WriteLine($"ClientSSH rendezvous listening on TCP {server.Port}. It keeps only short-lived public codes in memory. Ctrl+C to stop.");
await Task.Delay(Timeout.Infinite);
