$ErrorActionPreference = "Stop"; $here = $PSScriptRoot; $out = $here
$kits = "C:\Program Files (x86)\Windows Kits\10"; $ver = "10.0.26100.0"
$tw = "$kits\bin\$ver\x64\tracewpp.exe"; $cfg = "$kits\bin\$ver\WppConfig\Rev1"
$vc = "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
& $tw -cfgdir:"$cfg" -scan:"$here\WppEnum2Cfg.h" -odir:"$out" "$here\WppEnum2Emitter.cpp"
if (-not (Test-Path "$out\WppEnum2Emitter.tmh")) { throw "no tmh" }
cmd /c "`"$vc`" >nul 2>&1 && cd /d `"$out`" && cl /nologo /EHsc /Zi /I `"$out`" /Fe:WppEnum2Emitter.exe `"$here\WppEnum2Emitter.cpp`" /link advapi32.lib > `"$out\cl.txt`" 2>&1"
if (Test-Path "$out\WppEnum2Emitter.exe") { Write-Output "EXE OK" } else { Write-Output "NO EXE"; Get-Content "$out\cl.txt" -Tail 8 }
