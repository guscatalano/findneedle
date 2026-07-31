# Symbol-resolver plugin — reference + one-shot setup

Make FindNeedle resolve WPP symbols from *your* source automatically, with nothing to click. Two pieces:

1. **A reference resolver plugin** — `Plugins/SymbolResolver/SmbSymbolResolverPlugin` — implements
   `ISymbolResolver` (+ `IPluginDescription`). When FindNeedle's built-in lookup can't find a binary's PDB,
   it's handed the PDB identity (name + GUID + age + the ready-made symbol-store `Key`) and searches the
   symbol shares listed in `FINDNEEDLE_SYMBOL_SHARES` using the standard symbol-server layout
   (`<root>\<pdb>\<GUID+age>\<pdb>`). Fork it for anything else — a flat share, a REST/symbol-server call,
   credentials, a local cache copy, a build-drop scheme.

   There are **two** reference resolvers, on purpose — they show the two shapes a resolver takes:
   - **`SmbSymbolResolver`** (`FINDNEEDLE_SYMBOL_SHARES`) — *stateless*. It probes UNC/SMB share paths with
     `File.Exists`; the SMB redirector owns the connection, so there's nothing to cache or dispose.
   - **`HttpSymbolServerResolver`** (`FINDNEEDLE_SYMBOL_SERVERS`) — *stateful network*. It downloads PDBs
     from an HTTP(S) symbol store (msdl / corporate symsrv layout) into a local cache. Because it owns real
     network state, it demonstrates what a stateful resolver must get right given the plugin lifetime (a
     fresh instance per resolution pass): a **`static HttpClient`** reused across instances (never one per
     call — that leaks sockets); a **two-layer cache** (filesystem short-circuit + an in-memory
     `Lazy`-in-`ConcurrentDictionary` for single-fetch and negative caching); and a **fail-fast timeout**
     (`FINDNEEDLE_SYMBOL_HTTP_TIMEOUT_MS`, default 15s) so one dead server can't hang the decode — it falls
     through to the next. Fork whichever is closer to your source.

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

## Writing your own resolver

```csharp
public sealed class MyResolver : ISymbolResolver, IPluginDescription
{
    public string TryResolvePdb(SymbolLookupRequest r)
    {
        // r.PdbFileName, r.Guid, r.Age, r.Key (symbol-store key), r.BinaryPath
        // return a local/UNC path to the matching PDB, or null to pass.
    }
    public string GetPluginTextDescription() => "…";
    public string GetPluginFriendlyName()   => "My Resolver";
    public string GetPluginClassName()      => IPluginDescription.GetPluginClassNameBase(this);
}
```

Ship the built DLL wherever you like and point the `HKCU\Software\FindNeedle\Plugins` value at it (the
script does this). FindNeedle extracts the WPP TMFs from whatever PDB you return — your plugin never has to
touch tracefmt/TMF internals.
