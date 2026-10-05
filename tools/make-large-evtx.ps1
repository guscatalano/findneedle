# Generates a large .evtx fixture by writing synthetic events to a temporary custom event log and
# exporting it. Requires an elevated (Administrator) shell - creating an event log/source needs it.
#
#   pwsh -File tools\make-large-evtx.ps1 -Count 250000
#
# For a quick, no-admin fixture instead, just export an existing channel:
#   wevtutil epl Application LargeSamples\large-app.evtx
param(
    [int]$Count = 250000,
    [string]$LogName = "FindNeedleLargeEvtx",
    [string]$Source  = "FindNeedleGen",
    [string]$Out     = "$PSScriptRoot\..\LargeSamples\large-gen.evtx"
)

$ErrorActionPreference = "Stop"
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $admin) { Write-Error "Run this elevated (Administrator) - New-EventLog requires admin."; exit 1 }

if (-not [System.Diagnostics.EventLog]::SourceExists($Source)) {
    New-EventLog -LogName $LogName -Source $Source
}
# Make the log big enough to hold everything we are about to write, and PROVE it took effect.
#
# Limit-EventLog -MaximumSize is silently ignored for a custom log here: ask for 1 GB and the log
# stays at its 512 KB default (measured: MaximumSizeInBytes 1052672). The events then wrap, and
# because -OverflowAction OverwriteAsNeeded means "roll over" - the opposite of what the old comment
# claimed - the export quietly kept only the newest ~2,600 of them while the script reported the full
# count. A 1.2M-event run and a 60k-event run produced byte-identical 1.1 MB files.
#
# wevtutil sl does apply. /rt:false keeps overwrite-as-needed as the overflow mode, which is fine
# once the size is actually large enough.
$maxBytes = 1GB
wevtutil sl $LogName /ms:$maxBytes /rt:false
$applied = (Get-WinEvent -ListLog $LogName).MaximumSizeInBytes
Write-Host ("Log max size: {0:N0} bytes (asked for {1:N0})" -f $applied, $maxBytes)
if ($applied -lt 64MB) {
    Write-Error "The log's max size is only $applied bytes, so it will wrap and the fixture will be a fraction of -Count. Set it by hand: wevtutil sl $LogName /ms:$maxBytes"
    exit 1
}

$log = New-Object System.Diagnostics.EventLog($LogName)
$log.Source = $Source
$rand = [Random]::new()
$types = @(
    [System.Diagnostics.EventLogEntryType]::Information,
    [System.Diagnostics.EventLogEntryType]::Warning,
    [System.Diagnostics.EventLogEntryType]::Error)

Write-Host "Writing $Count events to '$LogName'..."
for ($i = 1; $i -le $Count; $i++) {
    $log.WriteEntry("Synthetic event #$i  payload=$($rand.Next())  guid=$([guid]::NewGuid())",
                    $types[$rand.Next(3)], ($i % 1000))
    if ($i % 10000 -eq 0) { Write-Host "  $i / $Count" }
}

# wevtutil will not create the target folder, and it reports the miss as
# "The system cannot find the path specified" with a zero exit code from PowerShell's point of
# view - so create the folder, then check the file really appeared.
$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }

if (Test-Path $Out) { Remove-Item $Out -Force }
$retained = (Get-WinEvent -ListLog $LogName).RecordCount
wevtutil epl $LogName $Out
Remove-EventLog -LogName $LogName
if (-not (Test-Path $Out)) {
    Write-Error "wevtutil did not write $Out - the export failed (its own message is above)."
    exit 1
}
# Report what the log actually held, not what we tried to write. Reading RecordCount off the live
# log is cheap; counting records in the exported file is not.
if ($retained -lt $Count) {
    Write-Warning "The log held $retained of $Count events - it wrapped, so the fixture is smaller than asked for."
}
$mb = [math]::Round((Get-Item $Out).Length / 1MB, 1)
Write-Host "Wrote $Out ($mb MB, $retained events retained of $Count written)"
