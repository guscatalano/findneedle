using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FindNeedlePluginUtils.StructuredLog;

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
    /// When on, timestamps are included in the full-text (substring) search index, so a date/time
    /// fragment typed in the search box matches. Off by default: timestamps are highly uniform, so
    /// trigram-indexing them makes the index build markedly slower for little value — the dedicated
    /// time-range filters cover time either way. Applied to <c>SqliteStorage.IndexLogTimeInFts</c>
    /// (also at startup); takes effect on the next search (a changed value rescans the log's cache).
    /// </summary>
    public const bool DefaultIndexTimestampsInSearch = false;
    public static bool IndexTimestampsInSearch
    {
        get => Data.IndexTimestampsInSearch ?? DefaultIndexTimestampsInSearch;
        set
        {
            Data.IndexTimestampsInSearch = value;
            // Apply live so the next search uses it without an app restart.
            FindPluginCore.Implementations.Storage.SqliteStorage.IndexLogTimeInFts = value;
            Save();
        }
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

    /// <summary>When true (default), navigating to another page (Next/Prev/First/Last/page-size change)
    /// scrolls the result grid back to the top of the new page rather than keeping the previous page's
    /// scroll offset. Some users prefer the offset retained, so it's toggleable.</summary>
    public const bool DefaultScrollToTopOnPageChange = true;
    public static bool ScrollToTopOnPageChange
    {
        get => Data.ScrollToTopOnPageChange ?? DefaultScrollToTopOnPageChange;
        set { Data.ScrollToTopOnPageChange = value; Save(); /* read live by the viewer; no Changed needed */ }
    }

    /// <summary>When a row is tagged (right-click → Tag), also tint the whole row with the tag
    /// color (in addition to the tag glyph in the row header). Off by default.</summary>
    public const bool DefaultColorTaggedRows = false;
    public static bool ColorTaggedRows
    {
        get => Data.ColorTaggedRows ?? DefaultColorTaggedRows;
        set { Data.ColorTaggedRows = value; Save(); Changed?.Invoke(); }
    }

    /// <summary>Whether the welcome page shows the "New here?" intro/getting-started banner. On by
    /// default; the banner's close button turns it off, a Preferences toggle turns it back on.</summary>
    public const bool DefaultShowWelcomeIntro = true;
    public static bool ShowWelcomeIntro
    {
        get => Data.ShowWelcomeIntro ?? DefaultShowWelcomeIntro;
        set { Data.ShowWelcomeIntro = value; Save(); Changed?.Invoke(); }
    }

    /// <summary>Whether the main-window status bar is shown. On by default; the bar's × turns it off,
    /// a Preferences toggle turns it back on. Broadcasts Changed so the window updates live.</summary>
    public const bool DefaultShowStatusBar = true;
    public static bool ShowStatusBar
    {
        get => Data.ShowStatusBar ?? DefaultShowStatusBar;
        set { Data.ShowStatusBar = value; Save(); Changed?.Invoke(); }
    }

    /// <summary>
    /// Whether the in-app MCP server is enabled. When on, the app hosts a localhost-only HTTP MCP
    /// endpoint that lets an agent read and drive the live result viewer. Off by default (it exposes
    /// loaded logs + lets an agent act in the app). Broadcasts Changed so the host starts/stops live.
    /// </summary>
    public const bool DefaultMcpServerEnabled = false;
    public static bool McpServerEnabled
    {
        get => Data.McpServerEnabled ?? DefaultMcpServerEnabled;
        set { Data.McpServerEnabled = value; Save(); Changed?.Invoke(); }
    }

    /// <summary>
    /// Whether RuleDSL "extract" enrichment runs during the scan (writing extracted fields like
    /// ProcessId/ThreadId/Provider into the stored columns). Off by default — the per-row regex is a
    /// scan-time cost, so it's opt-in. Enriched scans use a separate cache db, so toggling doesn't force
    /// a rescan once both are warm.
    /// </summary>
    public const bool DefaultEnrichmentEnabled = false;
    public static bool EnrichmentEnabled
    {
        get => Data.EnrichmentEnabled ?? DefaultEnrichmentEnabled;
        set { Data.EnrichmentEnabled = value; Save(); Changed?.Invoke(); }
    }

    /// <summary>Whether the "Open with → Find Needle" Explorer entry is registered (per-user). Off by
    /// default. Toggling registers/unregisters HKCU classes via <see cref="FileIntegration"/>. No Changed
    /// broadcast — it acts on the OS, not the viewer.</summary>
    public const bool DefaultFileOpenWithEnabled = false;
    public static bool FileOpenWithEnabled
    {
        get => Data.FileOpenWithEnabled ?? DefaultFileOpenWithEnabled;
        set { Data.FileOpenWithEnabled = value; Save(); }
    }

    /// <summary>Whether the right-click "Open in Find Needle" context-menu verb is registered (per-user).
    /// Off by default.</summary>
    public const bool DefaultFileContextMenuEnabled = false;
    public static bool FileContextMenuEnabled
    {
        get => Data.FileContextMenuEnabled ?? DefaultFileContextMenuEnabled;
        set { Data.FileContextMenuEnabled = value; Save(); }
    }

    /// <summary>What happens when a file/folder is dropped onto the viewer while a workspace is already
    /// loaded: Prompt (ask each time, default), ClearAndAdd, or AddToExisting. Applied per-drop (no Changed).</summary>
    public const DragDropMode DefaultDragDropMode = DragDropMode.Prompt;
    public static DragDropMode DragDropMode
    {
        get => !string.IsNullOrEmpty(Data.DragDropMode)
               && Enum.TryParse<DragDropMode>(Data.DragDropMode, ignoreCase: true, out var p)
            ? p : DefaultDragDropMode;
        set { Data.DragDropMode = value.ToString(); Save(); }
    }

    /// <summary>TCP port for the in-app MCP server (bound to 127.0.0.1 only).</summary>
    public const int DefaultMcpServerPort = 8765;
    public static int McpServerPort
    {
        get => Data.McpServerPort is int n && n > 0 && n <= 65535 ? n : DefaultMcpServerPort;
        set { Data.McpServerPort = value > 0 && value <= 65535 ? value : DefaultMcpServerPort; Save(); Changed?.Invoke(); }
    }

    /// <summary>
    /// Folder(s) where tracefmt looks for WPP .tmf files (Trace Message Format, extracted from a
    /// binary's PDB). Without this, WPP ETLs decode as "Unknown". Applied to the
    /// <c>TRACE_FORMAT_SEARCH_PATH</c> env var, which the tracefmt child process inherits. Multiple
    /// paths separated by ';'.
    /// </summary>
    public static string TraceFormatSearchPath
    {
        get => Data.TraceFormatSearchPath ?? "";
        set { Data.TraceFormatSearchPath = value ?? ""; Save(); Changed?.Invoke(); }
    }

    /// <summary>
    /// Symbol path in <c>_NT_SYMBOL_PATH</c> syntax — local PDB folders and/or symbol servers
    /// (<c>srv*cache*https://…</c>). Used by tracepdb (<c>-r</c>) to resolve PDBs for binaries and
    /// extract their TMFs, and exported as <c>_NT_SYMBOL_PATH</c>.
    /// </summary>
    public static string SymbolPath
    {
        get => Data.SymbolPath ?? "";
        set { Data.SymbolPath = value ?? ""; Save(); Changed?.Invoke(); }
    }

    /// <summary>
    /// Folder(s) (';'-separated) holding PDBs and/or binaries to extract WPP TMFs from (via the
    /// "Build TMFs from symbols" action).
    /// </summary>
    public static string SymbolSourcePath
    {
        get => Data.SymbolSourcePath ?? "";
        set { Data.SymbolSourcePath = value ?? ""; Save(); Changed?.Invoke(); }
    }

    /// <summary>Show the completed-steps checklist above the current step in the search progress
    /// spinner. On by default.</summary>
    public const bool DefaultShowStepHistory = true;
    public static bool ShowStepHistory
    {
        get => Data.ShowStepHistory ?? DefaultShowStepHistory;
        set { Data.ShowStepHistory = value; Save(); }
    }

    /// <summary>Open logs progressively — show the viewer as soon as the first page is ready and keep
    /// filling rows in the background (with a "still loading" banner) instead of waiting for the full scan.</summary>
    public const bool DefaultStreamWhileLoading = true;
    public static bool StreamWhileLoading
    {
        get => Data.StreamWhileLoading ?? DefaultStreamWhileLoading;
        set { Data.StreamWhileLoading = value; Save(); }
    }

    /// <summary>Loading overlay style: "Robot" (animated step-aware GIF) or "Spinner" (classic ring).</summary>
    public const string DefaultLoadingAnimation = "Robot";
    public static string LoadingAnimation
    {
        get => string.IsNullOrEmpty(Data.LoadingAnimation) ? DefaultLoadingAnimation : Data.LoadingAnimation;
        set { Data.LoadingAnimation = value; Save(); }
    }

    /// <summary>Robot loader uses the wide (landscape) art when true; the square 256 art when false.</summary>
    public const bool DefaultRobotWide = true;
    public static bool RobotWide
    {
        get => Data.RobotWide ?? DefaultRobotWide;
        set { Data.RobotWide = value; Save(); }
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
            { "Level",    true  },
            { "ProcessId",  false }, // correlation/detail columns hidden by default (opt-in via Columns ▾)
            { "ProcessName", false },
            { "ThreadId",   false },
            { "ActivityId", false },
            { "EventId",           false },
            { "OpCode",            false },
            { "Keywords",          false },
            { "RelatedActivityId", false },
            { "Channel",           false },
            { "ProviderGuid",      false },
            { "RecordId",          false },
            { "Raw Row",           false }  // the unformatted original row (CSV values / raw log line)
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
        // No Changed broadcast: column visibility is applied directly (viewer's ApplyColumnVisibility,
        // or re-applied when the viewer re-opens). Firing Changed would run OnSettingsChanged →
        // ApplyTheme, which momentarily resets level colors — and no subscriber acts on column state.
    }

    public static void ClearColumnVisibility()
    {
        Data.ColumnVisibility = null;
        Save();
        // See SetColumnVisibility: no Changed broadcast (avoids the spurious theme/level-color reset).
    }

    /// <summary>Canonical ordered set of fields the row-details panel can show.</summary>
    public static readonly IReadOnlyList<string> DetailFieldNames = new[]
    {
        "Index", "Time", "Provider", "TaskName", "Message", "Source", "Level",
        "MachineName", "Username", "OpCode", "ProcessId", "ProcessName", "ThreadId", "ActivityId",
        "EventId", "Keywords", "RelatedActivityId", "Channel", "ProviderGuid", "RecordId", "Tag",
    };

    /// <summary>Per-field visibility for the row-details panel (all shown by default). Merged with the
    /// defaults so a field the user hasn't touched shows.</summary>
    public static IReadOnlyDictionary<string, bool> DetailFieldVisibility
    {
        get
        {
            var merged = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in DetailFieldNames) merged[f] = true;
            if (Data.DetailFieldVisibility != null)
                foreach (var kv in Data.DetailFieldVisibility) merged[kv.Key] = kv.Value;
            return merged;
        }
    }

    public static void SetDetailFieldVisibility(string field, bool visible)
    {
        if (string.IsNullOrEmpty(field)) return;
        Data.DetailFieldVisibility ??= new Dictionary<string, bool>();
        Data.DetailFieldVisibility[field] = visible;
        Save();
        Changed?.Invoke();
    }

    public static string ThemeName
    {
        get => string.IsNullOrEmpty(Data.ThemeName) ? DefaultThemeName : Data.ThemeName;
        set { Data.ThemeName = value; Save(); Changed?.Invoke(); }
    }

    /// <summary>How the window title bar is colored: "System" (default), "Accent" (Windows accent), or
    /// "Custom" (<see cref="TitleBarCustomColor"/>). Broadcasts Changed so the window re-applies live.</summary>
    public const string DefaultTitleBarColorMode = "System";
    public static string TitleBarColorMode
    {
        get => string.IsNullOrEmpty(Data.TitleBarColorMode) ? DefaultTitleBarColorMode : Data.TitleBarColorMode;
        set { Data.TitleBarColorMode = value; Save(); Changed?.Invoke(); }
    }

    /// <summary>Custom title-bar color ("#RRGGBB" / "#AARRGGBB"), used when <see cref="TitleBarColorMode"/>
    /// is "Custom".</summary>
    public const string DefaultTitleBarCustomColor = "#2D7D9A";
    public static string TitleBarCustomColor
    {
        get => string.IsNullOrEmpty(Data.TitleBarCustomColor) ? DefaultTitleBarCustomColor : Data.TitleBarCustomColor;
        set { Data.TitleBarCustomColor = value; Save(); Changed?.Invoke(); }
    }

    /// <summary>
    /// Thickness (in px) of the result grid's scrollbars. WinUI's default is a thin auto-hiding bar
    /// (~12px); bumping this makes the scrollbar easier to see and grab. Clamped to a sane range so a
    /// corrupt value can't make the bar invisible or eat the grid. Applied as a <c>ScrollBarSize</c>
    /// resource override when the viewer page is built. Broadcasts Changed.
    /// </summary>
    public const double DefaultScrollBarSize = 12;
    public const double MinScrollBarSize = 8;
    public const double MaxScrollBarSize = 32;
    public static double ClampScrollBarSize(double v) =>
        v < MinScrollBarSize ? MinScrollBarSize : v > MaxScrollBarSize ? MaxScrollBarSize : v;
    public static double ScrollBarSize
    {
        get => ClampScrollBarSize(Data.ScrollBarSize ?? DefaultScrollBarSize);
        set { Data.ScrollBarSize = ClampScrollBarSize(value); Save(); Changed?.Invoke(); }
    }

    /// <summary>
    /// Font size (in px) of the result grid's row text. WinUI's default body size is ~14; this lets
    /// the user shrink it to fit more rows or grow it for readability. Clamped to a sane range so a
    /// corrupt value can't make text invisible or blow out the row height. The viewer scales the row
    /// height to match. Broadcasts Changed so an open viewer reflows live.
    /// </summary>
    public const double DefaultRowFontSize = 12;
    public const double MinRowFontSize = 9;
    public const double MaxRowFontSize = 24;
    public static double ClampRowFontSize(double v) =>
        v < MinRowFontSize ? MinRowFontSize : v > MaxRowFontSize ? MaxRowFontSize : v;
    public static double RowFontSize
    {
        get => ClampRowFontSize(Data.RowFontSize ?? DefaultRowFontSize);
        set { Data.RowFontSize = ClampRowFontSize(value); Save(); Changed?.Invoke(); }
    }

    /// <summary>
    /// Row height as a multiple of the row text size (the viewer sets RowHeight = ceil(fontSize ×
    /// this), floored at the default). Lets the user pick how tight or roomy rows feel. 1.9 is the
    /// default (comfortable line height); smaller packs more rows on screen, larger gives more
    /// breathing room. Clamped so a corrupt value can't collapse or balloon the rows.
    /// </summary>
    public const double DefaultRowHeightRatio = 1.9;
    public const double MinRowHeightRatio = 1.2;
    public const double MaxRowHeightRatio = 3.5;
    public static double ClampRowHeightRatio(double v) =>
        v < MinRowHeightRatio ? MinRowHeightRatio : v > MaxRowHeightRatio ? MaxRowHeightRatio : v;
    public static double RowHeightRatio
    {
        get => ClampRowHeightRatio(Data.RowHeightRatio ?? DefaultRowHeightRatio);
        set { Data.RowHeightRatio = ClampRowHeightRatio(value); Save(); Changed?.Invoke(); }
    }

    /// <summary>
    /// How a structured event's payload (e.g. an ETW TraceLogging event's fields) is rendered into the
    /// message column. The payload is kept as JSON in StructuredData, so this is re-applied at display
    /// time — switching is instant, no re-scan. Broadcasts Changed.
    /// </summary>
    public const PayloadFormat DefaultEtwPayloadFormat = PayloadFormat.KeyValueQuoted;
    public static PayloadFormat EtwPayloadFormat
    {
        get => !string.IsNullOrEmpty(Data.EtwPayloadFormat)
               && Enum.TryParse<PayloadFormat>(Data.EtwPayloadFormat, ignoreCase: true, out var p)
            ? p : DefaultEtwPayloadFormat;
        set { Data.EtwPayloadFormat = value.ToString(); Save(); Changed?.Invoke(); }
    }

    /// <summary>Per-field template for <see cref="PayloadFormat.Custom"/> (tokens {name} / {value}).</summary>
    public static string EtwPayloadCustomTemplate
    {
        get => string.IsNullOrEmpty(Data.EtwPayloadCustomTemplate)
            ? StructuredPayloadFormatter.DefaultCustomTemplate : Data.EtwPayloadCustomTemplate;
        set { Data.EtwPayloadCustomTemplate = value ?? ""; Save(); Changed?.Invoke(); }
    }

    /// <summary>
    /// How the viewer sorts rows when a log first opens (before any column header is clicked).
    /// LoadOrder (default) = the order rows were read from the source (SQLite Id ASC), NOT by timestamp.
    /// TimeAscending / TimeDescending sort by the Time column on open. The user can always re-sort by
    /// clicking a header; this only sets the initial sort. Applied on each fresh search/open.
    /// </summary>
    public const DefaultSortMode DefaultDefaultSort = DefaultSortMode.LoadOrder;
    public static DefaultSortMode DefaultSort
    {
        get => !string.IsNullOrEmpty(Data.DefaultSort)
               && Enum.TryParse<DefaultSortMode>(Data.DefaultSort, ignoreCase: true, out var p)
            ? p : DefaultDefaultSort;
        set { Data.DefaultSort = value.ToString(); Save(); /* applies on next open */ }
    }

    /// <summary>
    /// Width (px) of the left-docked filter pane. User-resizable via the splitter between the pane and
    /// the results. Clamped so a corrupt value can't make it vanish or eat the grid. Only applies when
    /// the filter dock is Left.
    /// </summary>
    /// <summary>Toolbar buttons the user can show/hide (stable ids). The search box and the More (⋯)
    /// menu are always shown and not in this list.</summary>
    public static readonly IReadOnlyList<string> ToolbarButtonIds =
        new[] { "SearchHelp", "Columns", "Export", "Sources", "Filters", "View", "FilterPerf", "Status" };

    /// <summary>Per-button toolbar visibility (all shown by default), merged with the defaults.</summary>
    public static IReadOnlyDictionary<string, bool> ToolbarButtonVisibility
    {
        get
        {
            var merged = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ToolbarButtonIds) merged[id] = true;
            if (Data.ToolbarButtons != null)
                foreach (var kv in Data.ToolbarButtons) merged[kv.Key] = kv.Value;
            return merged;
        }
    }

    public static void SetToolbarButtonVisibility(string id, bool visible)
    {
        if (string.IsNullOrEmpty(id)) return;
        Data.ToolbarButtons ??= new Dictionary<string, bool>();
        Data.ToolbarButtons[id] = visible;
        Save();
        // No Changed broadcast: this is per-window UI state. The viewer applies it directly via
        // ApplyToolbarVisibility(). Firing Changed would run the full OnSettingsChanged → ApplyTheme,
        // which momentarily resets level colors to the theme defaults (looked like a color "bug").
    }

    public const double DefaultFilterPaneWidth = 300;
    public const double MinFilterPaneWidth = 220;
    public const double MaxFilterPaneWidth = 760;
    public static double ClampFilterPaneWidth(double v) =>
        v < MinFilterPaneWidth ? MinFilterPaneWidth : v > MaxFilterPaneWidth ? MaxFilterPaneWidth : v;
    public static double FilterPaneWidth
    {
        get => ClampFilterPaneWidth(Data.FilterPaneWidth ?? DefaultFilterPaneWidth);
        set { Data.FilterPaneWidth = ClampFilterPaneWidth(value); Save(); /* applied per-window */ }
    }

    /// <summary>
    /// Whether the result viewer's per-column filter row shows "known value" dropdowns (populated from
    /// the loaded set's distinct Provider / TaskName / Source values) instead of free-text boxes, so the
    /// user can pick from what's actually present rather than guessing. Per-window UI state.
    /// </summary>
    public const bool DefaultShowKnownValues = false;
    public static bool ShowKnownValues
    {
        get => Data.ShowKnownValues ?? DefaultShowKnownValues;
        set { Data.ShowKnownValues = value; Save(); /* no Changed: applied per-window */ }
    }

    /// <summary>
    /// Per-field select mode for the "show known" dropdowns. Only Provider / TaskName / Source support
    /// known-value dropdowns. Defaults to Multi (match any of several values, OR-set) — like
    /// SimpleEventViewer, where picking a few providers/sources at once is the common case. Users can
    /// still switch a field to Single on the settings page.
    /// </summary>
    public const KnownFilterMode DefaultKnownFilterMode = KnownFilterMode.Multi;
    public static KnownFilterMode GetKnownFilterMode(string field)
    {
        if (!string.IsNullOrEmpty(field) && Data.KnownFilterModes != null
            && Data.KnownFilterModes.TryGetValue(field, out var s)
            && Enum.TryParse<KnownFilterMode>(s, ignoreCase: true, out var m))
            return m;
        return DefaultKnownFilterMode;
    }
    public static void SetKnownFilterMode(string field, KnownFilterMode mode)
    {
        if (string.IsNullOrEmpty(field)) return;
        Data.KnownFilterModes ??= new Dictionary<string, string>();
        Data.KnownFilterModes[field] = mode.ToString();
        Save();
        Changed?.Invoke();
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
        public string TitleBarColorMode { get; set; }
        public string TitleBarCustomColor { get; set; }
        public Dictionary<string, string> LevelColors { get; set; }
        public bool? FiltersExpanded { get; set; }
        public Dictionary<string, bool> ColumnVisibility { get; set; }
        public int? PageSize { get; set; }
        public string DefaultResultViewer { get; set; }
        public bool? UseSearchCache { get; set; } // legacy; superseded by CacheReuseMode
        public string CacheReuseMode { get; set; }
        public string IndexingMode { get; set; }
        public bool? IndexTimestampsInSearch { get; set; }
        public string SearchSubmitMode { get; set; }
        public string FilterDock { get; set; }
        public bool? ShowStepHistory { get; set; }
        public bool? StreamWhileLoading { get; set; }
        public string LoadingAnimation { get; set; }
        public bool? RobotWide { get; set; }
        public bool? ColorTaggedRows { get; set; }
        public bool? ScrollToTopOnPageChange { get; set; }
        public bool? ShowWelcomeIntro { get; set; }
        public bool? ShowStatusBar { get; set; }
        public bool? McpServerEnabled { get; set; }
        public int? McpServerPort { get; set; }
        public bool? FileOpenWithEnabled { get; set; }
        public bool? FileContextMenuEnabled { get; set; }
        public string DragDropMode { get; set; }
        public bool? EnrichmentEnabled { get; set; }
        public string TraceFormatSearchPath { get; set; }
        public string SymbolPath { get; set; }
        public string SymbolSourcePath { get; set; }
        public bool? DetailsPanelVisible { get; set; }
        public string DetailsMode { get; set; }
        public double? DetailsPanelHeight { get; set; }
        public double? FilterPaneWidth { get; set; }
        public Dictionary<string, bool> ToolbarButtons { get; set; }
        public double? ScrollBarSize { get; set; }
        public double? RowFontSize { get; set; }
        public double? RowHeightRatio { get; set; }
        public string EtwPayloadFormat { get; set; }
        public string EtwPayloadCustomTemplate { get; set; }
        public string DefaultSort { get; set; }
        public bool? ShowKnownValues { get; set; }
        public Dictionary<string, string> KnownFilterModes { get; set; }
        public Dictionary<string, bool> DetailFieldVisibility { get; set; }
    }
}
