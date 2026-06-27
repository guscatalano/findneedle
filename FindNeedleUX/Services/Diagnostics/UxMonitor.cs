using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using Microsoft.UI.Dispatching;

namespace FindNeedleUX.Services.Diagnostics;

/// <summary>Snapshot of app state captured when a slow interaction is recorded — the "under what
/// conditions" half of the diagnosis. Populated by a provider the viewer registers (so this service
/// doesn't take a hard dependency on the ViewModel).</summary>
public sealed class UxConditions
{
    public int TotalRows { get; set; }
    public int FilteredRows { get; set; }
    public string StorageTier { get; set; }       // InMemory / Hybrid / SqlLite
    public string FtsState { get; set; }           // building / built / none
    public bool IsStreaming { get; set; }
    public bool IsApplyingFilter { get; set; }
    public int PageSize { get; set; }
    public string SortColumn { get; set; }
    public List<string> ActiveFilters { get; set; } // which filter fields are set
    public string ViewerMode { get; set; }
    public string FilterDock { get; set; }
}

/// <summary>One recorded slow UX interaction.</summary>
public sealed class SlowInteractionRecord
{
    public string Timestamp { get; set; }
    public string Kind { get; set; }          // "input-idle" (UI-thread block) or "scope" (named op)
    public string Interaction { get; set; }   // element id / input kind, or scope name
    public long LatencyMs { get; set; }
    public List<string> ScopeChain { get; set; } // nested UxMonitor.Track names, outermost first
    public string CallSite { get; set; }      // file:line (member) for scopes
    public UxConditions Conditions { get; set; }
    public string Stack { get; set; }         // trimmed managed stack (scopes only)
}

/// <summary>
/// Built-in UX latency recorder. Two complementary mechanisms:
///   • <see cref="NoteInput"/> + a Low-priority dispatcher callback measures how long the UI thread
///     stayed busy after each user input (the perceived "it hung" latency) — catches every
///     interaction automatically with no per-handler wiring.
///   • <see cref="Track"/> wraps a named (possibly async) operation and times its wall-clock so slow
///     *non-blocking* work (e.g. an off-thread filter apply behind a loader) is captured too.
/// Anything over <see cref="ThresholdMs"/> is written to ux-slow.log (JSONL) + a brief perf-log line,
/// and kept in a small in-memory ring for the in-app / MCP surface. Always best-effort: instrumentation
/// must never throw into the path it measures.
/// </summary>
public static class UxMonitor
{
    public static long ThresholdMs { get; set; } = 2500;
    public static bool Enabled { get; set; } = true;

    /// <summary>Viewer registers this so a record can snapshot live state. Null → empty conditions.</summary>
    public static Func<UxConditions> ConditionsProvider { get; set; }

    private static DispatcherQueue _ui;
    private static readonly string _path = ComputePath();
    private static readonly object _ioLock = new();
    private static readonly Queue<SlowInteractionRecord> _recent = new();
    private const int RecentCap = 100;

    // input→idle state (all touched on the UI thread)
    private static int _inFlight;
    private static long _inputStartTicks;
    private static string _inputSource;

    // nested Track() scope names for the current logical activity
    private static readonly AsyncLocal<ImmutableScopeStack> _scopes = new();

    /// <summary>Call once at startup with the UI dispatcher.</summary>
    public static void Configure(DispatcherQueue uiDispatcher) => _ui = uiDispatcher;

    public static IReadOnlyList<SlowInteractionRecord> Recent
    {
        get { lock (_ioLock) return new List<SlowInteractionRecord>(_recent); }
    }

    public static string LogPath => _path;

    /// <summary>Note a user input (pointer/key). Starts (or coalesces into) a UI-thread-busy measurement
    /// that finishes when the dispatcher next drains to Low priority.</summary>
    public static void NoteInput(string source, string kind)
    {
        if (!Enabled || _ui == null) return;
        try
        {
            // First input of a burst arms the measurement; later inputs fold into the same span.
            if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return;
            _inputStartTicks = Environment.TickCount64;
            _inputSource = string.IsNullOrEmpty(source) ? kind : source;
            if (!_ui.TryEnqueue(DispatcherQueuePriority.Low, FinishInput))
                Interlocked.Exchange(ref _inFlight, 0); // couldn't schedule — disarm
        }
        catch { Interlocked.Exchange(ref _inFlight, 0); }
    }

    private static void FinishInput()
    {
        long elapsed = Environment.TickCount64 - _inputStartTicks;
        var src = _inputSource;
        Interlocked.Exchange(ref _inFlight, 0);
        if (elapsed < ThresholdMs) return;
        Record(new SlowInteractionRecord
        {
            Kind = "input-idle",
            Interaction = src,
            LatencyMs = elapsed,
            ScopeChain = CurrentScopeChain(),
        });
    }

    /// <summary>Time a named (possibly async) operation; records it if it runs longer than the threshold.
    /// Nestable — the names form a logical activity stack in the record.</summary>
    public static IDisposable Track(string name,
        [CallerFilePath] string file = null, [CallerLineNumber] int line = 0, [CallerMemberName] string member = null)
        => Enabled ? new ScopeHandle(name, file, line, member) : NullScope.Instance;

    private static List<string> CurrentScopeChain()
    {
        var s = _scopes.Value;
        if (s == null || s.IsEmpty) return null;
        var list = new List<string>();
        for (var n = s; n != null && !n.IsEmpty; n = n.Parent) list.Insert(0, n.Name);
        return list;
    }

    private static void Record(SlowInteractionRecord rec)
    {
        try
        {
            rec.Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            try { rec.Conditions = ConditionsProvider?.Invoke(); } catch { /* conditions are best-effort */ }

            lock (_ioLock)
            {
                _recent.Enqueue(rec);
                while (_recent.Count > RecentCap) _recent.Dequeue();
                try { File.AppendAllText(_path, JsonSerializer.Serialize(rec) + "\n"); } catch { }
            }

            // Brief line in the unified perf timeline too (no stack — that's in ux-slow.log).
            FindPluginCore.Diagnostics.PerfLog.Log("ux.slow",
                ("kind", rec.Kind), ("what", rec.Interaction ?? ""), ("ms", rec.LatencyMs));
        }
        catch { /* never break the UI */ }
    }

    private static string ComputePath()
    {
        try
        {
            var dir = Path.GetDirectoryName(FindPluginCore.Diagnostics.PerfLog.FilePath);
            if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); return Path.Combine(dir, "ux-slow.log"); }
        }
        catch { }
        return Path.Combine(Path.GetTempPath(), "findneedle-ux-slow.log");
    }

    // ----- scope plumbing -----

    private sealed class ImmutableScopeStack
    {
        public readonly string Name;
        public readonly ImmutableScopeStack Parent;
        public bool IsEmpty => Name == null;
        public static readonly ImmutableScopeStack Empty = new(null, null);
        public ImmutableScopeStack(string name, ImmutableScopeStack parent) { Name = name; Parent = parent; }
    }

    private sealed class ScopeHandle : IDisposable
    {
        private readonly string _name, _callSite;
        private readonly long _startTicks;
        private readonly ImmutableScopeStack _previous;
        private bool _disposed;

        public ScopeHandle(string name, string file, int line, string member)
        {
            _name = name;
            _callSite = $"{Path.GetFileName(file)}:{line} ({member})";
            _startTicks = Environment.TickCount64;
            _previous = _scopes.Value ?? ImmutableScopeStack.Empty;
            _scopes.Value = new ImmutableScopeStack(name, _previous.IsEmpty ? null : _previous);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            long elapsed = Environment.TickCount64 - _startTicks;
            var chain = CurrentScopeChain();
            _scopes.Value = _previous; // pop
            if (elapsed < ThresholdMs) return;
            Record(new SlowInteractionRecord
            {
                Kind = "scope",
                Interaction = _name,
                LatencyMs = elapsed,
                ScopeChain = chain,
                CallSite = _callSite,
                Stack = TrimStack(Environment.StackTrace),
            });
        }

        // Keep the app frames, drop the deep framework/async-machinery noise.
        private static string TrimStack(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var lines = s.Split('\n');
            var keep = new List<string>();
            foreach (var l in lines)
            {
                if (l.Contains("FindNeedle") || l.Contains("findneedle")) keep.Add(l.Trim());
                if (keep.Count >= 20) break;
            }
            return keep.Count > 0 ? string.Join(" | ", keep) : s;
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
