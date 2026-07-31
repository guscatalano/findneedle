$ErrorActionPreference = "Stop"
$here = $PSScriptRoot; $out = $here
$kits = "C:\Program Files (x86)\Windows Kits\10"; $ver = "10.0.26100.0"
$tracewpp = "$kits\bin\$ver\x64\tracewpp.exe"
$tracepdb = "$kits\bin\$ver\x64\tracepdb.exe"
$wppcfg   = "$kits\bin\$ver\WppConfig\Rev1"
$vcvars   = "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
& $tracewpp -cfgdir:"$wppcfg" -odir:"$out" "$here\WppStrEmitter.cpp"
if (-not (Test-Path "$out\WppStrEmitter.tmh")) { throw "no tmh" }
$compile = "`"$vcvars`" >nul 2>&1 && cd /d `"$out`" && cl /nologo /EHsc /Zi /I `"$out`" /Fe:WppStrEmitter.exe `"$here\WppStrEmitter.cpp`" /link advapi32.lib"
cmd /c $compile
if (-not (Test-Path "$out\WppStrEmitter.exe")) { throw "no exe" }
$tmf = Join-Path $out "tmf"; New-Item -ItemType Directory -Force -Path $tmf | Out-Null
& $tracepdb -f "$out\WppStrEmitter.pdb" -p "$tmf"
Write-Output "BUILD OK"
