# Generates a large .evtx fixture by writing synthetic events to a temporary custom event log and
# exporting it. Requires an elevated (Administrator) shell — creating an event log/source needs it.
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
if (-not $admin) { Write-Error "Run this elevated (Administrator) — New-EventLog requires admin."; exit 1 }

if (-not [System.Diagnostics.EventLog]::SourceExists($Source)) {
    New-EventLog -LogName $LogName -Source $Source
}
# Hold everything we write (don't roll over) — ~1 GB is plenty for a few million small events.
Limit-EventLog -LogName $LogName -MaximumSize 1073741824 -OverflowAction OverwriteAsNeeded

$log = New-Object System.Diagnostics.EventLog($LogName)
$log.Source = $Source
$rand = [Random]::new()
$types = @(
    [System.Diagnostics.EventLogEntryType]::Information,
    [System.Diagnostics.EventLogEntryType]::Warning,
    [System.Diagnostics.EventLogEntryType]::Error)

Write-Host "Writing $Count events to '$LogName'…"
for ($i = 1; $i -le $Count; $i++) {
    $log.WriteEntry("Synthetic event #$i  payload=$($rand.Next())  guid=$([guid]::NewGuid())",
                    $types[$rand.Next(3)], ($i % 1000))
    if ($i % 10000 -eq 0) { Write-Host "  $i / $Count" }
}

if (Test-Path $Out) { Remove-Item $Out -Force }
wevtutil epl $LogName $Out
Remove-EventLog -LogName $LogName
$mb = [math]::Round((Get-Item $Out).Length / 1MB, 1)
Write-Host "Wrote $Out ($mb MB, $Count events)"
