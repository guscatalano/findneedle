# FindNeedle MSI installer

WiX 5 installer for FindNeedleUX. Produces a single MSI that supports both
per-user and per-machine install via Single Package Authoring.

## Build

```cmd
cd FindNeedleUX.Installer
build.cmd
```

This:

1. Runs `dotnet publish FindNeedleUX -c Release -r win-x64 --self-contained true`
   so all .NET 8 + Windows App SDK dependencies are bundled.
2. Builds `FindNeedleUX.Installer.wixproj`, which harvests the published output
   and produces `bin\Release\FindNeedle.msi`.

WiX itself is pulled via NuGet (`WixToolset.Sdk` 5.0.2). No separate install needed.

## Install

**Per-user (default, no admin required):**

```cmd
msiexec /i FindNeedle.msi /qb
```

Installs to `%LocalAppData%\Programs\FindNeedle`. Shows up under the user's
"Apps & features" list.

**Per-machine (requires admin elevation):**

```cmd
msiexec /i FindNeedle.msi ALLUSERS=1 MSIINSTALLPERUSER=0 /qb
```

Installs to `%ProgramFiles%\FindNeedle`. Shows up globally for all users.

Double-clicking the MSI launches the WixUI dialog flow which lets the user pick
the install directory.

## What's installed

- All of `FindNeedleUX/bin/.../publish/*` — the self-contained app
- Start Menu shortcut: `Programs\FindNeedle\FindNeedle`
- Optional desktop shortcut (selectable in the install UI)
- File associations: `.log`, `.txt`, `.etl`, `.evtx`, `.zip` get added to the
  Windows "Open with" list. Default app for those extensions is **not**
  overridden — users explicitly pick FindNeedle from "Open with".
- Registered as a "Default Apps" candidate so it can be set as the default
  for any of the above extensions via Windows Settings.

## What's not installed

- **WebView2 Evergreen runtime.** Required for the result viewer's WebView2.
  On Windows 11 it's already present. On older Windows, install from
  <https://developer.microsoft.com/en-us/microsoft-edge/webview2/>.

## Versioning

The MSI version comes from the `ProductVersion` MSBuild property (default
`1.0.0.0`). To bump:

```cmd
dotnet build FindNeedleUX.Installer.wixproj -c Release -p:ProductVersion=1.2.3.0
```

`<MajorUpgrade>` is configured, so newer MSI installs over older without
manual uninstall.

## Upgrade code

`1133E465-074D-4B2A-B7FD-B0E929B2A3E0` — keep stable across versions or
upgrades break.
