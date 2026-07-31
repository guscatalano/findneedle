# Managed WPP decoder — known gaps

Honest inventory of what the managed WPP decoder (`ETWPlugin/Wpp/`) may miss. The type surface is
exhaustively covered and validated byte-for-byte against tracefmt (see `managed-wpp-decoder.md`), but the
fixtures didn't exercise these edges. Status legend: 🔧 fixing now · ⏳ deferred · ♾ inherent limitation.

## Being fixed (1–8)

1. **🔧 ANSI strings decode as ASCII, not the ANSI code page.** `ItemString`/`ItemPString` used
   `Encoding.ASCII`, so any high-byte char (é, ü, ©, … in CP-1252) came out wrong/`?`. tracefmt uses the
   system ANSI code page. → decode with the ANSI code page (1252 / `Encoding.Default`).

2. **🔧 32-bit captures untested.** Pointer width comes from `source.PointerSize`, but every fixture is x64.
   `ItemPtr`/`%p`/`%Iu` on a 32-bit trace (4-byte pointers) was unproven. → add a 32-bit-pointer test of the
   format engine.

3. **🔧 Truncated / oversized messages.** WPP caps a single message to the buffer size; there's no cross-event
   reassembly (each `DoTraceMessage` is one event), and an over-run arg blob must degrade gracefully rather
   than throw. → confirm the bounds-checked readers return empty on a short blob (test) + document.

4. **🔧 WPP meta specifiers in the message body render wrong.** `%!FUNC!`, `%!LEVEL!`, `%!FLAGS!`,
   `%!STDPREFIX!` (not arg types — resolved from event/TMF context) were emitted literally. → substitute from
   the TMF entry (`Func`, `Level`) in the format engine.

5. **🔧 WPP level/severity not carried through.** The decoded rows didn't set the event level, so viewer
   level-filtering treated them as unknown. The managed decoder has the level in hand (event + TMF `LEVEL=`).
   → carry it onto the row.

6. **🔧 CPU number hardcoded to `[0]`.** The synthesized row used cpu 0; the real processor number is
   available. → carry the real CPU.

7. **🔧 Activity IDs not captured.** `ActivityID` / `RelatedActivityID` (key for causal-sequence correlation)
   weren't surfaced. → carry both onto the row.

8. **🔧 `ItemFloat` unformatted.** Returned the raw float instead of printf `%g` (rare — WPP promotes
   float→double). → format like `ItemDouble`.

## Deferred / inherent

9. **♾ Symbol-table version drift & aliases.** Tables are generated from one SDK (10.0.26100). Codes added in
   other SDK versions fall back to bare hex, and same-value aliases can differ from a given tracefmt
   (e.g. `OID_GEN_SUPPORTED_LIST` vs `OID_GEN_CO_SUPPORTED_LIST`). Graceful, not tracefmt-identical everywhere.

10. **⏳ Mixed WPP + modern traces in Managed mode.** A trace with both WPP *and* manifest/EventSource events:
    the managed path decodes only the WPP portion and skips the rest (no TMF match), whereas the pipeline can
    fall back to TraceEvent for modern events. Managed mode would silently drop the modern events — worth at
    least detecting + logging.

11. **♾ HRESULTs from component-specific facilities** (outside `winerror.h`) → hex fallback (no symbol).
