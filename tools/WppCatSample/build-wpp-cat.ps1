<#
.SYNOPSIS
  Build a tiny WPP producer (wppcat.c) and capture a real WPP .etl fixture (+ its .tmf), so the
  tracefmt decode path in ETWPlugin can be tested against genuine WPP data (unlike the EventSource
  fixtures, which are self-describing).

.DESCRIPTION
  Pipeline: tracewpp (generate .tmh) -> cl (compile, with PDB) -> tracepdb (extract .tmf) ->
  tracelog (ETW capture while wppcat.exe runs) -> tracefmt (verify it decodes).
  Outputs to <repo>\LargeSamples\: cats-wpp.etl and wpp-tmf\*.tmf.

  Requires: Windows SDK/WDK tools (tracewpp/tracefmt/tracelog/tracepdb) + MSVC C++ toolset, and
  ADMIN (tracelog needs an elevated session). Run from an elevated prompt:
    pwsh -File tools\WppCatSample\build-wpp-cat.ps1 -Count 5000

.PARAMETER Count
  Number of WPP messages to emit (default 5000 for a quick validate; pass 5000000 for the big fixture).
#>
[CmdletBinding()]
param(
    [int]$Count = 5000
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = (Resolve-Path (Join-Path $here '..\..')).Path
$outDir = Join-Path $repo 'LargeSamples'
$tmfDir = Join-Path $outDir 'wpp-tmf'
$work = Join-Path $here 'build'
New-Item -ItemType Directory -Force -Path $outDir, $tmfDir, $work | Out-Null

$Guid = 'A1B2C3D4-E5F6-4789-ABCD-1234567890AB'   # must match WPP_CONTROL_GUIDS in wppcat.c
$etl = Join-Path $outDir 'cats-wpp.etl'
$session = 'FindNeedle_WppCat'

function Find-Tool([string]$name) {
    $kits = (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots').KitsRoot10
    $hit = Get-ChildItem -Path (Join-Path $kits 'bin') -Filter $name -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName | Select-Object -Last 1
    if (-not $hit) { throw "Could not find $name (x64) under the Windows Kits bin folder." }
    return $hit.FullName
}

function Find-WppConfig() {
    $kits = (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots').KitsRoot10
    $hit = Get-ChildItem -Path (Join-Path $kits 'bin') -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\WppConfig\\Rev1$' } |
        Sort-Object FullName | Select-Object -Last 1
    if (-not $hit) { throw 'Could not find WppConfig\Rev1 (tracewpp config templates).' }
    return $hit.FullName
}

function Find-VcVars() {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) { throw 'vswhere.exe not found.' }
    $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vs) { throw 'No VS install with the C++ toolset (VC.Tools.x86.x64) found.' }
    $vc = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
    if (-not (Test-Path $vc)) { throw "vcvars64.bat not found at $vc" }
    return $vc
}

$tracewpp = Find-Tool 'tracewpp.exe'
$tracefmt = Find-Tool 'tracefmt.exe'
$tracelog = Find-Tool 'tracelog.exe'
$tracepdb = Find-Tool 'tracepdb.exe'
$wppCfg   = Find-WppConfig
$vcvars   = Find-VcVars

Write-Host "tracewpp : $tracewpp"
Write-Host "WppConfig: $wppCfg"
Write-Host "vcvars   : $vcvars"
Write-Host ""

# 1) tracewpp -> wppcat.tmh (next to the source so the #include resolves)
Write-Host '== tracewpp (generate .tmh) =='
& $tracewpp -cfgdir:"$wppCfg" -odir:"$here" (Join-Path $here 'wppcat.c')
if ($LASTEXITCODE -ne 0) { throw "tracewpp failed ($LASTEXITCODE)" }

# 2) cl -> wppcat.exe (+ .pdb) inside the VS dev environment.
# Run from $work with relative output names so we avoid the MSVC /Fo:"dir\" trailing-backslash-quote
# bug. (Repo path has no spaces, so the source/include can be passed unquoted.)
Write-Host '== cl (compile) =='
$exe = Join-Path $work 'wppcat.exe'
$src = Join-Path $here 'wppcat.c'
Push-Location $work
try
{
    cmd /c "call `"$vcvars`" && cl /nologo /W3 /Zi /I$here /Fe:wppcat.exe /Fd:wppcat.pdb $src /link advapi32.lib"
    if ($LASTEXITCODE -ne 0) { throw "cl failed ($LASTEXITCODE)" }
}
finally { Pop-Location }
if (-not (Test-Path $exe)) { throw "wppcat.exe was not produced." }

# 3) tracepdb -> extract .tmf(s) from the PDB
Write-Host '== tracepdb (extract TMF) =='
Get-ChildItem $tmfDir -Filter *.tmf -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
& $tracepdb -f (Join-Path $work 'wppcat.pdb') -p $tmfDir
if ($LASTEXITCODE -ne 0) { Write-Warning "tracepdb returned $LASTEXITCODE" }
$tmfs = Get-ChildItem $tmfDir -Filter *.tmf -ErrorAction SilentlyContinue
Write-Host ("   extracted {0} TMF file(s)" -f ($tmfs | Measure-Object).Count)

# 4) tracelog capture: start session enabling our control GUID, run the producer, stop.
Write-Host '== tracelog capture =='
if (Test-Path $etl) { Remove-Item $etl -Force }
& $tracelog -stop $session 2>$null | Out-Null   # clean up a stale session if any
& $tracelog -start $session -guid "#$Guid" -f $etl -flags 0x7FFFFFFF -level 5
if ($LASTEXITCODE -ne 0) { throw "tracelog -start failed ($LASTEXITCODE) — are you elevated?" }
try {
    & $exe $Count
} finally {
    & $tracelog -stop $session
}
if (-not (Test-Path $etl)) { throw "ETL was not produced." }
Write-Host ("   {0}: {1:N0} bytes" -f $etl, (Get-Item $etl).Length)

# 5) verify it decodes via tracefmt using the extracted TMF
Write-Host '== tracefmt (verify decode) =='
$fmtOut = Join-Path $work 'wppcat.fmt.txt'
$fmtSum = Join-Path $work 'wppcat.sum.txt'
& $tracefmt $etl -tmf ($tmfs | Select-Object -First 1).FullName -o $fmtOut -nosummary 2>$null
if (Test-Path $fmtOut) {
    $lines = Get-Content $fmtOut -TotalCount 5
    Write-Host '   first formatted lines:'
    $lines | ForEach-Object { Write-Host "     $_" }
} else {
    Write-Warning 'tracefmt produced no output file — check the TMF / GUID.'
}

Write-Host ''
Write-Host "DONE -> $etl  (+ TMFs in $tmfDir)"
