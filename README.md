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
- Split panes: split a tab vertically or horizontally (each pane runs its own session), drag the divider to resize, rename tabs (double click or the tab's menu)
- Keyboard shortcuts for tabs and panes, configurable in Settings: new tab `Ctrl+Shift+T`, close tab `Ctrl+Shift+W`, next/previous tab `Ctrl+Tab`/`Ctrl+Shift+Tab`, split vertically `Ctrl+Shift+E`, split horizontally `Ctrl+Shift+O`, close pane `Ctrl+Shift+X`, next pane `Ctrl+Shift+N`, rename tab `Ctrl+Shift+R`
- Settings: language, theme, terminal font size, start with Windows, start minimized, keep in tray when closed
- Update check against the GitHub releases of a repository you can change, with a one-click update (the download is checked against `SHA256SUMS.txt`)

See [ROADMAP.md](ROADMAP.md) for what is planned (visual integration with Windows, and more).

## Privacy

ClientSSH makes no network connection on its own, with four exceptions: the SSH sessions you start, the update check, the AI assistant (only when you send it a prompt) and remote access (while **Accept access requests** is on, the default, the app keeps this PC findable by its number in the public BitTorrent DHT; see below). The check runs when you click **Check for updates** and every time the app opens (you can turn the automatic one off in Settings). It asks the GitHub API for the latest release of the repository set in Settings (default `eupsa/client-ssh`) and sends nothing else. If a newer version exists the app only shows a notice; it downloads and installs after you click **Update now**.

Data lives in `%LOCALAPPDATA%\ClientSSH` (`clientssh.db` with hosts, favorites and history, and `settings.json`). Set the `CLIENTSSH_DATA_DIR` environment variable to use another folder.

## Install

Download `ClientSSH-Setup-<version>.exe` from the [Releases](https://github.com/eupsa/client-ssh/releases) page and run it. The installer speaks your Windows language when it is one of 12 (English otherwise). It installs for your user only (no administrator rights) and does not touch your data on uninstall.

The installer is not code-signed, so Windows SmartScreen may warn about an unknown publisher: choose **More info**, then **Run anyway**. You can verify the build yourself, see [Build the installer](CONTRIBUTING.md#build-the-installer).

## Remote access (like AnyDesk, with no server)

See and control another Windows PC, or let someone help you on this one, **directly between the two PCs**: no ClientSSH server, no account, no relay.

1. Every installation gets a fixed **PC number** (9 digits, like `847 291 503`), shown under **Remote** > **This PC**. It comes from a key created once on this PC (kept encrypted with Windows DPAPI), not from hardware ids, and it stays the same across updates.
2. Your friend gives you their number. Type it under **Access another PC** and press **Connect**.
3. On their PC a request pops up, even when ClientSSH is minimized or in the tray: the window comes to the front, stays on top and the taskbar button flashes. They choose **Allow control**, **View only** or **Deny**. Both screens show the same six-digit **security code**: read it out loud to each other, and if it differs, deny.
4. While a screen is shared, a **Stop sharing** banner stays visible on every page of the shared PC.

The picture is H.264 at up to 1920 x 1080 and 60 frames per second, encoded and decoded by the codecs built into Windows (Media Foundation) and captured with Desktop Duplication; only changes are sent, so a still screen costs almost nothing. A busy screen uses about 3 to 8 Mbit/s.

How a PC is found with no server: while **Accept access requests** is on (the default), ClientSSH stores this PC's public address under a key derived from its number in the public BitTorrent DHT, the network of millions of PCs that BitTorrent clients use to find each other, with no owner and no central server. A caller looks the number up there, leaves its own address under a second key the called PC watches, and both send packets to each other at once to open the way through their routers (UDP hole punching). On the same network, the two PCs also find each other by a local multicast question. Only the PC that owns the number can answer: it signs the handshake with the key behind the number. Everything after that is end to end: one-time keys per session (ECDH P-256 + HKDF-SHA256), every packet encrypted and authenticated (AES-256-GCM).

Limits, honestly:
- **Without any server it cannot work on every network.** If one side uses CGNAT or a symmetric NAT (common on mobile data and some ISPs), the direct path may never open; AnyDesk solves that with relay servers, which ClientSSH does not have. The app says so when it happens. Try another network or a phone hotspot.
- **Your public IP address is published with your number** in the DHT while requests are accepted, so anyone who knows (or guesses) your number can learn your IP address. They still cannot see anything without your OK. Turn **Accept access requests** off to stop publishing.
- Requests only arrive while ClientSSH runs (open or in the tray). Windows may ask once to allow ClientSSH on the network.
- Main monitor only, no sound, no clipboard or file transfer yet. The keyboard is sent as key codes, so different layouts on the two PCs can type different characters. Windows does not allow input into administrator windows or the secure desktop (UAC prompts).
- The protocol is new and has had no outside security review. Review is welcome, see [SECURITY.md](SECURITY.md).

## Window

The window opens centered the first time and then remembers its position, size and maximized state.

## Local terminals and customization

Besides SSH hosts, the Hosts page lists the shells found on this PC: Windows PowerShell, PowerShell 7, CMD, each WSL distro and Git Bash (only the ones installed). One click opens it in a tab; the tab bar's **+** menu lists them too, and **Ctrl+Shift+1..9** opens profile 1 to 9 (change or turn off each shortcut in Settings).

In **Settings > Terminal** pick the window background (brand gradient, Mica or Acrylic) and the terminal look: color scheme (the brand one plus Campbell, One Half, Solarized, Dracula, Nord, Tango), monospace font and size, cursor shape (block, bar, underline) and thickness, and background opacity. Set it once as the default for every terminal, or customize one profile (or "SSH hosts"). A new host can use a profile's look. Your own schemes go in `schemes.json` in the data folder, in the Windows Terminal format (an array of schemes, or `{ "schemes": [...] }`).

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
| `ViewModels/` | MVVM view models (state and commands); they depend on service interfaces registered in `App.xaml.cs` |
| `Views/` | XAML pages and dialogs; code-behind holds only view concerns. The Win2D `TerminalView` is split into lifecycle, `.Drawing.cs` and `.Input.cs`, with `TerminalPalette`, `CursorBlink` and `TerminalKeys` beside it |
| `Services/Storage` | SQLite (hosts, favorites, opt-in history) and JSON settings in `%LOCALAPPDATA%` |
| `Services/Shell` | Navigation, dialogs, theme, tray icon, start with Windows |
| `Services/Updates` | Update check, verified download, crash marker |
| `Services/Localization` | Reads strings for text produced in code |
| `Services/Sessions` | `SessionManager`: the open tabs, their panes (`PaneTree`) and live sessions |
| `Services/Shortcuts` | `KeyChord` and `ShortcutMap`: the configurable tab and pane shortcuts |
| `Services/Demo` | The tour's in-memory demo host |
| `Services/Assistant` | AI command assistant: `AssistantService`, per-provider formats in `AiRequests`, DPAPI key store |
| `Terminal/` | Terminal engine: ConPTY (`PseudoConsole`, P/Invoke in `.Native.cs`) and simulated processes, `VtScreen` (buffer, `.Parser.cs`, `Cell.cs`), ssh command builder |
| `Services/Customization` | Local shell detection and terminal appearance (schemes, per-profile overrides) |
| `Services/Shortcuts` | Configurable key chords (open profile N) |
| `Themes/Palette.xaml` | The single source of colors and shape |
| `Strings/` | `Resources.resw` per language (`pt-BR`, `en-US`) |
| `brand/` | Logo and animation kit |
| `tests/` | xUnit tests for the terminal engine and the SQLite storage |

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md). Issues and pull requests are welcome.

## License

[MIT](LICENSE). Third-party notices are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
