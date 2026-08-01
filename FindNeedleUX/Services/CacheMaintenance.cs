using System;
using FindNeedleCoreUtils;
using FindPluginCore.Implementations.Storage;

namespace FindNeedleUX.Services;

/// <summary>
/// Startup cache-cleanup policy + the actual clear, shared by the App startup path and the in-window prompt.
/// The reopen-cache (cached searches) and <see cref="CachedStorage"/> point at the SAME directory, so we
/// measure size with <see cref="CachedStorage.GetCacheStats"/> and clear with
/// <see cref="CachedSearchCatalog.DeleteAll"/> — the two are guaranteed consistent.
/// </summary>
public static class CacheMaintenance
{
    /// <summary>Only prompt / auto-clean once the cache is at least this big. Matches the cache's own size
    /// cap (10 GB) so we never nag about a modest cache.</summary>
    public const long ThresholdBytes = CachedStorage.DefaultMaxCacheBytes;

    public static (int files, long bytes) GetStats() => CachedStorage.GetCacheStats();

    /// <summary>Clear every cached search (same as the Cached Searches page's "Clear all"). Returns how many
    /// were removed and the cache size just before.</summary>
    public static (int cleared, long bytesBefore) ClearAllCachedSearches()
    {
        var (_, before) = CachedStorage.GetCacheStats();
        int cleared = CachedSearchCatalog.DeleteAll();
        return (cleared, before);
    }

    /// <summary>Human-readable size, GB when ≥1 GB else MB.</summary>
    public static string FormatBytes(long bytes)
    {
        double gb = bytes / (1024.0 * 1024 * 1024);
        if (gb >= 1) return $"{gb:0.0} GB";
        return $"{bytes / (1024.0 * 1024):0} MB";
    }
}
