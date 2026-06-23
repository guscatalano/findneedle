<#
.SYNOPSIS
  Capture screenshots of FindNeedle for the README / Store listing.

.DESCRIPTION
  Drives the running app through its in-app MCP server (127.0.0.1:<port>) to set up a few
  representative views over a synthetic demo log (no real/PII data), and captures the app window to
  PNGs under <repo>\docs\screenshots. Launches the built Debug exe if the app isn't already running.

  Prereqs: the MCP server must be enabled (Settings → MCP) — it persists once toggled on.

.PARAMETER Port    MCP port (default 8765).
.PARAMETER OutDir  Output folder (default <repo>\docs\screenshots).

.EXAMPLE
  pwsh tools\screenshots\Capture-AppScreens.ps1
#>
param(
    [int]$Port = 8765,
    [string]$OutDir = "$PSScriptRoot\..\..\docs\screenshots"
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path "$PSScriptRoot\..\.."
$OutDir = (New-Item -ItemType Directory -Force -Path $OutDir).FullName
Add-Type -AssemblyName System.Drawing

# --- Win32 for foreground + window-rect capture ---
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class ScreenCap {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

function Invoke-Mcp([string]$Name, [hashtable]$Arguments = @{}) {
    $body = @{ jsonrpc = "2.0"; id = 1; method = "tools/call"; params = @{ name = $Name; arguments = $Arguments } } |
            ConvertTo-Json -Depth 8 -Compress
    $resp = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/mcp" -Method Post -ContentType "application/json" -Body $body -TimeoutSec 120
    $text = $resp.result.content[0].text
    try { return $text | ConvertFrom-Json } catch { return $text }
}

function Wait-Mcp([int]$TimeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        try { Invoke-Mcp "status" | Out-Null; return $true } catch { Start-Sleep -Milliseconds 700 }
    }
    return $false
}

function Get-AppWindow {
    $p = Get-Process -Name FindNeedleUX -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    return $p
}

function Save-Window([string]$FileName) {
    $p = Get-AppWindow
    if (-not $p) { throw "FindNeedleUX window not found." }
    $h = $p.MainWindowHandle
    [ScreenCap]::ShowWindow($h, 3) | Out-Null      # SW_MAXIMIZE
    [ScreenCap]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 900                   # let the maximize + render settle
    $r = New-Object ScreenCap+RECT
    [ScreenCap]::GetWindowRect($h, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
    if ($w -le 0 -or $ht -le 0) { throw "bad window rect" }
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $ht)))
    $path = Join-Path $OutDir $FileName
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "saved $path ($w x $ht)"
}

# --- Synthetic demo log (no real/PII data) ---
function New-DemoLog {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) "fn_screenshot_demo"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $lines = @(
        "[2024-05-01 09:00:01] INFO Service Acme.Worker started (pid 4821)"
        "[2024-05-01 09:00:02] INFO Loaded configuration from config.json"
        "[2024-05-01 09:00:05] WARNING Cache miss for key session:demo - recomputing"
        "[2024-05-01 09:01:23] ERROR Database connection failed - timeout after 30s"
        "[2024-05-01 09:01:24] INFO Retrying connection attempt 2 of 5"
        "[2024-05-01 09:02:15] WARNING Memory usage at 85 percent - consider cleanup"
        "[2024-05-01 09:03:45] ERROR File not found: assets/theme.xml"
        "[2024-05-01 09:05:12] DEBUG Cache hit rate 92 percent over 10k lookups"
        "[2024-05-01 09:07:30] CRITICAL OutOfMemoryException in ProcessBatch()"
        "[2024-05-01 09:08:01] ERROR NullReference at PipelineStage.Run line 142"
        "[2024-05-01 09:10:00] INFO Checkpoint committed (rev 10482)"
        "[2024-05-01 09:12:33] DEBUG Transaction rollback successful for txn 5512"
        "[2024-05-01 09:15:00] WARNING Disk space below 10 percent on data volume"
        "[2024-05-01 09:18:22] CRITICAL Pipeline halted - StackOverflow in Recurse()"
        "[2024-05-01 09:20:00] INFO Worker drained queue, 0 items pending"
        "[2024-05-01 09:25:45] INFO Health probe OK (latency 4ms)"
        "[2024-05-01 09:30:12] ERROR Authentication failed for service principal"
        "[2024-05-01 09:35:00] WARNING Throttling: 120 req/s exceeds soft limit"
        "[2024-05-01 17:00:00] INFO Scheduled maintenance window starting"
        "[2024-05-01 17:05:30] INFO Service Acme.Worker stopped cleanly"
    )
    # Repeat with incrementing timestamps so the grid looks populated.
    $all = New-Object System.Collections.Generic.List[string]
    for ($rep = 0; $rep -lt 12; $rep++) {
        foreach ($l in $lines) { $all.Add(($l -replace "09:", ("{0:D2}:" -f (9 + ($rep % 8))))) }
    }
    $path = Join-Path $dir "acme.worker.log"
    Set-Content -Path $path -Value $all -Encoding UTF8
    return $dir
}

# --- Ensure the app is running with MCP ---
if (-not (Get-AppWindow)) {
    $exe = Get-ChildItem "$repo\FindNeedleUX\bin\Debug" -Recurse -Filter FindNeedleUX.exe -ErrorAction SilentlyContinue |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $exe) { throw "No built FindNeedleUX.exe found. Build the app first." }
    Write-Host "launching $($exe.FullName)"
    Start-Process $exe.FullName
    Start-Sleep -Seconds 6
}
if (-not (Wait-Mcp 30)) { throw "MCP server not reachable on port $Port. Enable it in Settings -> MCP and retry." }

$demoDir = New-DemoLog
Write-Host "demo log: $demoDir"

# 1) Results overview — a populated grid with level colors + filters
Invoke-Mcp "clear_workspace" | Out-Null
Invoke-Mcp "add_folder" @{ path = $demoDir } | Out-Null
Invoke-Mcp "run_search" @{ ignoreCache = $true } | Out-Null
Invoke-Mcp "open_results" @{ timeoutMs = 15000 } | Out-Null
Invoke-Mcp "wait_for_load" @{ timeoutMs = 30000 } | Out-Null
Start-Sleep -Milliseconds 800
Save-Window "01-results-overview.png"

# 2) Structured query in the search box
Invoke-Mcp "set_filter" @{ search = 'level == Error OR level == Warning' } | Out-Null
Start-Sleep -Milliseconds 800
Save-Window "02-query-language.png"

# 3) A single-level view (level filter)
Invoke-Mcp "clear_filters" | Out-Null
Invoke-Mcp "set_filter" @{ level = "Error" } | Out-Null
Start-Sleep -Milliseconds 800
Save-Window "03-level-filter.png"

Invoke-Mcp "clear_filters" | Out-Null
Write-Host "Done. Screenshots in $OutDir"
