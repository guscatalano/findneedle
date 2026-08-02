#requires -Version 7
<#
.SYNOPSIS
  Merge the repo's listing-as-code (store/listing.<locale>.json) into a Store submission JSON.

.DESCRIPTION
  Takes the CURRENT submission (as returned by `msstore submission get <id>`) and applies every
  store/listing.*.json file:
    - en-us always exists in the submission -> its managed BaseListing fields are overwritten.
    - a NEW locale (no entry yet) -> a full BaseListing is created by cloning en-us (so all
      structural fields AND the screenshots/Images[] are present), then its text fields are
      overwritten from the locale file.
  Only the managed text fields are ever touched; pricing, availability, packages, and the
  screenshots themselves pass through untouched. Emits the merged product JSON for
  `msstore submission update` (whose argument is the JSON *content*, not a path).

  `msstore submission get` prints a few human-readable lines before the JSON body; this script
  tolerates that by slicing from the first '{'.

.EXAMPLE
  ./Build-Submission.ps1 -CurrentSubmission raw.txt -OutFile product.json
#>
[CmdletBinding()]
param(
  # File containing `msstore submission get <id>` output (may have preamble lines before the JSON).
  [Parameter(Mandatory)] [string] $CurrentSubmission,
  # Folder holding listing.<locale>.json files. Defaults to this script's folder.
  [string] $ListingDir = $PSScriptRoot,
  [Parameter(Mandatory)] [string] $OutFile,
  # Optional: override every locale's ReleaseNotes (e.g. inject the version being shipped).
  [string] $ReleaseNotes
)

$ErrorActionPreference = 'Stop'

# The managed keys — only these are overwritten/copied from a locale file. Images are handled
# separately (cloned from en-us for new locales, passed through for existing ones).
$managed = @(
  'Title','ShortTitle','ShortDescription','Description',
  'Keywords','Features','ReleaseNotes','CopyrightAndTrademarkInfo','LicenseTerms','DevStudio'
)

function Get-JsonBody([string]$path) {
  $text = Get-Content -Raw -LiteralPath $path
  $start = $text.IndexOf('{')
  if ($start -lt 0) { throw "No JSON object found in $path" }
  return $text.Substring($start)
}

function Copy-Json($obj) { $obj | ConvertTo-Json -Depth 50 | ConvertFrom-Json -Depth 50 }

function Set-ManagedFields($base, $listing) {
  foreach ($key in $managed) {
    if ($listing.PSObject.Properties.Name -contains $key) { $base.$key = $listing.$key }
  }
  if ($script:ReleaseNotesOverride) { $base.ReleaseNotes = $script:ReleaseNotesOverride }
}

function Assert-Limits($locale, $base) {
  if ($base.Description.Length -gt 10000) { throw "[$locale] Description exceeds 10000 chars ($($base.Description.Length))." }
  if ($base.ShortDescription -and $base.ShortDescription.Length -gt 1000) { throw "[$locale] ShortDescription exceeds 1000 chars." }
  if ($base.ReleaseNotes -and $base.ReleaseNotes.Length -gt 1500) { throw "[$locale] ReleaseNotes exceeds 1500 chars." }
  if ($base.Keywords.Count -gt 7) { throw "[$locale] More than 7 Keywords ($($base.Keywords.Count))." }
  if ($base.Features.Count -gt 20) { throw "[$locale] More than 20 Features ($($base.Features.Count))." }
  foreach ($f in $base.Features) { if ($f.Length -gt 200) { throw "[$locale] Feature exceeds 200 chars: '$f'" } }
}

$product = Get-JsonBody $CurrentSubmission | ConvertFrom-Json -Depth 50
$script:ReleaseNotesOverride = if ($PSBoundParameters.ContainsKey('ReleaseNotes') -and $ReleaseNotes) { $ReleaseNotes } else { $null }

# en-us must exist in the submission and is our clone template (it carries the screenshots).
$enusListing = $product.Listings.'en-us'
if ($null -eq $enusListing) { throw "Submission has no Listings.en-us to use as the template." }

$files = Get-ChildItem -LiteralPath $ListingDir -Filter 'listing.*.json' | Sort-Object Name
if (-not $files) { throw "No store/listing.*.json files found in $ListingDir" }

# Apply en-us first so its merged BaseListing (with Images) is the template for new locales.
$enusFile = $files | Where-Object { $_.Name -eq 'listing.en-us.json' }
if (-not $enusFile) { throw "listing.en-us.json is required (it's the default locale + clone template)." }
Set-ManagedFields $enusListing.BaseListing (Get-Content -Raw $enusFile.FullName | ConvertFrom-Json -Depth 50)
Assert-Limits 'en-us' $enusListing.BaseListing

$applied = @('en-us')
foreach ($f in ($files | Where-Object { $_.Name -ne 'listing.en-us.json' })) {
  $locale = $f.Name -replace '^listing\.', '' -replace '\.json$', ''
  $listing = Get-Content -Raw $f.FullName | ConvertFrom-Json -Depth 50
  $existing = $product.Listings.PSObject.Properties.Name -contains $locale
  if ($existing) {
    Set-ManagedFields $product.Listings.$locale.BaseListing $listing
    Assert-Limits $locale $product.Listings.$locale.BaseListing
    $applied += $locale
  } else {
    # NEW-LOCALE CREATION IS DISABLED. A new language listing needs its OWN uploaded screenshots — cloning
    # en-us's Images[] metadata does NOT satisfy the Store: the listing shows "incomplete (missing
    # screenshots)" and the submission commit hangs (seen on v1.0.232). We can't upload per-locale
    # screenshots yet (metadata-only, no image bytes). Re-enable this branch once real per-locale
    # screenshot UPLOAD exists. The translated listing.<locale>.json files stay in the repo, ready.
    Write-Host "SKIP $locale - a new-locale listing needs its own uploaded screenshots (not yet supported); leaving it out to keep the submission complete."
  }
}

# Compress: `submission update` takes the JSON as an inline command-line argument, and Windows caps
# a command line at ~32767 chars. Compact JSON (no pretty-print whitespace) buys headroom; the guard
# below fails loudly if a future locale would still overflow it (rather than truncating silently).
$json = $product | ConvertTo-Json -Depth 50 -Compress
if ($json.Length -gt 30000) {
  throw "Merged submission JSON is $($json.Length) chars — too close to the ~32767 command-line limit for 'msstore submission update'. Trim listing text or reduce locales."
}
Set-Content -LiteralPath $OutFile -Value $json -Encoding utf8 -NoNewline
Write-Host "Merged $($applied.Count) locale(s) -> $OutFile  [$($applied -join ', ')]  ($($json.Length) chars). New-locale listings are skipped until per-locale screenshots can be uploaded."
