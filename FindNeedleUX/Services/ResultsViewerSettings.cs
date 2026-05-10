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
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FindNeedle", "viewer-settings.json");

    private static SettingsData _data;
    private static SettingsData Data => _data ??= Load();

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

    public const int DefaultPageSize = 100;
    public static int PageSize
    {
        get => Data.PageSize is int n && n > 0 ? n : DefaultPageSize;
        set { Data.PageSize = value > 0 ? value : DefaultPageSize; Save(); /* no Changed: applied per-window */ }
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
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
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
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath,
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
    }
}
