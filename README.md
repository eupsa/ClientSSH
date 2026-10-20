# ClientSSH

A simple, fast SSH client for Windows. Open source, built with WinUI 3.

[Leia em português](README.pt-BR.md)

## Why

- **Your data stays on your PC.** Hosts and settings are saved locally. There is no account, no cloud and no telemetry.
- **Easy to connect.** Pick a host and connect. Sign-in is handled by the Windows OpenSSH client, so passwords, private keys, the SSH agent, security keys and your `known_hosts` all work as usual.
- **Modern Windows look.** WinUI 3, Fluent controls, light and dark themes, 14 languages (Brazilian Portuguese, English, Spanish, French, German, Italian, Russian, Chinese, Japanese, Korean, Arabic, Hindi, Turkish, Dutch; translations beyond pt-BR and en-US are machine-made, corrections welcome).

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

ClientSSH makes no network connection on its own, with three exceptions: the SSH sessions you start, the update check, and the AI assistant, which only runs when you send it a prompt. The check runs when you click **Check for updates** and every time the app opens (you can turn the automatic one off in Settings). It asks the GitHub API for the latest release of the repository set in Settings (default `eupsa/client-ssh`) and sends nothing else. If a newer version exists the app only shows a notice; it downloads and installs after you click **Update now**.

Data lives in `%LOCALAPPDATA%\ClientSSH` (`clientssh.db` with hosts, favorites and history, and `settings.json`). Set the `CLIENTSSH_DATA_DIR` environment variable to use another folder.

## Install

Download `ClientSSH-Setup-<version>.exe` from the [Releases](https://github.com/eupsa/client-ssh/releases) page and run it. It installs for your user only (no administrator rights) and does not touch your data on uninstall.

The installer is not code-signed, so Windows SmartScreen may warn about an unknown publisher: choose **More info**, then **Run anyway**. You can verify the build yourself, see [Build the installer](CONTRIBUTING.md#build-the-installer).

## Window

The window opens centered the first time and then remembers its position, size and maximized state.

## AI assistant

Optional. Open the panel from the tab bar, pick Claude, ChatGPT, Gemini or Groq, paste your own API key and describe what you want. The command the model returns is typed at the terminal prompt **without Enter**, so you read it and decide. Only your text and a fixed instruction are sent to the provider you chose: never terminal output, hosts or history. The key is stored encrypted with Windows DPAPI for your account and never leaves the PC except in the request to that provider.

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
