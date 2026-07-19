# Unofficial WebView sidecar for Modrinth App on Windows

> [!IMPORTANT]
> This is an independent interoperability project. It is not affiliated with,
> authorized, sponsored, or endorsed by Rinth, Inc. “Modrinth” and “Modrinth+”
> are trademarks of Rinth, Inc. This repository does not contain or redistribute
> the Modrinth App, Modrinth source code, logos, artwork, updater keys, account
> credentials, or advertising content.

This Windows-only utility launches a user-installed copy of the official
Modrinth App with a temporary loopback WebView2 debugging endpoint. It
identifies expected Tauri WebViews, requests the App's existing native ad-window
hide operation, and removes local sidebar placeholders with CSS.

It does **not** patch the installed Modrinth executable, alter the official
updater, change an account or subscription, impersonate Modrinth+, bypass
authentication or a product-license check, or block network traffic. The
optional installer changes only current-user `.lnk` shortcuts and keeps exact
backups.

The officially supported way to remove Modrinth advertising is
[Modrinth+](https://modrinth.com/plus), which also supports the service.

## Important security warning

> [!WARNING]
> Chrome DevTools Protocol (CDP) is a privileged, unauthenticated local control
> interface. The sidecar chooses a random port and requests a bind to
> `127.0.0.1`, validates the endpoint and target identities, disables proxy use,
> and caps response sizes. Even so, another local process that discovers the
> port may be able to inspect or control the App's WebViews. The debugging
> endpoint remains available while Modrinth's WebView2 process is running, even
> if the sidecar exits. Use this only on a trusted, non-shared computer, do not
> run it as administrator, and fully quit Modrinth when finished.

## Requirements and compatibility

- A currently supported Windows 11 release on x64.
- A separately installed official Modrinth App.
- Microsoft WebView2 and .NET Framework 4.x, normally already present on
  Windows 11.
- A normal, non-administrator user session.

The executables are managed AnyCPU/MSIL with no bundled native library. They may
run on Windows 11 ARM64 through the platform's .NET support, but ARM64 has not
been tested and is not currently a supported release target.

Continuous integration builds and tests on GitHub's Windows Server 2022 and
Windows Server 2025 images. Those runners are useful compatibility checks but
are not Windows 11. Release 1.2.0 was also tested locally on Windows 11
Enterprise 25H2 x64 (build 26200.8737) with Modrinth App 0.15.11.

## Install the background integration

1. Download the ZIP and its `.sha256.txt` file from
   [GitHub Releases](https://github.com/Ambassador4ik/modrinth-no-ads-sidecar/releases).
2. Verify the ZIP checksum if desired, then extract the entire folder.
3. Fully quit Modrinth, including its tray process.
4. Double-click **Install Background Shortcut.cmd**.
5. Launch through a patched current-user shortcut or through
   **Unofficial Modrinth No-Ads Launcher** in the Start menu.

The `.cmd` file uses `-ExecutionPolicy Bypass` only for the one child
`powershell.exe` process. It does not change the user's or machine's PowerShell
policy. Inspect the adjacent PowerShell script or build from source if that
one-process override is not acceptable.

The installer normally patches current-user Modrinth shortcuts and also creates
a separately named fallback shortcut. To leave existing shortcuts untouched and
create only the separate shortcut:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Install Background Shortcut.ps1" -SeparateShortcutOnly
```

For a custom App location, provide the executable explicitly:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Install Background Shortcut.ps1" -AppPath "D:\Apps\Modrinth App\Modrinth App.exe"
```

Taskbar pins may cache the original target. If a pin bypasses the sidecar, unpin
it and pin **Unofficial Modrinth No-Ads Launcher** from Start.

## Restore and uninstall

1. Fully quit Modrinth and the sidecar.
2. Run **Restore Original Shortcuts.cmd** from the extracted or installed
   package.
3. Confirm that the original shortcuts work.
4. Delete `%LOCALAPPDATA%\Programs\ModrinthNoAdsSidecar` if the runtime is no
   longer wanted.
5. After reviewing anything useful for diagnostics, optionally delete
   `%LOCALAPPDATA%\ModrinthNoAdsSidecar` to remove logs, the manifest, and
   shortcut backups.

Restore changes a shortcut only when its target, arguments, working directory,
icon, and description still match the state recorded by the installer. A
user-edited shortcut is left alone, and a user-deleted shortcut is not
recreated. The complete manifest is validated before any shortcut is changed.

## Run without installing shortcuts

The release ZIP includes a visible diagnostic launcher:

```powershell
& ".\ModrinthNoAdsSidecar.exe" --app "$env:LOCALAPPDATA\Modrinth App\Modrinth App.exe" --verbose
```

Useful commands:

```powershell
& ".\ModrinthNoAdsSidecar.exe" --check
& ".\ModrinthNoAdsSidecar.exe" --self-test
& ".\ModrinthNoAdsSidecar.exe" --version
& ".\ModrinthNoAdsSidecar.exe" --help
```

Normal launch requires Modrinth to be fully closed first. If an unprotected
Modrinth process is already running, the sidecar refuses to report success.

## How it works

1. The console sidecar finds and validates `Modrinth App.exe`.
2. It chooses an unused random TCP port and launches only that child process
   with `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` requesting
   `--remote-debugging-address=127.0.0.1`.
3. It removes inherited remote-debugging switches using Windows command-line
   parsing before appending its canonical loopback settings.
4. It validates that the loopback endpoint identifies as Microsoft
   Edge/WebView2 CDP and accepts only same-port loopback WebSocket URLs.
5. It classifies only expected Modrinth main/ad views using URL, Tauri labels,
   title, and DOM evidence.
6. It installs document-start guards. The main guard repeatedly calls the
   App's existing `plugin:ads|hide_ads_window` command and injects local CSS to
   collapse the sidebar placeholders.
7. The windowless launcher waits until the verified main guard is active. If
   verification fails, it displays an error instead of silently claiming
   success.

No global environment variable is set. No service, scheduled task, browser
extension, machine-wide registry value, administrator elevation, updater
replacement, or maintainer-operated server is used.

## Future Modrinth updates

The official Modrinth updater remains enabled and unchanged. Because this tool
depends on WebView names, internal command names, URLs, and CSS selectors, an
App update can break suppression. Target checks fail closed: unmatched WebViews
are not modified, but the App may then show its normal UI. The launcher reports
when it cannot verify an active guard.

GitHub Actions automatically builds a new GitHub Release when the maintainer
pushes a valid `vX.Y.Z` tag. Installed copies of this sidecar do **not**
self-update or contact GitHub; users must install a newer release manually.

## Privacy and local data

The sidecar has no telemetry, maintainer-operated service, advertising network
blocker, or automatic update client. Its own network communication is limited to
the temporary loopback CDP endpoint. The official Modrinth App retains its
normal network behavior; hiding a WebView does not guarantee that advertising
content or impressions are not requested or recorded.

The sidecar writes `%LOCALAPPDATA%\ModrinthNoAdsSidecar\sidecar.log`. It redacts
known user-profile prefixes from the App and log paths, rotates at approximately
1 MiB, strips control characters, and truncates individual messages. The log
can still contain timestamps, the App version, process IDs, the local debugging
port, errors, and—when `--verbose` is used—target paths and titles.

Shortcut integration stores absolute paths and exact `.lnk` backups under
`%LOCALAPPDATA%\ModrinthNoAdsSidecar\shortcut-integration`. Nothing is uploaded
automatically. Review and redact logs or state files before attaching them to a
public issue.

## Persistent changes

The optional installer writes only:

- `%LOCALAPPDATA%\Programs\ModrinthNoAdsSidecar`
- `%LOCALAPPDATA%\ModrinthNoAdsSidecar\shortcut-integration`
- selected current-user `.lnk` files under Desktop, Start menu, or pinned-item
  locations

It copies a staged and self-tested runtime, records exact post-install shortcut
state, and preserves original links in the backup directory. Existing official
shortcuts keep an icon reference to the user's own installed App; the new
unofficial shortcut uses the generic sidecar executable icon. No Modrinth icon
is distributed.

## Binary verification and release provenance

Release executables are not Authenticode-signed and may trigger SmartScreen or
antivirus warnings. Build from source when possible.

Each release contains:

- a ZIP with an internal `SHA256SUMS.txt`;
- a separate SHA-256 file for the ZIP;
- a GitHub artifact attestation generated by the release workflow.

SHA-256 detects accidental changes only when the checksum itself came from a
trusted release. It does not establish publisher identity. The GitHub
attestation ties the asset to this repository's workflow, but it is not
Windows Authenticode. Never download binaries from issue comments or unofficial
mirrors.

## Build and test from source

The project is dependency-free and builds with the .NET Framework C# compiler
included with Windows:

```powershell
git clone https://github.com/Ambassador4ik/modrinth-no-ads-sidecar.git
Set-Location .\modrinth-no-ads-sidecar
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-Repository.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Verify-Build.ps1 -BinaryDirectory .\artifacts\bin
```

Create the release ZIP locally:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\New-ReleasePackage.ps1
```

The source branch intentionally excludes executables, ZIPs, logs, manifests,
and shortcut backups. See [CONTRIBUTING.md](CONTRIBUTING.md) for safety
requirements.

## Terms and third-party rights

Using this utility changes the local presentation and behavior of a third-party
service. Modrinth's current [Terms of Use](https://modrinth.com/legal/terms)
include restrictions on devices, software, or routines that interfere with
operation of the service. Rinth, Inc. may consider use of this utility
inconsistent with those terms and may restrict access under its policies.

Review the current terms and applicable law before use. A disclaimer cannot
grant permission, and this repository is not legal advice. Written permission
from Rinth, Inc. is the strongest risk mitigation before broad, organizational,
or commercial distribution.

The MIT License covers only this project's original code. It grants no rights
in third-party software, services, content, accounts, or trademarks. See
[NOTICE.md](NOTICE.md) and [SECURITY.md](SECURITY.md).

## License

Copyright (c) 2026 Ambassador4ik and contributors.

Original project code is released under the [MIT License](LICENSE).
