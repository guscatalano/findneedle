# Managed WPP decoder — known gaps

Honest inventory of what the managed WPP decoder (`ETWPlugin/Wpp/`) may miss. The type surface is
exhaustively covered and validated byte-for-byte against tracefmt (see `managed-wpp-decoder.md`), but the
fixtures didn't exercise these edges. Status legend: 🔧 fixing now · ⏳ deferred · ♾ inherent limitation.

## Fixed (1–8)

1. **✅ ANSI code page.** `ItemString`/`ItemPString` now decode with `Encoding.Latin1` (0xA0–0xFF identical to
   CP-1252, no `CodePages` dependency) instead of ASCII, so high-byte chars survive. Test:
   `Gap1_AnsiString_DecodesHighBytes_NotAscii`. (Only 0x80–0x9F — curly quotes/dashes — differs from CP-1252.)

2. **✅ 32-bit captures.** Pointer width is threaded through read + format; `%p` pads to the pointer width.
   Test `Gap2_Pointer_32bit_ReadsAndPadsToPointerWidth` proves `ItemPtr` consumes 4 bytes and `%p`→8 hex on a
   32-bit trace.

3. **✅ Truncated messages degrade gracefully.** Bounds-checked readers return empty on an over-run blob rather
   than throw (no cross-event reassembly — each `DoTraceMessage` is one event). Test
   `Gap3_TruncatedBlob_DegradesGracefully_NoThrow`.

4. **✅ Meta specifiers.** `%!FUNC!`/`%!LEVEL!`/`%!FLAGS!`/`%!STDPREFIX!` are substituted from the TMF entry.
   Test `Gap4_MetaSpecifiers_SubstituteFromTmfEntry`.

5. **✅ Level carried (when present).** Managed mode now builds `ETLLogLine` rows directly and sets
   `eventLevel` from `ev.Level` (was `-1`/unknown). NOTE: WPP doesn't always populate a severity — WppEmitter
   logs at level 0/Always — so the value is whatever the provider set; the *plumbing* now carries it.

6. **✅ Real CPU.** The row uses `ev.ProcessorNumber` (was hardcoded `[0]`).

7. **✅ Activity IDs.** `ActivityID` / `RelatedActivityID` (+ the message/provider GUID) are set on every
   managed row. Test `ManagedMode_RowsCarryEventFields`.

8. **✅ `ItemFloat` → `%g`** (6 sig figs, like `ItemDouble`). Test `Gap8_Float_FormattedAsG`.

> **Implementation note:** fixing 5/6/7 meant Managed mode now emits `ETLLogLine` rows *directly* (carrying
> level/cpu/activity) instead of the tracefmt-format text round-trip. That buffers the rows in memory during
> decode rather than streaming from disk — fine for typical traces; a future optimization could stream. (Auto
> mode still uses tracefmt where the WDK exists, unaffected. Compare mode's managed side still uses the text
> path, so its rows carry cpu but not level/activity.)

## Deferred / inherent

9. **♾ Symbol-table version drift & aliases.** Tables are generated from one SDK (10.0.26100). Codes added in
   other SDK versions fall back to bare hex, and same-value aliases can differ from a given tracefmt
   (e.g. `OID_GEN_SUPPORTED_LIST` vs `OID_GEN_CO_SUPPORTED_LIST`). Graceful, not tracefmt-identical everywhere.

10. **⏳ Mixed WPP + modern traces in Managed mode.** A trace with both WPP *and* manifest/EventSource events:
    the managed path decodes only the WPP portion and skips the rest (no TMF match), whereas the pipeline can
    fall back to TraceEvent for modern events. Managed mode would silently drop the modern events — worth at
    least detecting + logging.

11. **♾ HRESULTs from component-specific facilities** (outside `winerror.h`) → hex fallback (no symbol).
