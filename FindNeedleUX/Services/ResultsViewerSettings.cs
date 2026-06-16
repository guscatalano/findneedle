using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FindNeedleUX.Services;

/// <summary>
/// Persisted preferences for the result viewer (time format + per-level colors + theme name).
/// Backed by a JSON file at %LocalAppData%\FindNeedle\viewer-settings.json so it works for both
/// packaged (MSIX) and unpackaged execution. Loaded lazily on first access; saved on every set.
/// </summary>
public static class ResultsViewerSettings
{
    private static readonly string DefaultSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FindNeedle", "viewer-settings.json");

    // Mutable so tests can redirect persistence to a temp file via the seam below; production
    // always uses DefaultSettingsPath.
    private static string _settingsPath = DefaultSettingsPath;

    private static SettingsData _data;
    private static SettingsData Data => _data ??= Load();

    // --- Test seam (only the unit-test assembly sees these, via InternalsVisibleTo) ---
    // Point persistence at a custom path and drop the cache so the next access reloads from there.
    internal static void SetStorageLocationForTests(string path) { _settingsPath = path; _data = null; }
    // Drop the in-memory cache so the next access reloads from disk (round-trip tests).
    internal static void ReloadFromDiskForTests() => _data = null;
    // Restore the production path + clear cache (call in a test's finally so nothing leaks).
    internal static void ResetStorageForTests() { _settingsPath = DefaultSettingsPath; _data = null; }

    /// <summary>Raised after any setter completes, on the same thread that called set.</summary>
    public static event Action Changed;

    public const string DefaultTimeFormat = "yyyy-MM-dd HH:mm:ss";
    public const string DefaultThemeName = "Subtle";

    public static string TimeFormat
    {
        get => string.IsNullOrEmpty(Data.TimeFormat) ? DefaultTimeFormat : Data.TimeFormat;
        set { Data.TimeFormat = value; Save(); Changed?.Invoke(); }
    }

    public static bool FiltersExpanded
    {
        get => Data.FiltersExpanded ?? true;
        set { Data.FiltersExpanded = value; Save(); /* no Changed: per-window UI state */ }
    }

    /// <summary>
    /// When true, the native viewer shows a "Details panel" beneath the DataGrid with the full
    /// field set of the selected row, and disables the inline expand-row details. When false
    /// (default), the viewer uses the in-row expandable details (click a row to expand).
    /// </summary>
    public static bool DetailsPanelVisible
    {
        get => Data.DetailsPanelVisible ?? false;
        set { Data.DetailsPanelVisible = value; Save(); /* no Changed: per-window UI state */ }
    }

    /// <summary>
    /// How the viewer shows a row's details: Inrow (click expands), BottomPanel (docked panel), or
    /// Popup (double-click dialog). Migrates from the legacy <see cref="DetailsPanelVisible"/> bool.
    /// Per-window UI state (no Changed broadcast).
    /// </summary>
    public const DetailsMode DefaultDetailsMode = DetailsMode.Inrow;
    public static DetailsMode DetailsMode
    {
        get
        {
            if (!string.IsNullOrEmpty(Data.DetailsMode)
                && Enum.TryParse<DetailsMode>(Data.DetailsMode, ignoreCase: true, out var parsed))
                return parsed;
            if (Data.DetailsPanelVisible == true) return DetailsMode.BottomPanel; // legacy migration
            return DefaultDetailsMode;
        }
        set { Data.DetailsMode = value.ToString(); Save(); /* no Changed: per-window UI state */ }
    }

    /// <summary>
    /// User-chosen height in pixels of the native viewer's details panel. Clamped to the
    /// [<see cref="MinDetailsPanelHeight"/>, <see cref="MaxDetailsPanelHeight"/>] range on read
    /// so a corrupt / out-of-range JSON value can't make the panel disappear or fill the screen.
    /// </summary>
    public const double DefaultDetailsPanelHeight = 240;
    public const double MinDetailsPanelHeight = 80;
    public const double MaxDetailsPanelHeight = 800;

    /// <summary>
    /// Clamp a details-panel height into [<see cref="MinDetailsPanelHeight"/>,
    /// <see cref="MaxDetailsPanelHeight"/>]. Pure (no I/O / no static state) so it's unit-testable;
    /// both the getter and setter route through it.
    /// </summary>
    public static double ClampDetailsPanelHeight(double value)
    {
        if (value < MinDetailsPanelHeight) return MinDetailsPanelHeight;
        if (value > MaxDetailsPanelHeight) return MaxDetailsPanelHeight;
        return value;
    }

    public static double DetailsPanelHeight
    {
        get => ClampDetailsPanelHeight(Data.DetailsPanelHeight ?? DefaultDetailsPanelHeight);
        set { Data.DetailsPanelHeight = ClampDetailsPanelHeight(value); Save(); }
    }

    public const int DefaultPageSize = 100;
    public static int PageSize
    {
        get => Data.PageSize is int n && n > 0 ? n : DefaultPageSize;
        set { Data.PageSize = value > 0 ? value : DefaultPageSize; Save(); /* no Changed: applied per-window */ }
    }

    /// <summary>
    /// Which viewer opens by default. Only the native viewer remains, so this is effectively always
    /// <c>GlobalSettings.NativeResultViewerKey</c>; kept persisted for forward/backward compatibility.
    /// </summary>
    public const string DefaultDefaultResultViewer = FindPluginCore.GlobalConfiguration.GlobalSettings.NativeResultViewerKey;
    public static string DefaultResultViewer
    {
        get => string.IsNullOrEmpty(Data.DefaultResultViewer) ? DefaultDefaultResultViewer : Data.DefaultResultViewer;
        set { Data.DefaultResultViewer = string.IsNullOrEmpty(value) ? DefaultDefaultResultViewer : value.ToLowerInvariant(); Save(); /* no Changed: applies on next viewer open */ }
    }

    /// <summary>
    /// When the on-disk cache for a single-file search is still valid (same size + mtime +
    /// schema version), how should we decide whether to reuse it?
    ///   Always — reuse silently (fastest)
    ///   Never  — always rescan
    ///   Prompt — ask the user via a dialog before reusing (default for new installs)
    /// </summary>
    /// <summary>
    /// When the substring-search (FTS) index is built for SQLite-backed searches:
    ///   Background — right after the viewer opens, off the critical path (default; open stays fast and
    ///                searches become fast on their own, without the user having to trigger a build)
    ///   Lazy       — on first substring search / search-box focus ("just open + scroll" never pays for it)
    ///   Eager      — during the search, before the viewer opens (slower open, instant first search)
    /// </summary>
    public const FindPluginCore.Searching.IndexingMode DefaultIndexingMode = FindPluginCore.Searching.IndexingMode.Background;
    public static FindPluginCore.Searching.IndexingMode IndexingMode
    {
        get
        {
            if (!string.IsNullOrEmpty(Data.IndexingMode)
                && Enum.TryParse<FindPluginCore.Searching.IndexingMode>(Data.IndexingMode, ignoreCase: true, out var parsed))
                return parsed;
            return DefaultIndexingMode;
        }
        set { Data.IndexingMode = value.ToString(); Save(); }
    }

    /// <summary>
    /// How the result viewer's search box submits searches:
    ///   Auto    — live until a search is slow (>~1s) or the log is large, then Enter-to-search (default)
    ///   Live    — search on every keystroke
    ///   OnEnter — search only when Enter is pressed
    /// </summary>
    public const SearchSubmitMode DefaultSearchSubmitMode = SearchSubmitMode.Auto;
    public static SearchSubmitMode SearchSubmitMode
    {
        get
        {
            if (!string.IsNullOrEmpty(Data.SearchSubmitMode)
                && Enum.TryParse<SearchSubmitMode>(Data.SearchSubmitMode, ignoreCase: true, out var parsed))
                return parsed;
            return DefaultSearchSubmitMode;
        }
        set { Data.SearchSubmitMode = value.ToString(); Save(); }
    }

    /// <summary>Where the result viewer's filter pane is docked (Top or Left). Broadcasts Changed so
    /// an open viewer re-lays-out live.</summary>
    public const FilterDock DefaultFilterDock = FilterDock.Top;
    public static FilterDock FilterDock
    {
        get
        {
            if (!string.IsNullOrEmpty(Data.FilterDock)
                && Enum.TryParse<FilterDock>(Data.FilterDock, ignoreCase: true, out var parsed))
                return parsed;
            return DefaultFilterDock;
        }
        set { Data.FilterDock = value.ToString(); Save(); Changed?.Invoke(); }
    }

    /// <summary>Show the completed-steps checklist above the current step in the search progress
    /// spinner. On by default.</summary>
    public const bool DefaultShowStepHistory = true;
    public static bool ShowStepHistory
    {
        get => Data.ShowStepHistory ?? DefaultShowStepHistory;
        set { Data.ShowStepHistory = value; Save(); }
    }

    public const FindPluginCore.Searching.CacheReuseMode DefaultCacheReuseMode = FindPluginCore.Searching.CacheReuseMode.Prompt;
    public static FindPluginCore.Searching.CacheReuseMode CacheReuseMode
    {
        get
        {
            // Prefer the new enum field; fall back to the legacy bool for installs that wrote
            // viewer-settings.json before this setting existed.
            if (!string.IsNullOrEmpty(Data.CacheReuseMode)
                && Enum.TryParse<FindPluginCore.Searching.CacheReuseMode>(Data.CacheReuseMode, ignoreCase: true, out var parsed))
                return parsed;
            // Migration policy: the legacy bool only meant "do I want any caching at all?". If
            // the user had it OFF that's a clear opt-out → preserve as Never. If it was ON (or
            // unset), default into the new Prompt mode so they actually see the choice on the
            // next reopen rather than getting silently locked into Always.
            if (Data.UseSearchCache.HasValue && !Data.UseSearchCache.Value)
                return FindPluginCore.Searching.CacheReuseMode.Never;
            return DefaultCacheReuseMode; // Prompt
        }
        set
        {
            Data.CacheReuseMode = value.ToString();
            // Wipe the legacy bool so the enum field is the source of truth from now on.
            Data.UseSearchCache = null;
            Save();
            // No Changed event: applies on next search.
        }
    }

    /// <summary>
    /// Default visibility of each result-grid column. Out of the box, Source is hidden because
    /// its full-path content is long; users can re-enable any time from the settings page (saved)
    /// or via the in-viewer Columns ▾ popover (session only).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, bool> DefaultColumnVisibility =
        new Dictionary<string, bool>
        {
            { "Index",    true  },
            { "Time",     true  },
            { "Provider", true  },
            { "TaskName", true  },
            { "Message",  true  },
            { "Source",   false }, // hidden by default
            { "Level",    true  }
        };

    /// <summary>
    /// Read-only snapshot of the persisted column-visibility map merged with the defaults
    /// (defaults fill in any column the user hasn't explicitly set).
    /// </summary>
    public static IReadOnlyDictionary<string, bool> ColumnVisibility
    {
        get
        {
            var merged = new Dictionary<string, bool>(DefaultColumnVisibility);
            if (Data.ColumnVisibility != null)
                foreach (var kv in Data.ColumnVisibility) merged[kv.Key] = kv.Value;
            return merged;
        }
    }

    public static void SetColumnVisibility(string column, bool visible)
    {
        if (string.IsNullOrEmpty(column)) return;
        Data.ColumnVisibility ??= new Dictionary<string, bool>();
        Data.ColumnVisibility[column] = visible;
        Save();
        Changed?.Invoke();
    }

    public static void ClearColumnVisibility()
    {
        Data.ColumnVisibility = null;
        Save();
        Changed?.Invoke();
    }

    public static string ThemeName
    {
        get => string.IsNullOrEmpty(Data.ThemeName) ? DefaultThemeName : Data.ThemeName;
        set { Data.ThemeName = value; Save(); Changed?.Invoke(); }
    }

    /// <summary>
    /// Per-level hex color overrides ("#AARRGGBB" / "#RRGGBB" / "Transparent").
    /// Returns a copy; mutate via <see cref="SetLevelColor"/> or <see cref="ClearLevelColors"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, string> LevelColors
        => Data.LevelColors != null
            ? new Dictionary<string, string>(Data.LevelColors)
            : new Dictionary<string, string>();

    public static void SetLevelColor(string level, string hex)
    {
        if (string.IsNullOrEmpty(level)) return;
        Data.LevelColors ??= new Dictionary<string, string>();
        Data.LevelColors[level] = hex;
        Save();
        Changed?.Invoke();
    }

    public static void ClearLevelColors()
    {
        Data.LevelColors = null;
        Save();
        Changed?.Invoke();
    }

    private static SettingsData Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData();
            }
        }
        catch { /* corrupt file => start fresh */ }
        return new SettingsData();
    }

    private static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_settingsPath,
                JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ResultsViewerSettings.Save failed: {ex.Message}");
        }
    }

    private class SettingsData
    {
        public string TimeFormat { get; set; }
        public string ThemeName { get; set; }
        public Dictionary<string, string> LevelColors { get; set; }
        public bool? FiltersExpanded { get; set; }
        public Dictionary<string, bool> ColumnVisibility { get; set; }
        public int? PageSize { get; set; }
        public string DefaultResultViewer { get; set; }
        public bool? UseSearchCache { get; set; } // legacy; superseded by CacheReuseMode
        public string CacheReuseMode { get; set; }
        public string IndexingMode { get; set; }
        public string SearchSubmitMode { get; set; }
        public string FilterDock { get; set; }
        public bool? ShowStepHistory { get; set; }
        public bool? DetailsPanelVisible { get; set; }
        public string DetailsMode { get; set; }
        public double? DetailsPanelHeight { get; set; }
    }
}
