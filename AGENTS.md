# Repository Guidelines

## Project Structure & Module Organization

- `WinDock.slnx` groups the application, core library, and tests.
- `src/WinDock/` contains the WPF interface, XAML styles, Win32/Shell integration, application catalogue, and managed shortcuts. Keep visual resources alongside the interface.
- `src/WinDock/Assets/` contains the source PNG and multi-resolution ICO for the application identity. `DockLabels` handles hover labels, `RecycleBinService` wraps the Windows recycle bin behind `IRecycleBinService`, and `StartupRegistration` manages the current user's startup shortcut.
- `src/WinDock.Core/` contains settings, persistence, entry rules, and placement geometry. Coordinates here are physical desktop pixels, including negative monitor origins.
- `tests/WinDock.Tests/Program.cs` contains the executable test harness.
- `artifacts/` holds generated previews and portable builds; it is ignored by Git. `README.md` and `QUICKSTART.txt` document usage.

## Build, Test, and Development Commands

Use Windows x64 with a stable .NET 10 SDK; `global.json` permits newer .NET 10 feature bands. Run commands from the repository root:

```powershell
dotnet build WinDock.slnx -c Release -m:1
dotnet run --project src/WinDock -c Release
dotnet run --project tests/WinDock.Tests -c Release -p:BuildInParallel=false
dotnet publish src/WinDock/WinDock.csproj -c Release -r win-x64 --self-contained true -o artifacts/WinDock-win-x64 -m:1
```

These compile, launch, test, and publish respectively. Tests require an interactive Windows session. After building tests, append `--no-build -- --preview artifacts/previews` to the test run command for screenshots, or `--no-build -- --catalog-smoke artifacts/previews` for installed-application checks; the latter opens Calculator.

## Coding Style & Naming Conventions

Follow existing C# conventions: four-space indentation, file-scoped namespaces, PascalCase types/methods/properties, and camelCase parameters/local variables/private fields. Match nearby XAML formatting. Nullable references and implicit usings are enabled; warnings are errors. Respect `.gitattributes` line endings. No dedicated formatter or linter is configured.

Keep Shell COM work on background STA workers and UI updates on the WPF dispatcher. Freeze images before sharing across threads. Preserve French user-facing text.

Periodic positioning must preserve window Z-order and avoid activation (`SWP_NOZORDER | SWP_NOACTIVATE`) so the dock does not cover its menus. Activate settings only in response to the user opening them; do not keep them permanently topmost. For WPF window icons, explicitly select the largest ICO frame: loading the first frame with `BitmapImage` can select only 16 × 16 pixels.

## Testing Guidelines

Tests use custom `Run("behavior description", ...)` groups and `Check` assertions, not a framework or `dotnet test`. Add meaningful regression coverage for changed behavior; no numeric coverage threshold is configured. Use temporary directories, never real user settings. For interaction changes, also verify focus, drag cancellation, screen edges, and mixed DPI manually.

Use a fake `IRecycleBinService` for recycle bin actions and state transitions. Automated tests must never empty the real recycle bin or change the account's startup registration; use a temporary startup directory. Preview generation may read the real recycle bin state and system icon, but must not modify its contents. Cover fixed-mode/full-screen transitions, menu Z-order, label placement and settings activation when changing these behaviors.

## Beta Delivery

Set the release version in `src/WinDock/WinDock.csproj` and keep the banner in `QUICKSTART.txt` aligned. The settings window reads the assembly informational version; do not hardcode a separate UI version. Update usage documentation for behavior changes.

Build Release, run the executable test harness, and generate/review previews for visual changes. Publish self-contained Windows x64 output to a fresh `artifacts/WinDock-<version>-win-x64` directory, using `-p:DebugType=None -p:DebugSymbols=false`, then archive the complete directory as `WinDock-<version>-win-x64.zip`. Keep the generated package out of Git. Report validation results and manual hardware checks still outstanding; do not imply that mixed-DPI testing was completed when only geometry tests ran.

## Commit & Pull Request Guidelines

Use concise, descriptive commit subjects. PRs should explain the problem, resulting behavior, validation, and remaining limitations. Link relevant issues and include screenshots for visual changes.

## Data Safety

Preserve compatibility with `%LocalAppData%\WinDock` settings. Save configuration before deleting managed copies; never modify original shortcuts or targets. Exclude credentials, signing keys, caches, and generated binaries from commits.
