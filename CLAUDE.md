# Wolffiles Uploader — Claude Code Project Context

> **Always read this file first when starting a session.**

---

## What this is

A native Windows desktop app (WinUI 3 / .NET 8) for uploading game files (.pk3, .zip, etc.) to **wolffiles.eu** — a community platform for Wolfenstein: Enemy Territory & Return to Castle Wolfenstein. Replaces the web upload form with a faster multi-file workflow: drag & drop, per-file metadata forms, queued uploads with progress.

Backend lives at `https://wolffiles.eu/api/v1` (Laravel 12, Sanctum token auth). API routes are deployed and live — do not regenerate them.

---

## Tech stack

- **WinUI 3** + **.NET 8** (`net8.0-windows10.0.19041.0`)
- **MVVM** via `CommunityToolkit.Mvvm` 8.2.2 (`[ObservableProperty]`, `[RelayCommand]`, `ObservableObject`)
- **DI** via `Microsoft.Extensions.DependencyInjection` — wire-up in `App.xaml.cs`
- **HTTP**: shared `HttpClient` singleton via `Microsoft.Extensions.Http`; token is **static** in `WolffilesApiService`
- **Multilingual**: `.resw` resource files in `Strings/{de-DE,en-US,fr-FR}/Resources.resw`
- **Platforms**: `x86;x64` (no ARM64 yet — see Build section)
- **Package**: `Microsoft.WindowsAppSDK` 1.5.240802000, `WindowsAppSDKSelfContained=true`

---

## Folder layout

```
wolffiles-uploader/
├── App.xaml(.cs)              # DI container setup, HttpClient registration
├── MainWindow.xaml(.cs)       # Hosts ShellPage
├── WolffilesUploader.csproj
├── Assets/icon.ico
├── Models/
│   └── Models.cs              # UserInfo, Category, CategoryOption, UploadItem, HistoryItem, UploadStats
├── Services/
│   ├── WolffilesApiService.cs # all API calls (auth, categories, upload, history)
│   ├── AuthService.cs
│   ├── UploadHistoryService.cs
│   └── UpdateCheckerService.cs # GitHub Releases API
├── ViewModels/
│   ├── LoginViewModel.cs
│   ├── RegisterViewModel.cs
│   ├── UploadQueueViewModel.cs   # core upload logic
│   ├── HistoryViewModel.cs
│   └── SettingsViewModel.cs
├── Views/
│   ├── LoginPage.xaml(.cs)
│   ├── RegisterPage.xaml(.cs)
│   ├── ShellPage.xaml(.cs)        # NavigationView host
│   ├── UploadQueuePage.xaml(.cs)  # main screen — drag&drop, queue list
│   ├── HistoryPage.xaml(.cs)
│   └── SettingsPage.xaml(.cs)
├── Converters/
│   └── Converters.cs           # registered in App.xaml as static resources
├── Strings/
│   ├── de-DE/Resources.resw    # German (master)
│   ├── en-US/Resources.resw
│   └── fr-FR/Resources.resw
└── .github/workflows/release.yml # MSIX + portable ZIP build on git tag v*.*.*
```

---

## Build commands (Windows, PowerShell)

```powershell
cd C:\Dev\wolffiles-uploader

# Debug build (always specify Platform explicitly — csproj forces win-x64 runtime)
dotnet build -c Debug -p:Platform=x64

# Run
dotnet run -c Debug -p:Platform=x64

# Portable single-file EXE for testing
dotnet publish WolffilesUploader.csproj `
  -c Release -r win-x64 -p:Platform=x64 `
  -p:PublishSingleFile=true -p:SelfContained=true `
  -p:WindowsPackageType=None `
  -o publish\portable
```

**Common gotchas:**
- Skipping `-p:Platform=x64` on an ARM64 host → `NETSDK1032` (runtime/platform mismatch)
- New Tailwind-style class names in XAML resources need `App.xaml` registration
- Converters must be registered in `App.xaml` as static resources before XAML can use them
- `ApplicationData.Current.LocalSettings` does NOT work in unpackaged builds → use file-based storage (`%LOCALAPPDATA%\WolffilesUploader\`)
- `dotnet workload install windows` does NOT exist — for WinUI 3 / .NET 8 the SDK comes via NuGet `Microsoft.WindowsAppSDK`, no workload needed

---

## API contract (live on wolffiles.eu)

Base URL: `https://wolffiles.eu/api/v1`
Auth: Bearer token (Laravel Sanctum) — token name `wolffiles-uploader`

| Method | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/auth/login` | no | `{email, password}` → `{token, user}` (email OR username works) |
| POST | `/auth/register` | no | `{name, email, password, password_confirmation}` → `{token, user}` |
| GET | `/auth/me` | yes | `{data: UserInfo}` |
| POST | `/auth/logout` | yes | revokes current token |
| GET | `/categories` | no | `{data: [{id, name, slug, children: [...]}]}` — flatten to `CategoryOption` |
| POST | `/files` | yes | multipart/form-data: `file, title, description, category_id, version, author, screenshot` — returns `{data: {id, title, status, url}}` with `status='pending'` |
| GET | `/files/my` | yes | paginated list of user's uploads |

**Cloudflare note:** wolffiles.eu is behind Cloudflare with Bot Fight Mode + AI Labyrinth. The `.NET HttpClient` is challenged unless `/api/*` is exempted via a Configuration Rule (Bot Fight Mode: Off, Browser Integrity Check: Off, Security Level: Essentially Off).

---

## Conventions

- **Language**: code/comments in English; UI strings in `.resw` (DE master, then EN/FR)
- **MVVM**: never put business logic in code-behind beyond event handler plumbing. ViewModels are constructor-injected via `App.Services.GetRequiredService<>()`
- **Bindings**: `x:Bind` preferred over `Binding` for compile-time safety; `Mode=OneWay` for read-only props, `Mode=TwoWay` for inputs
- **Async**: all I/O is async; UI updates only via DispatcherQueue if from background threads
- **Errors**: surface via `UploadItem.ErrorMessage` / `StatusMessage` properties — no MessageBox spam
- **DI registration**: every new service goes into `App.xaml.cs` `ConfigureServices`
- **Static resources** for brushes/colors live in `App.xaml` (e.g. `BgSurfaceBrush`, `AccentGoldBrush`, `TextPrimaryBrush`)
- **Color palette** (dark military/gold):
  - `BgSurface` `#0F1117`, `BgElevated` `#1A1D2A`
  - `TextPrimary` `#E4E6EB`, `TextSecondary` `#A0A4B8`, dim `#424860`
  - `AccentGold` `#E0A030` (primary action), `StatusRed` for errors

---

## Known issues / tech debt

- `Services/WolffilesApiService.cs` still contains `ProgressableContent` class as dead code — was disabled because it corrupted multipart boundary headers. Real upload progress is currently fake (jumps 10 → 100). Removing the class is safe.
- The `BrowseFiles_Click` handler in `UploadQueuePage.xaml.cs` shows a debug `ContentDialog` listing categories — should be removed.
- No proper retry / resumable upload for >100 MB files. Server limit is 5 GB; very large uploads may time out (HttpClient timeout = 300 s).
- ARM64 builds disabled in csproj. To enable: add `arm64` to `<Platforms>` and `win-arm64` to `<RuntimeIdentifiers>`.

---

## When in doubt

- The PLAN.md in repo root contains the original full project plan. Note: significant parts marked "geplant" there are actually done (RegisterPage, .resw, UpdateChecker, SettingsPage, GitHub Actions). Update PLAN.md as features land.
- For UI feature work, see `TASKS-UI-PARITY.md` — the current focus is matching the wolffiles.eu web upload form feature-by-feature.
