# Repository Guidelines

## Project Structure & Module Organization

- `WinDock.slnx` groups the application, core library, and tests.
- `src/WinDock/` contains the WPF interface, XAML styles, Win32/Shell integration, application catalogue, and managed shortcuts. Keep visual resources alongside the interface.
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

## Testing Guidelines

Tests use custom `Run("behavior description", ...)` groups and `Check` assertions, not a framework or `dotnet test`. Add meaningful regression coverage for changed behavior; no numeric coverage threshold is configured. Use temporary directories, never real user settings. For interaction changes, also verify focus, drag cancellation, screen edges, and mixed DPI manually.

## Commit & Pull Request Guidelines

History currently contains only the initial commit; no strict convention exists. Use concise, descriptive subjects. PRs should explain the problem, resulting behavior, validation, and remaining limitations. Link relevant issues and include screenshots for visual changes.

## Data Safety

Preserve compatibility with `%LocalAppData%\WinDock` settings. Save configuration before deleting managed copies; never modify original shortcuts or targets. Exclude credentials, signing keys, caches, and generated binaries from commits.
