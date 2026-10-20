# Contributing

Thanks for helping. This is a small project, so the rules are short.

## Setup

1. Install the .NET 8 SDK and Visual Studio with the **Windows application development** workload.
2. Open `ClientSSH.slnx` and press F5 (the packaged app must run from Visual Studio).
3. Run `dotnet test tests/ClientSSH.Tests` before opening a pull request.

## Conventions

- **MVVM.** View models hold state and commands and use no UI types (the few that do are noted in code). Code-behind is only view glue: events, focus, animations.
- **No fixed text.** Every user-facing string lives in `Strings/<language>/Resources.resw`, used through `x:Uid` or `Loc.Get`. Add each new key to **both** `pt-BR` and `en-US`.
- **Colors and shape come from `Themes/Palette.xaml`.** Use `{ThemeResource XxxBrush}` and `CardCornerRadius`/`ControlCornerRadius`, never literal colors or radii. The app uses the brand palette: dark blue, black and white.
- **Icons have no background of their own.** They sit on the background of the component they are in.
- **Animate with native transitions** (`EntranceThemeTransition`, `ScalarTransition`, `NavigationTransitionInfo`) instead of custom code, and with the brand Lottie animations through `LottieView` (`brand/anim`).
- **Privacy first.** Nothing may leave the PC on its own. Network use must be started by the user and documented in the README.
- **Validate input that reaches a process or a URL.** See `SshCommand` and `UpdateService` for the pattern.
- **Test logic.** Terminal engine and storage changes need a case in `tests/ClientSSH.Tests`.
- **Test with a separate profile.** Set `CLIENTSSH_DATA_DIR` to a scratch folder so your own hosts are never touched.
- **Comments explain why**, not what. Code is in English.

## Build the installer

Needs the .NET 8 SDK and [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`):

```bash
powershell -File installer/build.ps1 -Version 0.4.0
```

This publishes an unpackaged, self-contained build and writes `artifacts/ClientSSH-Setup-<version>.exe`. Run it afterwards with a normal packaged rebuild if you keep working in Visual Studio, because the publish changes `obj/`. Releases are published by hand on GitHub with a `vX.Y.Z` tag; the in-app update check reads the latest one.

## Reporting and conduct

Bugs and ideas go in [issues](https://github.com/eupsa/client-ssh/issues). Security problems: see [SECURITY.md](SECURITY.md). Everyone follows the [Code of Conduct](CODE_OF_CONDUCT.md).

## Project layout

See the table in [README.md](README.md#project-layout).

## Pull requests

- Keep them small and focused.
- Describe what changed and how you tested it.
- UI changes: attach a screenshot in both themes.
