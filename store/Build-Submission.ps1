#requires -Version 7
<#
.SYNOPSIS
  Merge the repo's listing-as-code (store/listing.en-us.json) into a Store submission JSON.

.DESCRIPTION
  Takes the CURRENT submission (as returned by `msstore submission get <id>`) and overwrites
  ONLY the managed en-us BaseListing fields (Title, Description, Features, ReleaseNotes, ...)
  with the values from store/listing.en-us.json. Every other field (pricing, availability,
  packages, gaming options, ...) is passed through untouched, so we never clobber settings the
  repo doesn't own. Emits the merged product JSON for `msstore submission update`.

  `msstore submission get` prints a few human-readable lines before the JSON body; this script
  tolerates that by slicing from the first '{'.

.EXAMPLE
  ./Build-Submission.ps1 -CurrentSubmission raw.txt -OutFile product.json
#>
[CmdletBinding()]
param(
  # File containing `msstore submission get <id>` output (may have preamble lines before the JSON).
  [Parameter(Mandatory)] [string] $CurrentSubmission,
  # The managed listing fields. Defaults to the sibling listing.en-us.json.
  [string] $ListingFile = (Join-Path $PSScriptRoot 'listing.en-us.json'),
  [Parameter(Mandatory)] [string] $OutFile,
  # Optional: override the en-us ReleaseNotes (e.g. inject the version being shipped).
  [string] $ReleaseNotes
)

$ErrorActionPreference = 'Stop'

function Get-JsonBody([string]$path) {
  $text = Get-Content -Raw -LiteralPath $path
  $start = $text.IndexOf('{')
  if ($start -lt 0) { throw "No JSON object found in $path" }
  return $text.Substring($start)
}

$product = Get-JsonBody $CurrentSubmission | ConvertFrom-Json -Depth 50
$listing = Get-Content -Raw -LiteralPath $ListingFile | ConvertFrom-Json -Depth 50

$base = $product.Listings.'en-us'.BaseListing
if ($null -eq $base) { throw "Submission has no Listings.en-us.BaseListing to merge into." }

# The managed keys — only these are overwritten. Images are managed by the screenshot step,
# not here, so they are intentionally excluded.
$managed = @(
  'Title','ShortTitle','ShortDescription','Description',
  'Keywords','Features','ReleaseNotes','CopyrightAndTrademarkInfo','LicenseTerms','DevStudio'
)

foreach ($key in $managed) {
  if ($listing.PSObject.Properties.Name -contains $key) {
    $base.$key = $listing.$key
  }
}
if ($PSBoundParameters.ContainsKey('ReleaseNotes') -and $ReleaseNotes) {
  $base.ReleaseNotes = $ReleaseNotes
}

# Basic Store-limit guardrails — fail loudly here rather than eat a Store rejection later.
if ($base.Description.Length -gt 10000) { throw "Description exceeds 10000 chars ($($base.Description.Length))." }
if ($base.ShortDescription -and $base.ShortDescription.Length -gt 1000) { throw "ShortDescription exceeds 1000 chars." }
if ($base.ReleaseNotes -and $base.ReleaseNotes.Length -gt 1500) { throw "ReleaseNotes exceeds 1500 chars." }
if ($base.Keywords.Count -gt 7) { throw "More than 7 Keywords ($($base.Keywords.Count))." }
if ($base.Features.Count -gt 20) { throw "More than 20 Features ($($base.Features.Count))." }
foreach ($f in $base.Features) { if ($f.Length -gt 200) { throw "Feature exceeds 200 chars: '$f'" } }

$product | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $OutFile -Encoding utf8
Write-Host "Merged listing -> $OutFile  (Title='$($base.Title)', Description=$($base.Description.Length) chars, Features=$($base.Features.Count), Keywords=$($base.Keywords.Count))"
