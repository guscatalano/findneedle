<#
.SYNOPSIS
  Aggregate submitted FindNeedle performance-benchmark JSONs into a comparison.

.DESCRIPTION
  Reads a folder of findneedle.perfbench/v1 result files (the JSONs users attach to the Benchmark
  submissions discussion), filters to one benchmarkVersion (only those are comparable), and prints:
    * cross-machine RATIOS (ftsVsScan / parallelSpeedup / usPerRow) — hardware-invariant, so they
      aggregate across every submission;
    * absolute 1M-ingest milliseconds bucketed BY MACHINE (only comparable within similar hardware),
      with the idle-CPU each run reported so busy-machine numbers can be discounted.

.EXAMPLE
  ./aggregate.ps1 -Dir ./submissions -BenchmarkVersion 1
#>
param(
    [string]$Dir = ".",
    [int]$BenchmarkVersion = 1
)

$runs = @()
foreach ($f in Get-ChildItem -Path $Dir -Filter *.json -Recurse -ErrorAction SilentlyContinue) {
    try { $j = Get-Content $f.FullName -Raw | ConvertFrom-Json } catch { continue }
    if ($j.schema -ne 'findneedle.perfbench/v1') { continue }
    if ($j.benchmarkVersion -ne $BenchmarkVersion) { continue }
    $runs += $j
}

Write-Host "Loaded $($runs.Count) submission(s) for benchmarkVersion=$BenchmarkVersion`n"
if ($runs.Count -eq 0) { return }

Write-Host "Cross-machine ratios (comparable across ALL hardware):"
foreach ($metric in 'ftsVsScan', 'parallelSpeedup', 'usPerRow') {
    $vals = @()
    foreach ($r in $runs) {
        foreach ($s in $r.scenarios) {
            if ($null -ne $s.ratios.$metric) { $vals += [double]$s.ratios.$metric }
        }
    }
    if ($vals.Count) {
        $stat = $vals | Measure-Object -Average -Minimum -Maximum
        Write-Host ("  {0,-18} n={1,-4} avg={2,-8} min={3,-8} max={4}" -f `
            $metric, $vals.Count, [math]::Round($stat.Average, 2), [math]::Round($stat.Minimum, 2), [math]::Round($stat.Maximum, 2))
    }
}

Write-Host "`nAbsolute 1M-row ingest (compare only within a hardware class):"
foreach ($r in ($runs | Sort-Object { $_.machine.logicalCores })) {
    $s = $r.scenarios | Where-Object { $_.id -eq 'engine.text.1M' } | Select-Object -First 1
    if ($s -and $null -ne $s.cold.ingestMs) {
        Write-Host ("  {0,-32} {1,3}c {2,5}GB  ingest={3,7} ms  idleCPU={4}%" -f `
            $r.machine.cpuModel, $r.machine.logicalCores, $r.machine.ramGB, $s.cold.ingestMs, $r.systemLoad.idleCpuPercentBefore)
    }
}
