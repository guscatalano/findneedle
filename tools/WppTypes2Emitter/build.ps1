$ErrorActionPreference = "Stop"; $here = $PSScriptRoot; $out = $here
$kits = "C:\Program Files (x86)\Windows Kits\10"; $ver = "10.0.26100.0"
$tw = "$kits\bin\$ver\x64\tracewpp.exe"; $tp = "$kits\bin\$ver\x64\tracepdb.exe"; $cfg = "$kits\bin\$ver\WppConfig\Rev1"
$vc = "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
& $tw -cfgdir:"$cfg" -odir:"$out" "$here\WppTypes2Emitter.cpp"
if (-not (Test-Path "$out\WppTypes2Emitter.tmh")) { throw "no tmh" }
cmd /c "`"$vc`" >nul 2>&1 && cd /d `"$out`" && cl /nologo /EHsc /Zi /I `"$out`" /Fe:WppTypes2Emitter.exe `"$here\WppTypes2Emitter.cpp`" /link advapi32.lib ole32.lib"
if (-not (Test-Path "$out\WppTypes2Emitter.exe")) { throw "no exe" }
$tmf = Join-Path $out "tmf"; New-Item -ItemType Directory -Force -Path $tmf | Out-Null
& $tp -f "$out\WppTypes2Emitter.pdb" -p "$tmf"
Write-Output "BUILD OK"
