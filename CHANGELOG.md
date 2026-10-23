# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow [SemVer](https://semver.org/).

## [Unreleased]

## [0.6.1] - 2026-10-23

### Fixed
- Remote control: the keyboard never reached the other PC, because the remote screen could not hold keyboard focus. It now takes the keyboard as soon as the other screen appears and when you click it.
- Remote control: releasing a mouse button outside the remote picture left it held down on the other PC.

## [0.6.0] - 2026-10-23

### Added
- **Update now** in Settings: after **Check for updates** finds a new version, one click downloads it, checks it against `SHA256SUMS.txt` and installs it over the current one (hosts and settings are kept).
- **Uninstall** in Settings, and an "Uninstall ClientSSH" entry in the Start menu. Your data folder is kept.

### Fixed
- **Update now** stayed disabled: the repository was renamed to `eupsa/ClientSSH` and the app rejected the installer links under the new name. 0.5.0 cannot update itself because of this, so install 0.6.0 by hand once.

## [0.5.0] - 2026-10-22

### Added
- **Remote access like AnyDesk, with no server of ours.** Each PC has a fixed 9-digit number; type a friend's number and connect. Their PC asks them, even when ClientSSH is minimized or in the tray (window to the front, on top, flashing), and they choose **Allow control**, **View only** or **Deny**, after comparing a six-digit security code. PCs find each other through the public BitTorrent DHT (and on the same network by local multicast), then connect directly (UDP hole punching), end-to-end encrypted. Video is H.264 up to Full HD at 60 fps, with the codecs built into Windows and Desktop Duplication capture. It may not connect through CGNAT or symmetric NAT, your public IP is published with your number while requests are accepted (can be turned off), and the protocol has had no outside security review.
- **Command palette:** press **Ctrl+Shift+P** (or the search button in the title bar) and type to run any action: open SSH to a saved host (most recent first when history is on), new connection, add host, close the current tab, restore tabs closed in this run, switch theme (light, dark, follow Windows), go to hosts, sessions, remote access or settings, and check for updates. Fuzzy search ignores case and accents; arrow keys, Enter and Esc work as usual, and screen readers hear the highlighted command. Closed tabs are remembered in memory only, never saved. It also lists tab and pane actions (new tab, split, close pane, next pane, next/previous tab, rename) with their shortcuts, and "Open terminal" for each local shell.
- **Split panes.** Split a tab vertically or horizontally (tab bar buttons, the tab's right-click menu or shortcuts); each pane runs its own session to the same host. Drag the divider to resize; the focused pane has an accent outline. Only the visible tab's panes keep a terminal canvas.
- **Rename tabs** with a double click on the title or from the tab's menu. The name lasts while the tab is open.
- **Configurable shortcuts** for new/close/next/previous tab, split, close pane, next pane and rename (Settings > Keyboard shortcuts). Only changes from the defaults are saved (`Shortcuts` in `settings.json`); older settings files keep loading.
- **Local terminals:** Windows PowerShell, PowerShell 7, CMD, each WSL distro and Git Bash open in tabs, from the Hosts page or the tab bar's **+** menu. Only shells installed on the PC are listed.
- **Terminal customization** in Settings > Terminal: color scheme (brand plus 8 built-in, and your own in `schemes.json`, Windows Terminal format), font and size, cursor shape and thickness, background opacity. A global default plus per-profile overrides (including "SSH hosts"); open terminals update live. New hosts can use a profile's look.
- **Window background:** brand gradient, Mica or Acrylic.
- **Profile shortcuts:** Ctrl+Shift+1..9 open profiles 1-9, each editable or off.

### Changed
- The terminal font size moved from Settings > Appearance into the new Terminal section (old settings files keep their size).
- Faster terminal with long output: the 5000-line scrollback is now a ring buffer that reuses lines (in a 200,000-line test, memory allocated went from 581 MB to 14.5 MB).
- The terminal is drawn from a copy of the visible cells, so reading SSH output no longer waits for every frame to be drawn.
- Remote access video reuses its buffers: about 3 MB less memory per encoded frame and 11 MB less per decoded frame at 1080p.
- Large source files were split by responsibility, and view models now depend on service interfaces (no change in behavior).

### Removed
- The two codes to swap by hand and the optional rendezvous server for remote access: the PC number replaces both.

## [0.4.0] - 2026-10-22

### Added
- Remote access between two PCs with no server of ours: swap two short spoken codes (like `bako rimu sela fidu nomi ta`), end-to-end encrypted, with a six-digit code to compare out loud and a per-session choice of view only, control or deny.
- AI assistant side panel (Claude, ChatGPT, Gemini, Groq): describe what you want and the command is typed at the terminal prompt without Enter, so you check it before running. Uses your own API key, encrypted with Windows DPAPI. Only your text and a fixed instruction are sent, never terminal output, hosts or history. "List available models" asks your provider which models your key can use.
- 12 new languages: Spanish, French, German, Italian, Russian, Chinese (Simplified), Japanese, Korean, Arabic, Hindi, Turkish and Dutch, with flags in the language picker. Arabic mirrors the interface. These translations were machine-made: corrections are welcome.
- Paste in the terminal with Ctrl+V, Ctrl+Shift+V or right click. Pasted text is cleaned of control characters.
- The window opens centered the first time, and afterwards where and how big you left it (position, size, maximized). If a monitor is gone, it falls back to the center of the main one.
- Only one ClientSSH runs at a time: opening it again brings the running window to the front (also from the tray or minimized). A separate data folder still runs on its own.
- The installer wizard follows the Windows language: English, Portuguese (Brazil), Spanish, French, German, Italian, Russian, Japanese, Korean, Arabic, Turkish and Dutch. Chinese and Hindi fall back to English (Inno Setup has no translation for them).

### Changed
- The "+" button in the tab bar lists your saved hosts next to it and opens the new tab there, instead of going back to the hosts page.
- Lighter idle terminal: the cursor blink no longer redraws when there is no cursor to blink, and rows are drawn without per-row allocations.
- Connection animations are created only while they are visible, instead of two players for every open tab.
- One shared Windows animation-settings object instead of one per animation load.

### Fixed
- Clicking the terminal with the left mouse button now gives it keyboard focus (only right click did before).

## [0.3.0] - 2026-10-20

### Added
- WinUI 3 app with splash, welcome screen and guided tour with a simulated host.
- Real terminal: ConPTY running the Windows `ssh.exe`, own VT/ANSI parser, drawing with Win2D.
- Multiple simultaneous sessions in tabs.
- Hosts, favorites and optional history (opt-in, host and time only) in local SQLite.
- Settings: theme, font size, language (pt-BR, en-US), start with Windows, minimize to tray, manual update check.
- Brand palette in one file (`Themes/Palette.xaml`), Lottie animations.
- Setup.exe installer (Inno Setup), per-user.

### Changed
- Update check also runs every time the app opens (can be turned off in Settings; a run after a crash still checks once). If a newer release exists, **Update now** downloads the installer, checks it against `SHA256SUMS.txt` and installs it. Nothing installs without a click.
- Task Manager shows the name `ClientSSH` without the version.

### Fixed
- Title bar buttons were invisible in the light theme.
