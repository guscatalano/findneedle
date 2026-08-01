using System;
using System.Collections.Generic;
using System.Text;

namespace FindNeedleUX.Services.WppSymbols;

/// <summary>One probe target inside a symbol-path chain: a local directory or an HTTP(S) server.</summary>
public sealed record SymbolStore(string Location, bool IsHttp);

/// <summary>
/// Parses the user-supplied symbol path (same syntax _NT_SYMBOL_PATH uses) into ordered probe
/// chains. Elements are ';'-separated:
///   • a plain directory — one-element chain (probed as loose folder AND store layout);
///   • <c>srv*A*B*C</c> — a chain probed left→right; when a later element hits, the PDB is
///     backfilled into the first local element (symsrv's cache write-through);
///   • <c>symsrv*symsrv.dll*A*B</c> — same as srv (the dll component is skipped);
///   • <c>cache*dir</c> — treated as a one-element local chain (simplified: it is probed and used
///     as a backfill target within its own chain only, not globally).
/// A bare <c>srv*</c> (no server) is rejected — there are no hardcoded default servers.
/// </summary>
public static class SymbolPathParser
{
    public static List<List<SymbolStore>> Parse(string symbolPath, StringBuilder log = null)
    {
        var chains = new List<List<SymbolStore>>();
        if (string.IsNullOrWhiteSpace(symbolPath)) return chains;

        foreach (var rawElement in symbolPath.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var element = rawElement.Trim();
            if (element.Length == 0) continue;

            List<string> parts;
            if (element.StartsWith("srv*", StringComparison.OrdinalIgnoreCase))
            {
                parts = SplitStars(element.Substring(4));
            }
            else if (element.StartsWith("symsrv*", StringComparison.OrdinalIgnoreCase))
            {
                parts = SplitStars(element.Substring(7));
                if (parts.Count > 0 && parts[0].EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    parts.RemoveAt(0); // "symsrv.dll" component — irrelevant to managed probing
            }
            else if (element.StartsWith("cache*", StringComparison.OrdinalIgnoreCase))
            {
                parts = SplitStars(element.Substring(6));
            }
            else
            {
                parts = new List<string> { element };
            }

            var chain = new List<SymbolStore>();
            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part)) continue;
                bool isHttp = part.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                           || part.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                chain.Add(new SymbolStore(part.Trim(), isHttp));
            }

            if (chain.Count == 0)
            {
                log?.AppendLine($"  symbol path element ignored (no target, and no default servers are assumed): \"{element}\"");
                continue;
            }
            chains.Add(chain);
        }
        return chains;
    }

    private static List<string> SplitStars(string s)
        => new(s.Split('*', StringSplitOptions.RemoveEmptyEntries));
}
