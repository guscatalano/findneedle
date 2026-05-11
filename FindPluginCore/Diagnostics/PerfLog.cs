using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace FindPluginCore.Diagnostics;

/// <summary>
/// Append-only timing log used to diagnose where wall-clock time goes during search + viewer
/// open. Writes structured <c>key=value</c> records to
/// <c>%LocalAppData%\FindNeedle\perf-log.txt</c>, one per line, plus a session header at process
/// start so multiple runs in the same file are easy to separate. Failures are swallowed —
/// instrumentation must never break the path it's measuring.
///
/// Format:
///   2026-05-10T14:23:01.123 phase=search.start rows=0 storage=hybrid
///   2026-05-10T14:23:05.123 phase=location.end name=large-sample.log rows=500000 elapsed_ms=3772
/// </summary>
public static class PerfLog
{
    private static readonly object _lock = new();
    private static readonly string _path = ComputePath();
    private static readonly long _maxBytes = 1L * 1024 * 1024; // 1 MB before rotating to .old
    private static bool _sessionHeaderWritten;

    private static string ComputePath()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FindNeedle");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "perf-log.txt");
        }
        catch
        {
            // Fallback to temp if LocalAppData isn't writable for some reason.
            return Path.Combine(Path.GetTempPath(), "findneedle-perf-log.txt");
        }
    }

    /// <summary>Absolute path to the log file, surfaced for UI / docs.</summary>
    public static string FilePath => _path;

    /// <summary>
    /// Write a single phase event. <paramref name="kvs"/> is an unbounded list of (key, value)
    /// tuples appended after <c>phase=</c>. Values are formatted with invariant culture; strings
    /// are emitted verbatim (no quoting) under the assumption that callers don't pass spaces or
    /// equals signs — keep the keys short and the values numeric / single-token.
    /// </summary>
    public static void Log(string phase, params (string key, object value)[] kvs)
    {
        if (string.IsNullOrEmpty(phase)) return;

        // Build the record before taking the lock so file I/O is the only thing serialised.
        var sb = new StringBuilder(128);
        sb.Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        sb.Append(" phase=").Append(phase);
        if (kvs != null)
        {
            foreach (var (k, v) in kvs)
            {
                if (string.IsNullOrEmpty(k)) continue;
                sb.Append(' ').Append(k).Append('=').Append(FormatValue(v));
            }
        }
        sb.Append('\n');
        WriteLineRaw(sb.ToString());
    }

    /// <summary>
    /// Marks the start of a phase and returns an <see cref="IDisposable"/> scope that emits a
    /// matching <c>.end</c> record with <c>elapsed_ms</c> when disposed. Usage:
    /// <code>
    /// using (PerfLog.Scope("consolidate", ("known_rows", 500000)))
    /// {
    ///     // work
    /// }
    /// </code>
    /// </summary>
    public static IDisposable Scope(string phase, params (string key, object value)[] kvs)
    {
        Log(phase + ".start", kvs);
        return new PhaseScope(phase, Environment.TickCount64);
    }

    private static string FormatValue(object v)
    {
        if (v == null) return "null";
        return v switch
        {
            string s => s.Replace(' ', '_'), // keep records space-delimited
            bool b => b ? "true" : "false",
            float f => f.ToString("R", CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            decimal dec => dec.ToString(CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString()
        };
    }

    private static void WriteLineRaw(string line)
    {
        try
        {
            lock (_lock)
            {
                if (!_sessionHeaderWritten)
                {
                    _sessionHeaderWritten = true;
                    RotateIfNeeded_NoLock();
                    var header = new StringBuilder(128);
                    header.Append("# ----- session start ");
                    header.Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
                    header.Append(" pid=").Append(Environment.ProcessId);
                    header.Append(" -----\n");
                    File.AppendAllText(_path, header.ToString(), Encoding.UTF8);
                }
                File.AppendAllText(_path, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Never let perf-log I/O take down the caller. The file is best-effort.
        }
    }

    private static void RotateIfNeeded_NoLock()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var info = new FileInfo(_path);
            if (info.Length < _maxBytes) return;
            var old = _path + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(_path, old);
        }
        catch { /* ignore */ }
    }

    private sealed class PhaseScope : IDisposable
    {
        private readonly string _phase;
        private readonly long _startTicks;
        private bool _disposed;

        public PhaseScope(string phase, long startTicks)
        {
            _phase = phase;
            _startTicks = startTicks;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            long elapsed = Environment.TickCount64 - _startTicks;
            Log(_phase + ".end", ("elapsed_ms", elapsed));
        }
    }
}
