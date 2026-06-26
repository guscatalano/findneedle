# Builds WppEmitter.exe (WPP software-trace emitter) and extracts its TMF, for the test fixture.
# Steps: tracewpp (generate .tmh) -> cl (compile+link, with PDB) -> tracepdb (extract .tmf).
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$out  = Join-Path $here "build"
New-Item -ItemType Directory -Force -Path $out | Out-Null

$kits = "C:\Program Files (x86)\Windows Kits\10"
$ver  = "10.0.26100.0"
$tracewpp = "$kits\bin\$ver\x64\tracewpp.exe"
$tracepdb = "$kits\bin\$ver\x64\tracepdb.exe"
$wppcfg   = "$kits\bin\$ver\WppConfig\Rev1"
$vcvars   = "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"

Write-Output "=== 1. tracewpp (generate .tmh) ==="
& $tracewpp -cfgdir:"$wppcfg" -odir:"$out" "$here\WppEmitter.cpp"
if (-not (Test-Path "$out\WppEmitter.tmh")) { throw "tracewpp did not produce WppEmitter.tmh" }
Write-Output "  -> $out\WppEmitter.tmh"

Write-Output "=== 2. cl (compile + link) ==="
# vcvars sets cl + the SDK include/lib; build from $out so obj/pdb land there; /I for the .tmh.
$compile = "`"$vcvars`" >nul 2>&1 && cd /d `"$out`" && cl /nologo /EHsc /Zi /I `"$out`" /Fe:WppEmitter.exe `"$here\WppEmitter.cpp`" /link advapi32.lib"
cmd /c $compile
if (-not (Test-Path "$out\WppEmitter.exe")) { throw "cl did not produce WppEmitter.exe" }
Write-Output "  -> $out\WppEmitter.exe"

Write-Output "=== 3. tracepdb (extract .tmf) ==="
$tmf = Join-Path $out "tmf"
New-Item -ItemType Directory -Force -Path $tmf | Out-Null
& $tracepdb -f "$out\WppEmitter.pdb" -p "$tmf"
$tmfs = Get-ChildItem $tmf -Filter *.tmf -ErrorAction SilentlyContinue
Write-Output ("  -> {0} TMF file(s): {1}" -f $tmfs.Count, (($tmfs | Select-Object -ExpandProperty Name) -join ", "))

Write-Output "BUILD OK"