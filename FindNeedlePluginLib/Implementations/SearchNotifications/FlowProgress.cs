using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FindNeedlePluginLib;

/// <summary>
/// The high-level, ordered phases of a search+open. A run uses a subset (skipped phases are dropped
/// from the plan so "Step X of N" stays accurate), in this order.
/// </summary>
public enum FlowPhase
{
    CheckCache,
    OpenLocations,
    DecodeEtl,
    ReadParse,
    Filter,
    StoreResults,
    Consolidate,
    BuildIndex,
    OpenViewer,
    LoadFirstPage,
}

/// <summary>One step in the plan, for rendering a structured checklist (fixed columns).</summary>
public sealed class FlowStep
{
    public int Number { get; init; }      // 1-based
    public string Name { get; init; } = "";
    public string Detail { get; init; } = ""; // the metric/result, e.g. "5,000,000 rows"
    public int? Percent { get; init; }        // progress for the active step, if known
    public bool PercentIsEstimate { get; init; } // true → render as "~N%"
    public bool Done { get; init; }
    public bool Current { get; init; }
}

/// <summary>
/// Cross-layer "Step X of N" coordinator for the whole search→view flow. The orchestrator declares
/// the applicable phases up front (<see cref="StartPlan"/>); the engine, the ETL decoder, and the
/// viewer call <see cref="Begin"/>/<see cref="Detail"/> as they reach their phase. Tracks a final
/// detail per phase so the UI can show a checklist of completed steps plus the current one. Lives in
/// the plugin lib so every layer can reach it.
/// </summary>
public static class FlowProgress
{
    private static readonly object _lock = new();
    private static List<FlowPhase> _plan = new();
    private static List<string> _details = new();
    private static List<int?> _percents = new();
    private static List<bool> _estimates = new();
    private static int _current = -1;

    /// <summary>Raised on any change (label of current step, step number, total). step=0 = idle.</summary>
    public static event Action<string, int, int> Updated;

    public static string CurrentLabel { get; private set; } = "";

    private static readonly Dictionary<FlowPhase, string> Names = new()
    {
        [FlowPhase.CheckCache]    = "Checking cache",
        [FlowPhase.OpenLocations] = "Opening locations",
        [FlowPhase.DecodeEtl]     = "Decoding ETL",
        [FlowPhase.ReadParse]     = "Reading & parsing logs",
        [FlowPhase.Filter]        = "Filtering",
        [FlowPhase.StoreResults]  = "Storing results",
        [FlowPhase.Consolidate]   = "Consolidating",
        [FlowPhase.BuildIndex]    = "Building search index",
        [FlowPhase.OpenViewer]    = "Opening viewer",
        [FlowPhase.LoadFirstPage] = "Loading first page",
    };

    public static string NameOf(FlowPhase p) => Names.TryGetValue(p, out var n) ? n : p.ToString();

    public static void StartPlan(IEnumerable<FlowPhase> phases)
    {
        lock (_lock)
        {
            _plan = (phases ?? Enumerable.Empty<FlowPhase>()).Distinct().OrderBy(p => (int)p).ToList();
            _details = Enumerable.Repeat("", _plan.Count).ToList();
            _percents = Enumerable.Repeat((int?)null, _plan.Count).ToList();
            _estimates = Enumerable.Repeat(false, _plan.Count).ToList();
            _current = -1;
        }
        Fire();
    }

    public static void Begin(FlowPhase phase, string detail = "")
    {
        lock (_lock)
        {
            int i = _plan.IndexOf(phase);
            if (i < 0) { _plan.Add(phase); _details.Add(""); _percents.Add(null); _estimates.Add(false); i = _plan.Count - 1; }
            if (i >= _current) _current = i; // monotonic
            if (!string.IsNullOrEmpty(detail)) _details[_current] = detail;
        }
        Fire();
    }

    public static void Detail(string detail, int? percent = null, bool estimate = false)
    {
        lock (_lock)
        {
            if (_current >= 0 && _current < _details.Count)
            {
                _details[_current] = detail ?? "";
                _percents[_current] = percent;
                _estimates[_current] = estimate;
            }
        }
        Fire();
    }

    public static void Complete()
    {
        lock (_lock) { _current = -1; _plan = new(); _details = new(); _percents = new(); _estimates = new(); }
        CurrentLabel = "";
        Updated?.Invoke("", 0, 0);
    }

    /// <summary>Snapshot of all planned steps with their state + last detail (for the checklist UI).</summary>
    public static List<FlowStep> Steps()
    {
        lock (_lock)
        {
            var list = new List<FlowStep>(_plan.Count);
            for (int i = 0; i < _plan.Count; i++)
                list.Add(new FlowStep
                {
                    Number = i + 1,
                    Name = NameOf(_plan[i]),
                    Detail = _details[i],
                    Percent = i == _current ? _percents[i] : null, // % only for the active step
                    PercentIsEstimate = _estimates[i],
                    Done = i < _current,
                    Current = i == _current,
                });
            return list;
        }
    }

    public static int Total { get { lock (_lock) return _plan.Count; } }

    /// <summary>Completed steps as "✓ Reading &amp; parsing logs · 5,000,000 rows", newline-separated.</summary>
    public static string HistoryText()
    {
        lock (_lock)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _current && i < _plan.Count; i++)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("✓ ").Append(NameOf(_plan[i]));
                if (!string.IsNullOrEmpty(_details[i])) sb.Append(" · ").Append(_details[i]);
            }
            return sb.ToString();
        }
    }

    private static void Fire()
    {
        string label; int step, total;
        lock (_lock)
        {
            total = _plan.Count;
            step = _current + 1;
            if (_current < 0 || _current >= _plan.Count) label = "";
            else
            {
                label = $"Step {step} of {total} · {NameOf(_plan[_current])}";
                if (!string.IsNullOrEmpty(_details[_current])) label += $" · {_details[_current]}";
            }
        }
        CurrentLabel = label;
        Updated?.Invoke(label, step, total);
    }
}
