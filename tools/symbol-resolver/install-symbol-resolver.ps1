<#
.SYNOPSIS
  Set FindNeedle up so a custom WPP symbol resolver is loaded automatically — no editing of the installed
  app. Optionally installs FindNeedle via winget, then deploys the resolver DLL to a writable per-user
  folder and registers it through the HKCU plugin key + the symbol-share environment variable.

.DESCRIPTION
  FindNeedle honors an extra plugin list at HKCU\Software\FindNeedle\Plugins (a ';'-separated list of
  ABSOLUTE DLL paths), merged with its built-in plugins at startup. This script points that key at a
  symbol-resolver plugin, so on the next launch FindNeedle asks your resolver for any PDB its built-in
  lookup can't find — e.g. the bundled SmbSymbolResolver, which searches the symbol shares in
  FINDNEEDLE_SYMBOL_SHARES. Registry plugin loading is enabled in the shipped PluginConfig.json.

  VERIFIED against a PACKAGED (MSIX, full-trust) FindNeedle: the packaged app reads this HKCU value and
  loads the external DLL by absolute path. (A file-based list under %LocalAppData% would NOT work when
  packaged — MSIX virtualizes %LocalAppData% into the package container — which is why the registry is
  used. And the DLL must NOT be placed in the install dir: a packaged app lives under the read-only
  C:\Program Files\WindowsApps\... So it goes in a writable per-user folder instead.)

  Runs per-user (HKCU + user env vars — no admin), so it's easy to push via a logon script or deployment
  tool.

.PARAMETER PluginDll
  Path to the resolver DLL to register (default: the bundled SmbSymbolResolverPlugin.dll built in this
  repo, if present). It is COPIED to %LocalAppData%\FindNeedle\plugins\ and that copy is registered.

.PARAMETER SymbolShares
  ';'-separated symbol-share roots for the bundled SMB resolver (sets FINDNEEDLE_SYMBOL_SHARES, user
  scope). e.g. '\\corp\symbols;\\build\drops\symbols'. Omit if your resolver doesn't use it.

.PARAMETER WingetId
  If given, run `winget install <WingetId>` first (a published FindNeedle package id). Optional.

.EXAMPLE
  ./install-symbol-resolver.ps1 -SymbolShares '\\corp\symbols'
#>
[CmdletBinding()]
param(
    [string]$PluginDll,
    [string]$SymbolShares,
    [string]$WingetId
)

$ErrorActionPreference = 'Stop'

if ($WingetId) {
    Write-Host "winget install $WingetId ..."
    winget install --id $WingetId --accept-source-agreements --accept-package-agreements
}

# --- Resolve the resolver DLL (default: the bundled sample built in this repo) ---
if (-not $PluginDll) {
    $PluginDll = Get-ChildItem -Path (Join-Path $PSScriptRoot '..\..\Plugins\SymbolResolver') `
        -Filter 'SmbSymbolResolverPlugin.dll' -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object FullName
}
if (-not $PluginDll -or -not (Test-Path -LiteralPath $PluginDll)) {
    throw "Resolver DLL not found. Build Plugins\SymbolResolver\SmbSymbolResolverPlugin, or pass -PluginDll <path>."
}

# --- Deploy to a WRITABLE per-user folder (NOT the install dir — a packaged app's dir is read-only). The
#     packaged app loads it by absolute path, which bypasses MSIX known-folder virtualization. Its only
#     dependency (FindNeedlePluginLib) resolves from FindNeedle's own copy at load time. ---
$pluginDir = Join-Path $env:LOCALAPPDATA 'FindNeedle\plugins'
New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
$dest = Join-Path $pluginDir ([IO.Path]::GetFileName($PluginDll))
Copy-Item -LiteralPath $PluginDll -Destination $dest -Force
Write-Host "Deployed resolver -> $dest"

# --- Register via the HKCU plugin key (absolute paths; merged with the built-ins at startup) ---
$key = 'HKCU:\Software\FindNeedle\Plugins'
New-Item -Path $key -Force | Out-Null
$existing = [Microsoft.Win32.Registry]::GetValue('HKEY_CURRENT_USER\Software\FindNeedle\Plugins', '', $null)
$paths = @()
if ($existing) { $paths += ($existing -split ';' | Where-Object { $_ -and $_ -ne $dest }) }
$paths += $dest
[Microsoft.Win32.Registry]::SetValue('HKEY_CURRENT_USER\Software\FindNeedle\Plugins', '', ($paths -join ';'))
Write-Host "Registered plugin(s): $($paths -join ';')"

# --- Point the bundled SMB resolver at your symbol share(s) ---
if ($SymbolShares) {
    [Environment]::SetEnvironmentVariable('FINDNEEDLE_SYMBOL_SHARES', $SymbolShares, 'User')
    Write-Host "Set FINDNEEDLE_SYMBOL_SHARES (user) = $SymbolShares"
}

Write-Host ""
Write-Host "Done. Launch FindNeedle — the symbol resolver is now loaded automatically."
Write-Host "Verify in %APPDATA%\FindNeedlePlugin\findneedle_log.txt: a 'Loading plugin module: ...\plugins\...' line."
