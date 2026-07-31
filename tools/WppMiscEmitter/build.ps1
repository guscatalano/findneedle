$ErrorActionPreference = "Stop"; $here = $PSScriptRoot; $out = $here
$kits = "C:\Program Files (x86)\Windows Kits\10"; $ver = "10.0.26100.0"
$tw = "$kits\bin\$ver\x64\tracewpp.exe"; $tp = "$kits\bin\$ver\x64\tracepdb.exe"; $cfg = "$kits\bin\$ver\WppConfig\Rev1"
$vc = "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
& $tw -cfgdir:"$cfg" -odir:"$out" "$here\WppMiscEmitter.cpp"
cmd /c "`"$vc`" >nul 2>&1 && cd /d `"$out`" && cl /nologo /EHsc /Zi /I `"$out`" /Fe:WppMiscEmitter.exe `"$here\WppMiscEmitter.cpp`" /link advapi32.lib > `"$out\cl_out.txt`" 2>&1"
if (Test-Path "$out\WppMiscEmitter.exe") { Write-Output "EXE OK" } else { Write-Output "NO EXE"; Get-Content "$out\cl_out.txt" -Tail 15 }
