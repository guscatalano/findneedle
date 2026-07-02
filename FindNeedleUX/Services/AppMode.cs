using System;
using System.Diagnostics;

namespace FindNeedleUX.Services;

/// <summary>
/// Developer/QA mode gate. A few affordances (the "Preview first-run (new user)…" launcher, the rule
/// "test file path" box) are internal tools that only add noise for a real user, so they're hidden
/// unless this is on. On when the env var <c>FINDNEEDLE_DEV=1</c> is set or a debugger is attached.
/// </summary>
public static class AppMode
{
    public static bool IsDeveloper
    {
        get
        {
            try
            {
                if (string.Equals(Environment.GetEnvironmentVariable("FINDNEEDLE_DEV"), "1", StringComparison.Ordinal))
                    return true;
            }
            catch { /* env access denied — fall through */ }
            return Debugger.IsAttached;
        }
    }
}
