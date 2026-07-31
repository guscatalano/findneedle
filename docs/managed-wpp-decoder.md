# Managed WPP decoder — prototype

**Goal:** decode classic WPP (TMF-based) ETL events in managed C#, with no dependency on the WDK's
`tracefmt.exe`. Same spirit as keeping `PcapPlugin` managed-only. This is the only path that would let
FindNeedle drop its WDK/tracefmt native dependency for WPP.

**Status: working prototype, validated end-to-end against a real capture.** Not yet wired into the
`ETLProcessor` decode path — it's a standalone decoder (`ETWPlugin/Wpp/`) plus tests.

## The pieces

tracefmt does three things; we reimplemented all three in managed code:

1. **Load the TMF table** — `TmfDatabase` parses `*.tmf` (`tracepdb -f` output) into a lookup keyed on
   `(message GUID, message number)`, each entry holding the printf-style format string + the ordered,
   typed argument list.
2. **Read the WPP events off the ETL wire** — `ManagedWppEtlDecoder`. This turned out to be the easy part:
   **TraceEvent already de-frames each classic WPP event for us.** The probe (`ManagedWppProbe`, run against
   a real WppEmitter capture) established the mapping decisively:
   - `TraceEvent.TaskGuid`   = the WPP **message GUID** (the `.tmf` filename)
   - `TraceEvent.ID`         = the WPP **message number** (the `#typev` number)
   - `TraceEvent.EventData()` = **exactly the packed argument blob**, no extra header
   So we never touch the raw WPP framing — we read the ETL with TraceEvent (a managed nuget lib, no WDK)
   and get the three things we need directly.
3. **Decode args + apply the format** — `WppMessageFormatter` reads each typed argument off the blob
   (`ItemLong` = int32, `ItemULong`, `ItemLongLong`, pointer, char, counted strings, …) and applies the
   `%N!spec!` format string (`d/i/u/x/X/p/c/s`, width + zero-pad, length modifiers stripped). `%0` is the
   WPP prefix (provider/time/pid live in separate ETLLogLine fields); user args are `%10`+.

## What's validated

- `Tmf_Parses_BothWppEmitterStatements` — parses WppEmitter's real committed `.tmf`.
- `Format_WorkItem/Detail_MatchesTracefmtOutput` — renders both statements byte-for-byte as tracefmt does,
  from hand-built arg blobs.
- **`ManagedDecode_RealWppEmitterEtl_MatchesTracefmt`** — the headline: decodes a **real 16 KB WppEmitter
  capture** (committed at `ETWPluginTests/WppFixtures/wppemitter-sample.etl`) fully managed, and asserts the
  exact per-event strings (30 work-item + 15 detail events, including `%x` hex: `27 → 0x1b`). No tracefmt,
  no WDK, no admin — runs in CI.
- **`ManagedDecode_RealStringArgs_MatchesTracefmt`** — decodes a real capture with **string args** (committed
  `WppFixtures/wppstr-sample.etl` + its TMF, from `tools/WppStrEmitter`): `ItemString` (ANSI) + `ItemLong`,
  and `ItemWString` (wide). Byte-for-byte vs tracefmt. This is what caught the wire-format bug below.
- **`ManagedDecode_Win32AndPointerTypes_MatchesTracefmt`** — decodes a real capture (`tools/WppTypesEmitter`)
  covering the common **C++/Win32 + pointer types**, byte-for-byte vs tracefmt: 32/64-bit signed/unsigned/hex
  ints (`ItemLong`, `ItemLongLong`, `ItemLongLongX`, `%u`/`%x`/`%I64x`), **`%p` pointers** (uppercase,
  pointer-width zero-padded), width/zero-pad (`%05d`/`%08x`), **HRESULT** + **NTSTATUS** (`0x…(SYMBOL)`), and
  **GUID** (`ItemGuid`, 16 inline bytes → standard GUID string).
- `ApplyFormat_*` / `DecodeArgs_*` — the format engine + arg reader in isolation.

## What's NOT done / known gaps

- **Integer/hex AND string args are validated against real captures.** `ItemLong` (`%d`/`%x`) via WppEmitter;
  `ItemString` (NUL-terminated ANSI) + `ItemWString` (NUL-terminated UTF-16) via WppStrEmitter. NOTE: the
  string test caught a real bug — the framing is **NUL-terminated inline**, not the USHORT-length-prefix the
  formatter first assumed. A self-referential unit test would have "passed" against the wrong assumption; the
  real capture is what corrected it. Other numeric widths (short/longlong/pointer) are implemented but only
  unit-tested, not capture-validated.
- **HRESULT/NTSTATUS symbolic names are a PARTIAL table.** `ItemHRESULT`/`ItemNTSTATUS` render `0x{hex}(SYMBOL)`
  exactly like tracefmt for the common codes we carry (ACCESS_DENIED, E_FAIL, …) and fall back to just the hex
  otherwise. tracefmt's full table comes from the WDK's WppConfig `.ini` files — porting it is the remaining
  work for exact parity on arbitrary codes. `ItemGuid`, `%p`, and the int/hex types are fully matched.
- **Other exotic WPP custom types** — `!SID!`, `!TID!`, `!ASSERT!`, `!TIME!`, `!IPADDR!`, etc. — not yet
  handled (rendered as the raw value or skipped). Same porting approach as the status table.
- **Floats** (`ItemFloat`/`ItemDouble`) are implemented but not capture-validated (WPP float support is spotty).
- **Reserved `%1..%9`** (WPP standard fields: sequence, flags, etc.) render empty. WppEmitter doesn't use
  them; some providers do.
- **Not wired into `ETLProcessor`.** Wiring it in as an alternative to the tracefmt shell-out (behind a flag)
  is the next step if we want to actually drop the WDK dependency on the WPP path.
- **Pointer size** is taken from `source.PointerSize`; untested on a 32-bit capture.

## Why this matters

If completed, this removes the last hard native/WDK dependency on the WPP decode path: no tracefmt.exe, no
`SampleWDK` bundling, no "install the WDK" prerequisite — WPP ETLs decode with just the managed TraceEvent
reader + this code. The heavy lift left is the long tail of WPP type/spec coverage, not the core mechanism
(which is proven).

- **`ManagedDecode_CountedStringsSidAndMore_MatchesTracefmt`** — round 2 (`tools/WppTypes2Emitter`): counted
  strings (`ItemPString`/`ItemPWString` — `ANSI_STRING`/`UNICODE_STRING`/`std::string`), `ItemSid`, GUID
  aliases (`ItemCLSID`/`ItemIID`), `ItemWINERROR`, `ItemIPAddr`, `ItemPort`, `ItemLongLongXX`/`ItemLongLongO`.
  All vs tracefmt (one deliberate divergence: SID → canonical `S-1-5-18`, not tracefmt's account-name lookup).

## WPP type coverage (from the WDK `defaultwpp.ini` — the authoritative list)

**Handled + capture-validated:** `ItemLong` (SINT/UINT/SLONG/ULONG), `ItemShort`, `ItemChar`, `ItemLongLong`,
`ItemULongLong`, `ItemLongLongX` (`%I64x`), `ItemLongLongXX` (`%I64X`), `ItemLongLongO` (`%I64o`), `ItemPtr`
(`%p`), `ItemString`/`ItemWString` (NUL-term), `ItemPString`/`ItemPWString` (counted), `ItemHRESULT`,
`ItemNTSTATUS`, `ItemWINERROR`, `ItemGuid`/`ItemCLSID`/`ItemIID`/`ItemLIBID`, `ItemSid`, `ItemIPAddr`, `ItemPort`,
**`ItemList*`** (enum → `0x{hex}(name)`) and **`ItemSet*`** (bitset → `[a,b]`) — the value→name tables are parsed
out of the TMF TypeName (`ItemListByte(Low,APC,DPC)`).

**Implemented, not capture-validated:** `ItemDouble`, `ItemRString`/`ItemRWString` (raw LPCSTR/LPCWSTR variants).

**Handled + capture-validated (cont.):** `ItemChar4` (FourCC → 4 ASCII bytes, `RGBA`), `ItemTimestamp`
(FILETIME → **UTC** — deliberately diverges from tracefmt's timezone-dependent LOCAL time, for a portable result),
`ItemDouble` (printf `%g`), `ItemTimeDelta` (`5.000s`), `ItemRString`/`ItemRWString` (raw LPCSTR/LPCWSTR, NUL-term).

**Status/error symbol tables are COMPLETE**, not a hand-picked subset. `ItemHRESULT`, `ItemNTSTATUS`, and
`ItemWINERROR` resolve against the full tables **generated from the SDK `ntstatus.h` + `winerror.h`**
(`tools/wpp-symbol-tables/gen.py` → `ETWPlugin/Wpp/Tables/*.txt`, embedded as resources, loaded lazily):
**2,843 NTSTATUS + 2,857 Win32 + 3,602 HRESULT = 9,302 codes.** A FACILITY_WIN32 HRESULT (`0x8007xxxx`) renders
the Win32 name like tracefmt (`0x80070005 → ERROR_ACCESS_DENIED`). Unknown codes fall back to the bare hex/decimal.

**Not yet handled (rarer / need structure or the PDB):** `ItemEnum`/`ItemFlagsEnum` (PDB-symbol-resolved enums —
distinct from the TMF-embedded `ItemList*`/`ItemSet*` we do handle), `ItemHexDump` (BIN buffer dump — needs a
user-defined length+buffer macro), `ItemWaitTime`, `ItemNDIS`.

**Deliberate divergences from tracefmt** (chosen for portability/determinism): `ItemSid` → canonical `S-1-5-18`
(not account-name lookup); `ItemTimestamp` → UTC (not local time). Both are TZ/locale/machine-independent.

## Real-world coverage run (throwing random machine WPP at it)

Captured 456 MB of real activity (`wpr -start GeneralProfile`, ~2.5M events) and ran the decoder over all
of it (`ManagedWppProbe.Probe_CoverageReport`):

- **2,490,652** events total; **191,729** were WPP-shaped (classic/unhandled + a task GUID), across **6**
  distinct message GUIDs; processed in ~3 s.
- **Zero decode exceptions** — the arg reader survived every real, varied WPP blob. (Robustness: the point
  of this run.)
- **0 decoded** — because those 6 are OS providers' **message GUIDs** (the `.tmf` source-file identity, not
  control GUIDs — you can't even enable them directly), and their format strings live in the components'
  **private** PDBs, which we don't have.

Is that a decoder gap, or fundamental? Ran the **real WDK tracefmt with Microsoft's public symbol server**
(`_NT_SYMBOL_PATH=srv*…msdl`) over an equivalent capture: **every** WPP message GUID it saw
(`a669021c` ×350K, `def2fe46` ×85K, `3d6fa8d1` ×39K, `e43445e0` ×25K, …) landed in **Unknown/Error**. Not a
single OS WPP provider formatted — Microsoft strips WPP trace-format data from public symbols. (tracefmt's
millions of "formatted" lines were **kernel MOF + manifest** events, which TraceEvent/our pipeline already
decode — not WPP.)

**Conclusion:** random OS WPP is undecodable without the owning component's private PDB — for tracefmt and
for us alike. The managed decoder's job is to (a) read all WPP robustly [proven at scale] and (b) fully
decode whatever TMFs the user *can* supply [proven byte-for-byte vs tracefmt]. It is not, and can't be,
"decode arbitrary OS WPP with no symbols" — nothing can.

## Files

- `ETWPlugin/Wpp/TmfDatabase.cs` — TMF parser.
- `ETWPlugin/Wpp/WppMessageFormatter.cs` — arg decode + printf format engine.
- `ETWPlugin/Wpp/ManagedWppEtlDecoder.cs` — ETL → decoded events (via TraceEvent).
- `ETWPluginTests/ManagedWppDecoderTests.cs` / `ManagedWppEndToEndTests` — unit + end-to-end tests.
- `ETWPluginTests/ManagedWppProbe.cs` — the wire-format calibration probe + coverage/robustness report over an
  arbitrary capture (both gated on env vars).
- `ETWPluginTests/WppFixtures/wppemitter-sample.etl` — the 16 KB real capture used by the E2E test.
