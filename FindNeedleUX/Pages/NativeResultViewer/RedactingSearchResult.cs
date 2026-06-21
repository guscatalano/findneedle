using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FindNeedlePluginLib;

namespace FindNeedleUX.Pages.NativeResultViewer;

/// <summary>
/// Wraps an <see cref="ISearchResult"/> and masks matched text in its text-bearing fields, so PII is
/// replaced (e.g. with <c>[REDACTED]</c>) everywhere the viewer reads it — grid, details, search, and
/// export. Used by the rule-view filter when "redact" rules are active. Non-text fields (time, level,
/// ids) pass through untouched. Redaction is applied once, eagerly, and cached.
/// </summary>
internal sealed class RedactingSearchResult : ISearchResult
{
    private readonly ISearchResult _inner;
    private readonly IReadOnlyList<(Regex match, string replacement)> _rules;

    private string _message, _searchable, _source, _taskName, _username, _machineName, _opCode;
    private bool _done;

    public RedactingSearchResult(ISearchResult inner, IReadOnlyList<(Regex match, string replacement)> rules)
    {
        _inner = inner;
        _rules = rules;
    }

    private string Redact(string s)
    {
        if (string.IsNullOrEmpty(s) || _rules == null) return s;
        foreach (var (match, replacement) in _rules)
        {
            try { s = match.Replace(s, replacement); }
            catch (RegexMatchTimeoutException) { /* leave this field as-is on a pathological pattern */ }
        }
        return s;
    }

    private void EnsureRedacted()
    {
        if (_done) return;
        _message = Redact(_inner.GetMessage());
        _searchable = Redact(_inner.GetSearchableData());
        _source = Redact(_inner.GetSource());
        _taskName = Redact(_inner.GetTaskName());
        _username = Redact(_inner.GetUsername());
        _machineName = Redact(_inner.GetMachineName());
        _opCode = Redact(_inner.GetOpCode());
        _done = true;
    }

    // Redacted text fields.
    public string GetMessage() { EnsureRedacted(); return _message; }
    public string GetSearchableData() { EnsureRedacted(); return _searchable; }
    public string GetSource() { EnsureRedacted(); return _source; }
    public string GetTaskName() { EnsureRedacted(); return _taskName; }
    public string GetUsername() { EnsureRedacted(); return _username; }
    public string GetMachineName() { EnsureRedacted(); return _machineName; }
    public string GetOpCode() { EnsureRedacted(); return _opCode; }

    // Pass-through fields (no PII risk / not free text).
    public DateTime GetLogTime() => _inner.GetLogTime();
    public Level GetLevel() => _inner.GetLevel();
    public string GetResultSource() => _inner.GetResultSource();
    public void WriteToConsole() => _inner.WriteToConsole();
    public long GetRowId() => _inner.GetRowId();
    public string GetProcessId() => _inner.GetProcessId();
    public string GetThreadId() => _inner.GetThreadId();
    public string GetActivityId() => _inner.GetActivityId();
    public string GetEventId() => _inner.GetEventId();
    public string GetKeywords() => _inner.GetKeywords();
    public string GetRelatedActivityId() => _inner.GetRelatedActivityId();
    public string GetChannel() => _inner.GetChannel();
    public string GetProviderGuid() => _inner.GetProviderGuid();
    public string GetRecordId() => _inner.GetRecordId();
    public string GetProcessName() => _inner.GetProcessName();
    public string GetStructuredData() => Redact(_inner.GetStructuredData());
}
