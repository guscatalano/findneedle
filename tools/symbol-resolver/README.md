# Symbol-resolver plugin — reference + one-shot setup

Make FindNeedle resolve WPP symbols from *your* source automatically, with nothing to click. Two pieces:

1. **A reference resolver plugin** — `Plugins/SymbolResolver/SmbSymbolResolverPlugin` — implements
   `ISymbolResolver` (+ `IPluginDescription`). When FindNeedle's built-in lookup can't find a binary's PDB,
   it's handed the PDB identity (name + GUID + age + the ready-made symbol-store `Key`) and searches the
   symbol shares listed in `FINDNEEDLE_SYMBOL_SHARES` using the standard symbol-server layout
   (`<root>\<pdb>\<GUID+age>\<pdb>`). Fork it for anything else — a flat share, a REST/symbol-server call,
   credentials, a local cache copy, a build-drop scheme.

2. **`install-symbol-resolver.ps1`** — a per-user script (no admin) that wires it in:
   - optionally `winget install <FindNeedle package>` (`-WingetId`),
   - copies the resolver DLL next to `FindNeedleUX.exe` (so its `FindNeedlePluginLib` dependency resolves),
   - registers it under `HKCU\Software\FindNeedle\Plugins` (a `;`-separated DLL-path list FindNeedle merges
     with its built-in plugins at startup — the shipped `PluginConfig.json` has this enabled),
   - sets `FINDNEEDLE_SYMBOL_SHARES` (user scope) for the bundled SMB resolver.

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
