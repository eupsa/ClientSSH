# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow [SemVer](https://semver.org/).

## [Unreleased]

## [0.2.0] - 2026-10-19

### Added
- WinUI 3 app with splash, welcome screen and guided tour with a simulated host.
- Real terminal: ConPTY running the Windows `ssh.exe`, own VT/ANSI parser, drawing with Win2D.
- Multiple simultaneous sessions in tabs.
- Hosts, favorites and optional history (opt-in, host and time only) in local SQLite.
- Settings: theme, font size, language (pt-BR, en-US), start with Windows, minimize to tray, manual update check.
- Brand palette in one file (`Themes/Palette.xaml`), Lottie animations.
- Setup.exe installer (Inno Setup), per-user.

### Changed
- Update check also runs once after a crash and when Windows starts the app. If a newer release exists, **Update now** downloads the installer, checks it against `SHA256SUMS.txt` and installs it. Nothing installs without a click.
- Task Manager shows the name `ClientSSH` without the version.

### Fixed
- Title bar buttons were invisible in the light theme.
