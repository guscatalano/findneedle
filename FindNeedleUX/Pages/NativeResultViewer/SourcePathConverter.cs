using System;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml.Data;

namespace FindNeedleUX.Pages.NativeResultViewer;

/// <summary>
/// Trims FindNeedle's temp-extraction noise out of a Source value for DISPLAY only — the full path
/// stays the filter/sort/cache key, so filtering is unchanged. An archive is extracted to
/// <c>…\FindNeedleTemp_&lt;id&gt;_&lt;hex&gt;\&lt;archive&gt;_&lt;id&gt;_&lt;hex&gt;\inner\file</c>; that
/// collapses to <c>&lt;archive&gt;/inner/file</c>. Any leading kind label (e.g. "LocalEventLogRecord-")
/// is preserved; non-temp paths are returned unchanged.
/// </summary>
public static class SourcePathTrimmer
{
    // The temp dirs are "<hint>_<digits>_<8-hex>" (see TempStorage.GenerateRandomFolderName): the outer
    // is FindNeedleTemp, the next is the archive's base name.
    private static readonly Regex TempRx = new(
        @"FindNeedleTemp_\d+_[0-9a-fA-F]{8}[\\/](?<arch>[^\\/]+?)_\d+_[0-9a-fA-F]{8}[\\/](?<inner>.+)$",
        RegexOptions.Compiled);
    private static readonly Regex DriveRx = new(@"[A-Za-z]:[\\/]", RegexOptions.Compiled);

    public static string Trim(string source)
    {
        if (string.IsNullOrEmpty(source)) return source;
        var m = TempRx.Match(source);
        if (!m.Success) return source;

        // Keep whatever kind label precedes the path (e.g. "LocalEventLogRecord-"), drop the temp path.
        var head = source.Substring(0, m.Index);
        var label = head;
        var d = DriveRx.Match(head);
        if (d.Success) label = head.Substring(0, d.Index);
        else { var u = head.IndexOf(@"\\", StringComparison.Ordinal); if (u >= 0) label = head.Substring(0, u); }

        var inner = m.Groups["inner"].Value.Replace('\\', '/');
        return label + m.Groups["arch"].Value + "/" + inner;
    }
}

/// <summary>XAML converter wrapper for <see cref="SourcePathTrimmer.Trim"/> (Source column + details).</summary>
public sealed class SourcePathConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => SourcePathTrimmer.Trim(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value;
}
