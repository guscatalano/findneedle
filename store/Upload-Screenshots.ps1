#requires -Version 7
<#
.SYNOPSIS
  Upload the listing screenshots into a pending Store submission's file zip.

.DESCRIPTION
  The Microsoft Store legacy submission API keeps packages AND listing images in ONE zip at the submission's
  FileUploadUrl (there is no per-image upload API, and msstore has no image command). After
  `msstore publish --noCommit` uploads the package zip, this script:
    1. GETs that zip (the SAS is read+write),
    2. injects each screenshot at its per-locale path (from Build-Submission.ps1's manifest, "<zip-path>|<file>"),
    3. PUTs the zip back — preserving the package, adding the images.
  Build-Submission.ps1 must have referenced the same paths in each locale's Images[] (FileStatus=PendingUpload).
  Run BEFORE `msstore submission update` / `submission publish`.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)] [string] $CurrentSubmission,   # `msstore submission get` output (has FileUploadUrl)
  [Parameter(Mandatory)] [string] $ScreenshotManifest,  # "<zip-path>|<local-file>" lines from Build-Submission
  [string] $WorkDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (-not $WorkDir) { $WorkDir = [IO.Path]::GetTempPath() }

$entries = @(Get-Content -LiteralPath $ScreenshotManifest -ErrorAction SilentlyContinue | Where-Object { $_.Trim() })
if ($entries.Count -eq 0) { Write-Host "No screenshots to upload (empty manifest) - skipping."; return }

# FileUploadUrl from the submission dump (tolerate the CLI's preamble + line-wrapping — strip CR/LF plus any
# continuation indent so a `\uXXXX` escape the CLI wrapped mid-sequence rejoins instead of breaking the parse).
$text = Get-Content -Raw -LiteralPath $CurrentSubmission
$text = $text.Substring($text.IndexOf('{')) -replace "\r?\n[ \t]*", ''
$sub = $text | ConvertFrom-Json -Depth 50
$url = $sub.FileUploadUrl
if ([string]::IsNullOrWhiteSpace($url)) {
  throw "No FileUploadUrl on the pending submission - a pending submission from 'msstore publish --noCommit' is required."
}

$zip = Join-Path $WorkDir ("subupload_" + [guid]::NewGuid().ToString('N') + ".zip")
try {
  Write-Host "Downloading current submission zip..."
  Invoke-WebRequest -Uri $url -Method Get -OutFile $zip -ErrorAction Stop | Out-Null
  Write-Host ("  got {0:N0} bytes" -f (Get-Item $zip).Length)

  $z = [System.IO.Compression.ZipFile]::Open($zip, 'Update')
  try {
    foreach ($line in $entries) {
      $parts = $line -split '\|', 2
      if ($parts.Count -ne 2) { continue }
      $zipPath = $parts[0].Trim(); $local = $parts[1].Trim()
      if (-not (Test-Path -LiteralPath $local)) { throw "screenshot file not found: $local" }
      $ex = $z.GetEntry($zipPath); if ($ex) { $ex.Delete() }
      [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, $local, $zipPath) | Out-Null
      Write-Host "  + $zipPath"
    }
  } finally { $z.Dispose() }

  Write-Host "Uploading updated zip..."
  $bytes = [IO.File]::ReadAllBytes($zip)
  Invoke-WebRequest -Uri $url -Method Put -Headers @{ 'x-ms-blob-type' = 'BlockBlob' } -Body $bytes -ContentType 'application/zip' -ErrorAction Stop | Out-Null
  Write-Host ("Uploaded {0} screenshot(s) into the submission zip ({1:N0} bytes)." -f $entries.Count, $bytes.Length)
} finally {
  Remove-Item -LiteralPath $zip -ErrorAction SilentlyContinue
}
