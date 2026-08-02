#requires -Version 7
<#
.SYNOPSIS
  Merge the repo's listing-as-code (store/listing.<locale>.json + store/screenshots) into a Store submission.

.DESCRIPTION
  Applies every store/listing.*.json to the submission from `msstore submission get`:
    - en-us always exists; its managed text fields are overwritten.
    - a NEW locale is created by cloning en-us's structure, then its text is overwritten.
  Screenshots: if store/screenshots/*.png exist, EVERY locale's Images[] is set to that set, referenced by a
  per-locale path ("<locale>/<file>.png") with FileStatus=PendingUpload, and a MANIFEST of
  "<zip-path>|<local-file>" lines is written (-ScreenshotManifest) so Upload-Screenshots.ps1 can put those
  bytes into the submission's upload zip. Without screenshots, new-locale listings are SKIPPED (a language
  listing with no screenshots is rejected by the Store as "incomplete").
  Emits the compact product JSON for `msstore submission update` (whose arg is the JSON content, not a path).
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)] [string] $CurrentSubmission,
  [string] $ListingDir = $PSScriptRoot,
  [string] $ScreenshotDir = (Join-Path $PSScriptRoot 'screenshots'),
  [Parameter(Mandatory)] [string] $OutFile,
  [string] $ScreenshotManifest,
  [string] $ReleaseNotes
)

$ErrorActionPreference = 'Stop'

$managed = @('Title','ShortTitle','ShortDescription','Description',
             'Keywords','Features','ReleaseNotes','CopyrightAndTrademarkInfo','LicenseTerms','DevStudio')

function Get-JsonBody([string]$path) {
  $text = Get-Content -Raw -LiteralPath $path
  $start = $text.IndexOf('{'); if ($start -lt 0) { throw "No JSON object in $path" }
  # `msstore submission get` console-wraps long JSON values, inserting raw newlines (+ continuation indent)
  # MID-VALUE — including mid `\uXXXX` escape for CJK listings. A raw newline inside a JSON string is invalid
  # JSON, so ConvertFrom-Json fails with "Invalid Unicode escape sequence: \u" (the reported space is the raw
  # newline). Strip CR/LF + any following indent so wrapped escapes/strings rejoin (\u4e<nl>2d -> 中; CJK
  # has no spaces so this is lossless). The managed text fields are overwritten from the repo's listing.*.json
  # afterward, so trimming a space from a preserved field can't reach the output. (Upload-Screenshots does the
  # same normalization.)
  return ($text.Substring($start)) -replace "\r?\n[ \t]*", ''
}
function Copy-Json($o) { $o | ConvertTo-Json -Depth 50 | ConvertFrom-Json -Depth 50 }
function Set-ManagedFields($base, $listing) {
  foreach ($k in $managed) { if ($listing.PSObject.Properties.Name -contains $k) { $base.$k = $listing.$k } }
  if ($script:ReleaseNotesOverride) { $base.ReleaseNotes = $script:ReleaseNotesOverride }
}
function Assert-Limits($loc, $b) {
  if ($b.Description.Length -gt 10000) { throw "[$loc] Description > 10000" }
  if ($b.ShortDescription -and $b.ShortDescription.Length -gt 1000) { throw "[$loc] ShortDescription > 1000" }
  if ($b.ReleaseNotes -and $b.ReleaseNotes.Length -gt 1500) { throw "[$loc] ReleaseNotes > 1500" }
  if ($b.Keywords.Count -gt 7) { throw "[$loc] > 7 Keywords" }
  if ($b.Features.Count -gt 20) { throw "[$loc] > 20 Features" }
  foreach ($f in $b.Features) { if ($f.Length -gt 200) { throw "[$loc] Feature > 200 chars: '$f'" } }
}

# Screenshots shared across all locales (English UI). Each locale references its OWN copy in the zip.
$shots = @(Get-ChildItem -LiteralPath $ScreenshotDir -Filter *.png -ErrorAction SilentlyContinue | Sort-Object Name)
$haveShots = $shots.Count -gt 0
$manifest = [System.Collections.Generic.List[string]]::new()
function Set-LocaleImages($base, $locale) {
  $imgs = @()
  foreach ($s in $shots) {
    $zipPath = "$locale/$($s.Name)"   # per-locale path inside the upload zip
    $imgs += [pscustomobject]@{ FileName = $zipPath; FileStatus = 'PendingUpload'; ImageType = 'Screenshot' }
    $manifest.Add("$zipPath|$($s.FullName)")
  }
  $base.Images = $imgs
}

$product = Get-JsonBody $CurrentSubmission | ConvertFrom-Json -Depth 50
$script:ReleaseNotesOverride = if ($PSBoundParameters.ContainsKey('ReleaseNotes') -and $ReleaseNotes) { $ReleaseNotes } else { $null }

$enus = $product.Listings.'en-us'
if ($null -eq $enus) { throw "Submission has no Listings.en-us to use as the template." }

$files = Get-ChildItem -LiteralPath $ListingDir -Filter 'listing.*.json' | Sort-Object Name
$enusFile = $files | Where-Object { $_.Name -eq 'listing.en-us.json' }
if (-not $enusFile) { throw "listing.en-us.json is required." }
Set-ManagedFields $enus.BaseListing (Get-Content -Raw $enusFile.FullName | ConvertFrom-Json -Depth 50)
if ($haveShots) { Set-LocaleImages $enus.BaseListing 'en-us' }   # replace en-us's screenshots with the curated set
Assert-Limits 'en-us' $enus.BaseListing

$applied = @('en-us')
foreach ($f in ($files | Where-Object { $_.Name -ne 'listing.en-us.json' })) {
  $locale = $f.Name -replace '^listing\.', '' -replace '\.json$', ''
  $listing = Get-Content -Raw $f.FullName | ConvertFrom-Json -Depth 50
  $existing = $product.Listings.PSObject.Properties.Name -contains $locale
  if ($existing) {
    Set-ManagedFields $product.Listings.$locale.BaseListing $listing
    if ($haveShots) { Set-LocaleImages $product.Listings.$locale.BaseListing $locale }
    Assert-Limits $locale $product.Listings.$locale.BaseListing
    $applied += $locale
  } elseif ($haveShots) {
    # New locale WITH screenshots available: create it (clone en-us structure, overwrite text + set images).
    $newListing = Copy-Json $enus
    Set-ManagedFields $newListing.BaseListing $listing
    Set-LocaleImages $newListing.BaseListing $locale
    Assert-Limits $locale $newListing.BaseListing
    $product.Listings | Add-Member -NotePropertyName $locale -NotePropertyValue $newListing -Force
    $applied += $locale
  } else {
    # No screenshots -> a new-locale listing would be "incomplete" and hang the commit. Skip it.
    Write-Host "SKIP $locale - no screenshots in $ScreenshotDir (a new-locale listing needs them); leaving it out."
  }
}

$json = $product | ConvertTo-Json -Depth 50 -Compress
if ($json.Length -gt 30000) { throw "Merged submission JSON is $($json.Length) chars - too close to the ~32767 command-line limit for 'msstore submission update'. Trim text or reduce locales." }
Set-Content -LiteralPath $OutFile -Value $json -Encoding utf8 -NoNewline
if ($ScreenshotManifest) { Set-Content -LiteralPath $ScreenshotManifest -Value ($manifest -join "`n") -Encoding utf8 }
Write-Host "Merged $($applied.Count) locale(s) -> $OutFile  [$($applied -join ', ')]  ($($json.Length) chars; $($shots.Count) screenshot(s)/locale, $($manifest.Count) image(s) to upload)."
