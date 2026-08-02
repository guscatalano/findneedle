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

- **`en-us` is the default and the template.** It must exist; new locales are created by cloning its
  BaseListing (structure **and** screenshots) and overwriting the text — so a new locale is never
  missing required fields or images.
- **Add a locale:** drop in `listing.<locale>.json` (copy an existing one, translate the text). The
  next release picks it up automatically — no code change. Use Store locale codes (`de-de`, `fr-fr`,
  `ja-jp`, `zh-cn`, …).
- **Keep it under the limit.** `msstore submission update` takes the whole product JSON as one
  command-line argument, capped at ~32767 chars by Windows. The script emits compact JSON and **fails
  the build at 30000 chars** — if you hit that, trim text or split the release across fewer locales.
- Technical acronyms (ETL, WPP, ETW, TMF, UML, SMB, GUID, CSV/JSON/ZIP) are intentionally left in
  their original form across all locales.

## How a release publishes the listing

The `publish-store` job (`.github/workflows/dotnet-desktop.yml`) does:

1. `msstore publish <msix> -id 9NWLTBV4NRDL --noCommit` — create the pending draft with the package,
   don't commit.
2. `msstore submission get` → `Build-Submission.ps1` → `msstore submission update` — merge this
   folder's listing into the draft.
3. `msstore submission publish` — commit the draft (package + listing) into certification.

If a previous run left a **stuck pending submission**, clear it once with
`msstore submission delete 9NWLTBV4NRDL` before re-tagging.

## Screenshots (currently manual)

The `Images[]` in the submission are **passed through untouched** by the merge — the release job does
not yet upload screenshots. Manage them in Partner Center → your app → the submission → Store
listings → Screenshots.

To (re)generate a fresh set from the committed demo logs, run the FlaUI generator
(`FindNeedleUX.UITests/GenerateReadmeScreenshots.cs`) locally — it drives the app over
`Samples/Demo/logs` and writes deterministic PNGs. Generation is intentionally **local and
human-reviewed** (headless CI can't drive the desktop UI reliably); publishing stays deterministic.

**Automating screenshot upload is the planned next step** — it needs a scripted zip upload to the
pending submission's `FileUploadUrl` (the legacy submission API bundles package + image bytes in one
zip), which warrants its own validation before going live.
