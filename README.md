# Open OTS Tray

[![Build](https://github.com/felip3costa/openotstray/actions/workflows/build.yml/badge.svg)](https://github.com/felip3costa/openotstray/actions/workflows/build.yml)

A Windows system tray app (WPF, .NET) that generates [OneTimeSecret](https://onetimesecret.com) links directly from a flyout panel near your system tray — no browser tab needed.

## Disclaimer

This application is an independent, open-source project that uses the official OneTimeSecret API.

This is not an official OneTimeSecret application and is not affiliated with, endorsed by, or maintained by OneTimeSecret.

The software is provided "as is", without warranties or guarantees of any kind. No technical support, maintenance, or assistance is provided for this application.

Please use the application at your own discretion and always handle sensitive information responsibly.

## Features

- **Flyout UI** anchored near the tray icon (click the tray icon, left or right, to open/close it) — no separate windows to manage.
- **Selected-text hotkey** — select text in any app, press a global shortcut (`Alt+C` by default, configurable in Settings), and it's replaced in place with a one-time link. A tray notification confirms what happened, and the generated link is added to history like any other. Works by simulating Copy/Paste, so it needs the target app to support the clipboard for its selection (most do) and can't reach into windows running elevated (as Administrator) from a non-elevated instance of this app.
- **Quick One Time** (default tab) — paste or type any text/secret and generate a link in one click.
- **Generate** — create one or more random passwords at once, each with its own one-time link.
- **Latest Link** — history of recently generated links (this session and restored from disk), with Copy Password / Copy Link / Copy Passphrase buttons and a one-click "Send Email" action. A lock icon shows whether each link has already been opened, checked automatically whenever the flyout opens.
- **Settings** (via the gear icon) — password length, region, start-with-Windows, the selected-text hotkey, a customizable email template, and a "Clear saved link history" button.
- **About** (via the gear icon) — version and disclaimer info.
- Single instance — launching it twice just focuses the existing tray icon instead of running a second copy.
- Zero external NuGet dependencies — only what ships with the .NET SDK (WPF, WinForms' `NotifyIcon`, `HttpClient`, `System.Text.Json`, `Microsoft.Win32.Registry`, DPAPI).

## Requirements

- Windows
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build. To just *run* the published app, only the **.NET 10 Desktop Runtime** is needed on the target machine — if it's missing, double-clicking the exe shows Windows' built-in prompt offering to install it.

That's the *only* prerequisite — the project has zero external NuGet dependencies, so `dotnet restore` doesn't pull anything beyond what the SDK already has.

### Check before you build

Open PowerShell and run:

```powershell
dotnet --list-sdks
```

You're good to go if a line starting with `10.` shows up (e.g. `10.0.401 [C:\Program Files\dotnet\sdk]`). If the command isn't recognized at all, or no `10.x` entry is listed, install the SDK first — see below.

### Install the .NET 10 SDK

**Option A — winget** (built into Windows 10 21H2+ and Windows 11):

```powershell
winget install Microsoft.DotNet.SDK.10
```

**Option B — official install script** (if winget isn't available):

```powershell
irm https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
.\dotnet-install.ps1 -Channel 10.0 -InstallDir "$env:ProgramFiles\dotnet"
```

After installing, close and reopen your terminal, then re-run `dotnet --list-sdks` to confirm.

## Build & run (development)

```bash
cd OpenOTSTray
dotnet build
dotnet run
```

## Continuous integration

Every push and pull request builds on a `windows-latest` GitHub Actions runner (see [`.github/workflows/build.yml`](.github/workflows/build.yml)): restore, build (Release), then `dotnet publish` a framework-dependent single-file `OpenOTSTray.exe`, uploaded as a workflow artifact you can download from the [Actions tab](https://github.com/felip3costa/openotstray/actions).

This also lays the groundwork for code signing the published exe via [SignPath](https://signpath.io) (free for qualifying open-source projects), which signs artifacts produced by a CI pipeline rather than local builds.

## Installing

`OpenOTSTray.exe` is portable — no installer. The first time you run it from anywhere other than its permanent home, it asks:

> Open OTS Tray is running from a temporary location. Install it to your Programs folder and start it automatically with Windows? You won't need to find this file again afterward.

Say **Yes** and it copies itself to `%LocalAppData%\Programs\OpenOTSTray\` (the same per-user convention VS Code uses — no admin rights needed), pins a Start Menu shortcut, turns on **Start with Windows**, and relaunches itself from the new location — all in one step. You'll never need to open a folder or remember where the file is again; it just runs.

Say **No** and it keeps running from wherever it is, and won't ask again (your choice is remembered) — you can always turn on **Start with Windows** later from **Settings** (gear icon) if you change your mind, or move the exe yourself:

```powershell
$installDir = "$env:LocalAppData\Programs\OpenOTSTray"
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Move-Item -Path ".\OpenOTSTray.exe" -Destination $installDir -Force
```

> If you ever move or rename the exe after enabling "Start with Windows" (self-installed or not), just open it once from its new location — the app rewrites its own startup registry entry with the current path on every launch, so it self-heals instead of leaving a dangling shortcut.

## Usage

1. Run `OpenOTSTray.exe`. It has no window — look for its icon in the system tray (you may need to expand the hidden icons arrow).
2. Click the icon (left or right click both work) to open the flyout:
   - **Quick One Time** — paste/type a secret, click "Generate Link".
   - **Generate** — set quantity, TTL (days), optional passphrase and region, click "Generate".
   - **Latest Link** — see and copy everything you've generated, check open/burned status, send by email.
   - Gear icon (top right) — **Settings** and **About**.

## Settings & history storage

Stored per-user at `%AppData%\OpenOTSTray\`:

- `settings.json` — password length, region, start-with-Windows, hotkey, email template. Validated on load against safe defaults, so a corrupted or tampered file can't be used to redirect requests or send content you didn't intend.
- `history.json` — link history metadata (title, status, region). **Passwords, passphrases, and email bodies are never written to disk.** The link itself is encrypted at rest with Windows DPAPI (tied to your Windows user account and this machine) — copying `history.json` to another PC or user account makes the encrypted links unreadable there.

"Start with Windows" is implemented via the per-user Registry `Run` key (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) — no admin rights required, and it can be toggled off from Setup at any time.

## Notes

- Uses the public (guest) OneTimeSecret API v2 endpoint — no account or API key required.
- Links are single-use: once opened (or burned), they can't be viewed again.
