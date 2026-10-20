using System.Text.Json.Serialization;
using ClientSSH.Models;
using ClientSSH.Services.Updates;

namespace ClientSSH.Services.Storage;

// Source generation keeps JSON working with PublishTrimmed in Release.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<Host>))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(ReleaseInfo))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
