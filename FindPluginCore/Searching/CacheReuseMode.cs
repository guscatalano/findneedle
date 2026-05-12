using System;

namespace FindPluginCore.Searching;

/// <summary>
/// Controls whether <see cref="NuSearchQuery"/> reuses an existing cached SQLite DB when the
/// source file's size + last-write-time still match what was recorded last run.
/// </summary>
public enum CacheReuseMode
{
    /// <summary>Reuse the cache without asking, whenever the validation check passes.</summary>
    Always,

    /// <summary>Never reuse — every search starts with a freshly wiped storage.</summary>
    Never,

    /// <summary>
    /// Validate the cache silently, but pop a prompt to the user before reusing it. If the
    /// user declines, the cache is wiped and the search runs fresh.
    /// </summary>
    Prompt,
}

/// <summary>
/// Information passed to the user-facing "use cached results?" prompt. The UX layer renders
/// this in a dialog and returns the user's choice.
/// </summary>
public sealed class CacheReusePromptInfo
{
    public string SourceFilePath { get; init; } = "";
    public long SourceFileSize { get; init; }
    public DateTime SourceFileMtimeUtc { get; init; }
    public int CachedRowCount { get; init; }
    public DateTime? CacheCompletedAtUtc { get; init; }
}
