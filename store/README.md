# Store listing-as-code

This folder is the **source of truth for the Microsoft Store listing** (product `9NWLTBV4NRDL`).
On every `v*.*.*` release tag, the `publish-store` CI job pushes the package **and** this listing
text to the Store in a single submission.

> **Authority note:** because CI overwrites the managed listing fields on each release, **direct
> edits to those fields in Partner Center get reverted on the next tag.** Edit them *here* instead.

## Files

- **`listing.<locale>.json`** — one file per Store locale (`listing.en-us.json`, `listing.de-de.json`,
  …). Each holds the managed listing fields: `Title`, `ShortDescription`, `Description`, `Features`,
  `Keywords`, `ReleaseNotes`, etc. CI reads the current submission, overwrites *only* these keys per
  locale, and pushes it back — so unmanaged fields (pricing, availability, packages, gaming options)
  are never touched. `Title` must match a reserved app name in Partner Center (keep it identical
  across locales). Store limits are enforced by `Build-Submission.ps1` (fails the build, not the cert
  review).
- **`Build-Submission.ps1`** — merges *all* `listing.*.json` into a `msstore submission get` dump and
  emits the compact `product.json` for `msstore submission update`. Run it standalone to preview a
  merge.
- **`screenshots/`** — the curated screenshot set (see below).

### Locales

- **`en-us` is the default and the template.** It must exist; a new locale is created by cloning its
  BaseListing structure, overwriting the text, and setting its own screenshots (see below) — so a new
  locale is never missing required fields or images.
- **Add a locale:** drop in `listing.<locale>.json` (copy an existing one, translate the text). The
  next release picks it up automatically — no code change. Use Store locale codes (`de-de`, `fr-fr`,
  `ja-jp`, `zh-cn`, …).
- **Keep it under the limit.** `msstore submission update` takes the whole product JSON as one
  command-line argument, capped at ~32767 chars by Windows. The script emits compact JSON and **fails
  the build at 30000 chars** — if you hit that, trim text or split the release across fewer locales.
- Technical acronyms (ETL, WPP, ETW, TMF, UML, SMB, GUID, CSV/JSON/ZIP) are intentionally left in
  their original form across all locales.
- **Verifying a stored listing:** `msstore submission get` line-wraps its terminal output and a wrap
  can land *inside* a `\uXXXX` escape (breaks JSON parsers on CJK). This is a display quirk, not
  corruption — the API rejects malformed escapes, so a successful `submission update` means the data
  stored correctly. To re-parse the dump, strip raw newlines first (real ones are escaped as `\n`).

## How a release publishes the listing

The `publish-store` job (`.github/workflows/dotnet-desktop.yml`) does:

1. `msstore publish <msix> -id 9NWLTBV4NRDL --noCommit` — create the pending draft with the package,
   don't commit. (This uploads a zip = {the package} to the submission's `FileUploadUrl`.)
2. `msstore submission get` → **`Build-Submission.ps1`** — merge this folder's listings (all locales)
   into the draft and set each locale's `Images[]` to the `screenshots/` set (writes a manifest of
   `<zip-path>|<file>` lines).
3. **`Upload-Screenshots.ps1`** — GET that zip, inject the per-locale screenshot copies, PUT it back
   (preserves the package, adds the images). Legacy API keeps packages + images in ONE zip.
4. `msstore submission update` (the merged JSON) → `msstore submission publish` — commit into certification.

If a previous run left a **stuck pending submission**, clear it once with
`msstore submission delete 9NWLTBV4NRDL` before re-tagging.

## Screenshots (automated)

- **`screenshots/*.png`** — the shared screenshot set (English UI). Every locale references its OWN copy
  in the submission zip (`<locale>/<file>.png`) — the Store requires each language listing to have its
  own uploaded images; a metadata reference to another locale's image is rejected as "incomplete".
- **Requirements:** PNG, ≥ 1366×768, ≤ 10 per locale.
- **`Upload-Screenshots.ps1`** does the actual upload (GET the submission zip → add each `<locale>/<file>`
  → PUT). It runs in CI between `Build-Submission` and `submission update`.
- **No screenshots present?** `Build-Submission.ps1` then **skips new-locale listings** (a language with
  no screenshots hangs the commit as "incomplete") and ships en-us only.
- **Refresh the set:** run the FlaUI generator (`FindNeedleUX.UITests/GenerateReadmeScreenshots.cs`)
  over `Samples/Demo/logs`, drop the PNGs in `screenshots/`, commit — the next release uploads them.

> The legacy submission API bundles the package **and** image bytes in the single `FileUploadUrl` zip and
> has no per-image API, so `Upload-Screenshots.ps1` re-uploads the ~90 MB zip on every release. Commit-time
> image ingestion is only fully proven by a real publish (the local `--noCommit` dry-run validates the
> upload + per-locale registration, not the final commit).
