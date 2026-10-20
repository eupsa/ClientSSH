# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow [SemVer](https://semver.org/).

## [Unreleased]

## [0.4.0] - 2026-10-20

### Added
- AI assistant side panel (Claude, ChatGPT, Gemini, Groq): describe what you want and the command is typed at the terminal prompt without Enter, so you check it before running. Uses your own API key, encrypted with Windows DPAPI. Only your text and a fixed instruction are sent, never terminal output, hosts or history.
- The window opens centered the first time, and afterwards where and how big you left it (position, size, maximized). If a monitor is gone, it falls back to the center of the main one.

- Paste in the terminal with Ctrl+V, Ctrl+Shift+V or right click. Pasted text is cleaned of control characters.
- 12 new languages: Spanish, French, German, Italian, Russian, Chinese (Simplified), Japanese, Korean, Arabic, Hindi, Turkish and Dutch, with flags in the language picker. Arabic mirrors the interface. These translations were machine-made: corrections are welcome.

- Only one ClientSSH runs at a time: opening it again brings the running window to the front (also from the tray or minimized). A separate data folder still runs on its own.
- AI assistant: "List available models" asks your provider which models your key can use, so a retired default is easy to replace.

### Changed
- The "+" button in the tab bar lists your saved hosts next to it and opens the new tab there, instead of going back to the hosts page.
- Lighter idle terminal: the cursor blink no longer redraws when there is no cursor to blink, and rows are drawn without per-row allocations.
- Connection animations are created only while they are visible, instead of two players for every open tab.
- One shared Windows animation-settings object instead of one per animation load.

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
