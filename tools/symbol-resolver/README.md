# Symbol-resolver plugin — reference + one-shot setup

Make FindNeedle resolve WPP symbols from *your* source automatically, with nothing to click. Two pieces:

1. **A reference resolver plugin** — `Plugins/SymbolResolver/SmbSymbolResolverPlugin` — implements
   `ISymbolResolver` (+ `IPluginDescription`). When FindNeedle's built-in lookup can't find a binary's PDB,
   it's handed the PDB identity (name + GUID + age + the ready-made symbol-store `Key`) and searches the
   symbol shares listed in `FINDNEEDLE_SYMBOL_SHARES` using the standard symbol-server layout
   (`<root>\<pdb>\<GUID+age>\<pdb>`). Fork it for anything else — a flat share, a REST/symbol-server call,
   credentials, a local cache copy, a build-drop scheme.

   There are **two resolver kinds**, for the two situations a WPP capture puts you in:

   **Binary-driven — `ISymbolResolver`** (there ARE binaries alongside the trace): FindNeedle reads each
   binary's PDB identity (name + GUID + age) and asks the resolver to find that PDB; it extracts the TMFs.
   Two reference implementations show the two shapes this takes:
   - **`SmbSymbolResolver`** (`FINDNEEDLE_SYMBOL_SHARES`) — *stateless*. It probes UNC/SMB share paths with
     `File.Exists`; the SMB redirector owns the connection, so there's nothing to cache or dispose.
   - **`HttpSymbolServerResolver`** (`FINDNEEDLE_SYMBOL_SERVERS`) — *stateful network*. It downloads PDBs
     from an HTTP(S) symbol store (msdl / corporate symsrv layout) into a local cache. Because it owns real
     network state, it demonstrates what a stateful resolver must get right given the plugin lifetime (a
     fresh instance per resolution pass): a **`static HttpClient`** reused across instances (never one per
     call — that leaks sockets); a **two-layer cache** (filesystem short-circuit + an in-memory
     `Lazy`-in-`ConcurrentDictionary` for single-fetch and negative caching); and a **fail-fast timeout**
     (`FINDNEEDLE_SYMBOL_HTTP_TIMEOUT_MS`, default 15s) so one dead server can't hang the decode — it falls
     through to the next.

   **ETL-only — `IWppTmfResolver`** (there are NO binaries — just `.etl` files): with no binary there's no
   PDB identity to look up, so the binary path can't fire. This seam works from the one key a bare ETL
   exposes — the **WPP message/trace GUID** of an event whose TMF is missing (FindNeedle discovers these
   during decode). Given that GUID, the resolver returns a path to a matching `.tmf`; FindNeedle copies it
   into the TMF cache and retries — no PDB, no tracepdb, no WDK.
   - **`TmfStoreResolver`** (`FINDNEEDLE_TMF_STORES`) — finds `<guid>.tmf` in a TMF store, probing both flat
     (`<root>\<guid>.tmf`) and SSQP-style (`<root>\<guid>\<guid>.tmf`) layouts. Point it at your org's TMF
     share and ETL-only captures decode with nothing else configured.

   All three are consulted with the same per-call hang backstop (`FINDNEEDLE_SYMBOL_RESOLVER_TIMEOUT_MS`,
   default 120s) — a resolver that never returns is abandoned so it can't stall the decode. Fork whichever
   is closest to your source; a plugin may implement more than one kind.

2. **`install-symbol-resolver.ps1`** — a per-user script (no admin) that wires it in:
   - optionally `winget install <FindNeedle package>` (`-WingetId`),
   - deploys the resolver DLL to `%LocalAppData%\FindNeedle\plugins\` (a writable folder — see the
     packaging note below),
   - registers its absolute path under `HKCU\Software\FindNeedle\Plugins` (a `;`-separated list FindNeedle
     merges with its built-in plugins at startup — the shipped `PluginConfig.json` has this enabled),
   - sets `FINDNEEDLE_SYMBOL_SHARES` (user scope) for the bundled SMB resolver.

## Packaging note (why registry + a writable folder — verified)

FindNeedle ships as a **packaged (MSIX, full-trust) app**, which changes two things — both confirmed by
registering the app with real package identity and testing:

- **The registry seam works packaged.** A full-trust packaged FindNeedle reads the external
  `HKCU\Software\FindNeedle\Plugins` value and loads the DLL at the absolute path it points to. (With the
  key set the resolver loaded; with it removed it didn't — clean A/B.)
- **A file-based list would NOT work packaged.** The app's `%LocalAppData%\FindNeedle` writes are
  virtualized into `…\Packages\<family>\LocalCache\…`, so a plugin-list file an external script drops in
  the real `%LocalAppData%\FindNeedle` isn't the one the packaged app reads. That's why the extension list
  lives in the **registry**, not a file.
- **The DLL can't go in the install dir.** A packaged app lives under read-only
  `C:\Program Files\WindowsApps\…`. The DLL goes in the writable `%LocalAppData%\FindNeedle\plugins\`
  instead; the app loads it by **absolute path**, which bypasses the known-folder virtualization above.

## Quick start

```powershell
# Build the sample resolver
dotnet build Plugins\SymbolResolver\SmbSymbolResolverPlugin -c Debug

# Install FindNeedle (if you have a winget id) + point the resolver at your symbol share
tools\symbol-resolver\install-symbol-resolver.ps1 -SymbolShares '\\corp\symbols'
# (add -WingetId <id> to winget-install first, or -AppDir <path> if auto-detect misses)
```

Launch FindNeedle → open a WPP `.etl` → symbols resolve from the share with no manual symbol-path setup.
The plugin is only consulted on the build/extract path (never the cheap diagnostic banner), so network
shares are fine.

## Why the registry key (not `PluginConfig.json`)

`PluginConfig.json` ships with the app and lists the built-in I/O plugins. The `HKCU` key is the *user/org*
extension seam — set it with a logon script or deployment tool and every machine picks up your resolver
without touching the install. That's the "install FindNeedle and the resolver is already set up" path.

## Three plugin kinds — locate vs decode (understand the difference)

There are **three** extension points, and they differ along one axis: do you help FindNeedle **find symbols
so it can decode**, or do you **decode the event yourself**? Pick by what you actually have.

| Kind | You have… | You give back… | FindNeedle then… | Needs |
|------|-----------|----------------|------------------|-------|
| **`ISymbolResolver`** | binaries + a PDB source | a **PDB path** | extracts TMFs with tracepdb, decodes | binary present, WDK/tracepdb |
| **`IWppTmfResolver`** | just ETLs + a TMF source | a **`.tmf` path** *or* **TMF text** | caches the TMF, decodes | nothing but the message GUID |
| **`IWppEventDecoder`** | just ETLs + format-in-code | the **decoded string** | uses your string as-is | you parse the raw arg blob |

The decode path tries them in that order: built-in TMF → `IWppTmfResolver` → (per event) `IWppEventDecoder`.
The first two **locate** — they find symbols and let FindNeedle's decoder do the work, so your plugin never
touches TMF wire-format internals. The third **decodes** — it's the last-resort hatch for a provider whose
format lives only in your code. **Prefer a locate plugin whenever you can ship symbols** (a PDB or a TMF);
reach for `IWppEventDecoder` only when you can't, because it makes you own the arg-blob parsing.

**1. `ISymbolResolver` — binaries present, find the PDB.**
```csharp
public sealed class MyResolver : ISymbolResolver, IPluginDescription
{
    public string TryResolvePdb(SymbolLookupRequest r)
    {
        r.Log($"looking for {r.PdbFileName} {r.Key}");   // shows up in the resolution log, attributed to you
        // r.PdbFileName, r.Guid, r.Age, r.Key (symbol-store key), r.BinaryPath
        return /* local/UNC path to the matching PDB, or */ null; // null → pass to the next resolver
    }
    public string GetPluginTextDescription() => "…";
    public string GetPluginFriendlyName()   => "My Resolver";
    public string GetPluginClassName()      => IPluginDescription.GetPluginClassNameBase(this);
}
```

**2. `IWppTmfResolver` — ETL only, find (or generate) the TMF.** Return a `.tmf` **path**, or override
`TryResolveTmfText` to return the TMF **content** directly (for a resolver that builds the format itself):
```csharp
public sealed class MyTmfResolver : IWppTmfResolver, IPluginDescription
{
    public string TryResolveTmf(WppTmfResolveRequest r)
    {
        r.Log($"looking for TMF {r.GuidD}");
        return /* path to a .tmf defining r.MessageGuid, or */ null;
    }
    // optional — return TMF text instead of a file (default returns null → path-only):
    public string TryResolveTmfText(WppTmfResolveRequest r) => null;
    /* …IPluginDescription members… */
}
```

**3. `IWppEventDecoder` — last resort, decode the raw event yourself.** `CanDecode` is asked once per GUID
(cached); `TryDecode` runs per event on the decode thread, so keep it fast and non-blocking:
```csharp
public sealed class MyDecoder : IWppEventDecoder, IPluginDescription
{
    public bool CanDecode(Guid providerGuid) => providerGuid == MyProvider;
    public string TryDecode(WppRawEvent e)
    {
        // e.Data is the RAW arg blob — you parse it per your provider's format (e.PointerSize for pointers).
        e.Log($"decoding msg {e.MessageNumber}");
        return /* formatted message, or */ null; // null → event stays unresolved
    }
    /* …IPluginDescription members… */
}
```

**Logging (all three):** every request carries a `Log` sink — anything you write lands in FindNeedle's
resolution log, attributed to your plugin and in context under the PDB/GUID being resolved. It's never null,
so call it freely; you don't need to know *where* logs go.

**Robustness (all three):** each resolver call is bounded by `FINDNEEDLE_SYMBOL_RESOLVER_TIMEOUT_MS`
(default 120s) — a plugin that hangs is abandoned, never stalling the decode.

Ship the built DLL wherever you like and point the `HKCU\Software\FindNeedle\Plugins` value at it (the
script does this). A single DLL may implement more than one kind.
