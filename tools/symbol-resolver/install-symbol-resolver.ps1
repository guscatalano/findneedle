<#
.SYNOPSIS
  Set FindNeedle up so a custom WPP symbol resolver is loaded automatically — no editing of the installed
  app. Optionally installs FindNeedle via winget first, then deploys the resolver DLL next to the app and
  registers it through the HKCU plugin key + the symbol-share environment variable.

.DESCRIPTION
  FindNeedle honors an extra plugin list at HKCU\Software\FindNeedle\Plugins (a ';'-separated list of DLL
  paths). This script points that key at a symbol-resolver plugin, so on the next launch FindNeedle will
  ask your resolver for any PDB its built-in lookup can't find — e.g. the bundled SmbSymbolResolver, which
  searches the symbol shares in FINDNEEDLE_SYMBOL_SHARES. The shipped PluginConfig.json has registry plugin
  loading enabled, so nothing in the install needs changing.

  Meant to be run per-user (writes HKCU + user env vars — no admin needed), so it's easy to push via a
  logon script or an org deployment tool.

.PARAMETER PluginDll
  Path to the resolver DLL to register (default: the bundled SmbSymbolResolverPlugin.dll built next to this
  script's repo, if present). It is COPIED next to FindNeedle.exe so its FindNeedlePluginLib dependency
  resolves, and the copy is what gets registered.

.PARAMETER SymbolShares
  ';'-separated symbol-share roots for the bundled SMB resolver (sets FINDNEEDLE_SYMBOL_SHARES, user scope).
  e.g. '\\corp\symbols;\\build\drops\symbols'. Omit if your resolver doesn't use it.

.PARAMETER AppDir
  FindNeedle install directory (where FindNeedle*.exe lives). Auto-detected if omitted.

.PARAMETER WingetId
  If given, run `winget install <WingetId>` first (e.g. a published FindNeedle package id). Optional.

.EXAMPLE
  ./install-symbol-resolver.ps1 -SymbolShares '\\corp\symbols' -PluginDll .\SmbSymbolResolverPlugin.dll
#>
[CmdletBinding()]
param(
    [string]$PluginDll,
    [string]$SymbolShares,
    [string]$AppDir,
    [string]$WingetId
)

$ErrorActionPreference = 'Stop'

if ($WingetId) {
    Write-Host "winget install $WingetId ..."
    winget install --id $WingetId --accept-source-agreements --accept-package-agreements
}

# --- Locate the FindNeedle install dir (so we can place the DLL where its dependency resolves) ---
if (-not $AppDir) {
    $cand = Get-ChildItem -Path @(
        "$env:LOCALAPPDATA\Microsoft\WindowsApps",
        "$env:ProgramFiles\FindNeedle",
        "${env:ProgramFiles(x86)}\FindNeedle"
    ) -Filter 'FindNeedleUX*.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cand) { $AppDir = $cand.DirectoryName }
}
if (-not $AppDir -or -not (Test-Path $AppDir)) {
    throw "Could not find the FindNeedle install directory. Pass -AppDir <path to the folder with FindNeedleUX.exe>."
}
Write-Host "FindNeedle install dir: $AppDir"

# --- Resolve the resolver DLL (default: the bundled sample next to this repo, if built) ---
if (-not $PluginDll) {
    $PluginDll = Get-ChildItem -Path (Join-Path $PSScriptRoot '..\..\Plugins\SymbolResolver') `
        -Filter 'SmbSymbolResolverPlugin.dll' -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object FullName
}
if (-not $PluginDll -or -not (Test-Path $PluginDll)) {
    throw "Resolver DLL not found. Build Plugins\SymbolResolver\SmbSymbolResolverPlugin, or pass -PluginDll <path>."
}

# --- Deploy the DLL next to the app so its FindNeedlePluginLib reference resolves at load time ---
$dest = Join-Path $AppDir ([IO.Path]::GetFileName($PluginDll))
Copy-Item -Path $PluginDll -Destination $dest -Force
Write-Host "Copied resolver -> $dest"

# --- Register it via the HKCU plugin key (merged with the built-in plugins at startup) ---
$key = 'HKCU:\Software\FindNeedle\Plugins'
New-Item -Path $key -Force | Out-Null
$existing = (Get-ItemProperty -Path $key -Name '(default)' -ErrorAction SilentlyContinue).'(default)'
$paths = @()
if ($existing) { $paths += ($existing -split ';' | Where-Object { $_ -and $_ -ne $dest }) }
$paths += $dest
Set-ItemProperty -Path $key -Name '(default)' -Value ($paths -join ';')
Write-Host "Registered plugin(s): $($paths -join ';')"

# --- Point the bundled SMB resolver at your symbol share(s) ---
if ($SymbolShares) {
    [Environment]::SetEnvironmentVariable('FINDNEEDLE_SYMBOL_SHARES', $SymbolShares, 'User')
    Write-Host "Set FINDNEEDLE_SYMBOL_SHARES (user) = $SymbolShares"
}

Write-Host ""
Write-Host "Done. Launch FindNeedle — the symbol resolver is now loaded automatically."
Write-Host "Verify in %LocalAppData%\FindNeedle\findneedle_log.txt: 'Loaded plugin from registry' + 'symbol-resolver plugin(s) available'."
