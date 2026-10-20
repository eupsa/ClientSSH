# ClientSSH

A simple, fast SSH client for Windows. Open source, built with WinUI 3.

[Leia em português](README.pt-BR.md)

## Why

- **Your data stays on your PC.** Hosts and settings are saved locally. There is no account, no cloud and no telemetry.
- **Easy to connect.** Pick a host and connect. Sign-in is handled by the Windows OpenSSH client, so passwords, private keys, the SSH agent, security keys and your `known_hosts` all work as usual.
- **Modern Windows look.** WinUI 3, Fluent controls, light and dark themes, English and Brazilian Portuguese.

## Features

- Host list with favorites and an optional, local connection history (off by default)
- Guided first-run tour (the tour uses a simulated host, no network)
- Real terminal: `ssh.exe` in a Windows pseudo console (ConPTY), drawn with Win2D
- Brand animations (Lottie) for the splash, empty states and connection status
- Tabs: several simultaneous sessions, kept running while you browse hosts and settings
- Settings: language, theme, terminal font size, start with Windows, start minimized, keep in tray when closed
- Update check against the GitHub releases of a repository you can change, with a one-click update (the download is checked against `SHA256SUMS.txt`)

See [ROADMAP.md](ROADMAP.md) for what is planned (visual integration with Windows, and more).

## Privacy

ClientSSH makes no network connection on its own, with two exceptions: the SSH sessions you start, and the update check. The check runs when you click **Check for updates**, and also once after the app closed by an error or when Windows starts it. It asks the GitHub API for the latest release of the repository set in Settings (default `eupsa/client-ssh`) and sends nothing else. If a newer version exists the app only shows a notice; it downloads and installs after you click **Update now**.

Data lives in `%LOCALAPPDATA%\ClientSSH` (`clientssh.db` with hosts, favorites and history, and `settings.json`). Set the `CLIENTSSH_DATA_DIR` environment variable to use another folder.

## Install

Download `ClientSSH-Setup-<version>.exe` from the [Releases](https://github.com/eupsa/client-ssh/releases) page and run it. It installs for your user only (no administrator rights) and does not touch your data on uninstall.

The installer is not code-signed, so Windows SmartScreen may warn about an unknown publisher: choose **More info**, then **Run anyway**. You can verify the build yourself, see [Build the installer](CONTRIBUTING.md#build-the-installer).

## Requirements

- Windows 10 1809 or later (Windows 11 recommended)
- OpenSSH Client (installed by default on Windows 10/11)
- To build: .NET 8 SDK and Visual Studio 2022 or later with the **Windows application development** workload

## Build and run

Open `ClientSSH.slnx` in Visual Studio and press F5, or from a terminal:

```bash
dotnet build ClientSSH.csproj -p:Platform=x64
```

The packaged app must be started from Visual Studio (it needs the MSIX package). To run an unpackaged build for quick checks:

```bash
dotnet build ClientSSH.csproj -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:OutDir=out/
```

Run the tests:

```bash
dotnet test tests/ClientSSH.Tests
```

## Project layout

| Folder | What it holds |
|---|---|
| `Models/` | Plain data: `Host`, `AppSettings` |
| `ViewModels/` | MVVM view models (state and commands, no UI types) |
| `Views/` | XAML pages, dialogs and the Win2D `TerminalView` |
| `Services/Storage` | SQLite (hosts, favorites, opt-in history) and JSON settings in `%LOCALAPPDATA%` |
| `Services/Shell` | Navigation, dialogs, theme, tray icon, start with Windows |
| `Services/Updates` | Update check, verified download, crash marker |
| `Services/Localization` | Reads strings for text produced in code |
| `Services/Sessions` | `SessionManager`: the open tabs and their live sessions |
| `Services/Demo` | The tour's in-memory demo host |
| `Terminal/` | Terminal engine: ConPTY and simulated processes, VT/ANSI parser, ssh command builder |
| `Themes/Palette.xaml` | The single source of colors and shape |
| `Strings/` | `Resources.resw` per language (`pt-BR`, `en-US`) |
| `brand/` | Logo and animation kit |
| `tests/` | xUnit tests for the terminal engine and the SQLite storage |

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md). Issues and pull requests are welcome.

## License

[MIT](LICENSE). Third-party notices are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
