@echo off
setlocal
set CONFIG=Release
set RID=win-x64
set PUBLISH_DIR=%~dp0..\FindNeedleUX\bin\x64\%CONFIG%\net8.0-windows10.0.19041.0\%RID%\publish

echo === Publishing FindNeedleUX (self-contained %RID%) ===
dotnet publish "%~dp0..\FindNeedleUX\FindNeedleUX.csproj" -c %CONFIG% -r %RID% --self-contained true -p:Platform=x64 -p:GenerateAppxPackageOnBuild=false
if errorlevel 1 exit /b 1

echo === Building MSI ===
dotnet build "%~dp0FindNeedleUX.Installer.wixproj" -c %CONFIG%
if errorlevel 1 exit /b 1

echo.
echo MSI: %~dp0bin\%CONFIG%\FindNeedle.msi
echo.
echo Per-user install (default, no admin):
echo     msiexec /i FindNeedle.msi /qb
echo Per-machine install (admin):
echo     msiexec /i FindNeedle.msi ALLUSERS=1 MSIINSTALLPERUSER=0 /qb
endlocal
