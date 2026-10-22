# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow [SemVer](https://semver.org/).

## [Unreleased]

## [0.5.0] - 2026-10-22

### Changed
- **Remote access like AnyDesk, with no server of ours.** Each PC has a fixed 9-digit number; type a friend's number and connect. Their PC asks them, even when ClientSSH is minimized or in the tray (window to the front, on top, flashing), and they choose **Allow control**, **View only** or **Deny**, after comparing a six-digit security code. PCs find each other through the public BitTorrent DHT (and on the same network by local multicast), then connect directly (UDP hole punching), end-to-end encrypted. Video is H.264 up to Full HD at 60 fps, with the codecs built into Windows and Desktop Duplication capture. It may not connect through CGNAT or symmetric NAT, your public IP is published with your number while requests are accepted (can be turned off), and the protocol has had no outside security review.

### Removed
- The two codes to swap by hand and the optional rendezvous server: the PC number replaces both.

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
