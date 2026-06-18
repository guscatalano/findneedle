using System;

namespace FindNeedleUX.Services;

/// <summary>
/// Pushes the user's WPP TMF search path (<see cref="ResultsViewerSettings.TraceFormatSearchPath"/>)
/// into the <c>TRACE_FORMAT_SEARCH_PATH</c> environment variable, which the <c>tracefmt.exe</c> child
/// process FindNeedle spawns inherits. This is how WPP ETLs decode in a normal run without the user
/// having to set the env var by hand. Call <see cref="Apply"/> at startup and on settings change.
/// </summary>
public static class TraceFormatConfig
{
    private const string EnvVar = "TRACE_FORMAT_SEARCH_PATH";

    // The ambient value present at launch (e.g. set by the dev environment). Captured once so that
    // clearing the setting restores it rather than wiping it.
    private static readonly string _original = Environment.GetEnvironmentVariable(EnvVar) ?? "";

    public static void Apply()
    {
        var configured = (ResultsViewerSettings.TraceFormatSearchPath ?? "").Trim();
        string combined;
        if (string.IsNullOrEmpty(configured))
            combined = _original;                                   // nothing set → restore ambient
        else if (string.IsNullOrEmpty(_original))
            combined = configured;
        else
            combined = configured + ";" + _original;               // user path first, keep ambient

        Environment.SetEnvironmentVariable(EnvVar, combined);
    }
}
