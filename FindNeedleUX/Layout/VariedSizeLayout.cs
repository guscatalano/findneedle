// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using findneedle;
using Windows.Foundation;
using FindNeedlePluginLib;
using VirtualizingLayout = Microsoft.UI.Xaml.Controls.VirtualizingLayout;
using VirtualizingLayoutContext = Microsoft.UI.Xaml.Controls.VirtualizingLayoutContext;

namespace FindNeedleUX;



public class LogLine
{

    

    public LogLine(ISearchResult searchResult, int index)
    {
        Index = index;
        // Stable identity for record lookup / tagging / MCP. Disk-backed rows carry the SQLite Id;
        // backends without one (in-memory) return -1, so fall back to the load-order Index, which is
        // stable for those sources (the in-memory list is built once and only sliced/sorted after).
        var rowId = searchResult.GetRowId();
        RowId = rowId >= 0 ? rowId : index;
        Provider = NormalizeMissing(searchResult.GetSource());
        TaskName = NormalizeMissing(searchResult.GetTaskName());
        LogTime = searchResult.GetLogTime();
        Time = LogTime.ToString("o");
        Source = NormalizeMissing(searchResult.GetResultSource());
        Level = searchResult.GetLevel().ToString();
        MachineName = NormalizeMissing(searchResult.GetMachineName());
        Username = NormalizeMissing(searchResult.GetUsername());
        OpCode = NormalizeMissing(searchResult.GetOpCode());
        SearchableData = searchResult.GetSearchableData(); // unmodified original; used for filtering fallback
        Message = CleanMessage(searchResult.GetMessage(), LogTime, Level);
    }

    /// <summary>
    /// Plugins used to return <c>ISearchResult.NOT_SUPPORTED</c> ("!NOT_SUPPORTED!") for fields
    /// they couldn't fill. The plugins now return empty, but normalize defensively so any older
    /// plugin still emitting the sentinel doesn't leak into the UI.
    /// </summary>
    private static string NormalizeMissing(string value)
        => value == ISearchResult.NOT_SUPPORTED ? string.Empty : value;

    /// <summary>
    /// Strip a leading <c>[timestamp]</c> and a leading <c>LEVEL:</c> from a log message when they
    /// duplicate what we already show in the Time / Level columns. The original full text is kept
    /// in <see cref="SearchableData"/> so search continues to match the un-cleaned form.
    /// </summary>
    internal static string CleanMessage(string message, DateTime time, string level)
    {
        if (string.IsNullOrEmpty(message)) return message;
        var working = message.TrimStart();

        // [2026-03-01 09:00:00]  -or-  [2026-03-01T09:00:00]
        if (time != DateTime.MinValue && working.StartsWith("["))
        {
            int end = working.IndexOf(']');
            if (end > 0 && end <= 40)
            {
                var inside = working.Substring(1, end - 1);
                if (DateTime.TryParse(inside, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeLocal, out var parsed)
                    && Math.Abs((parsed - time).TotalSeconds) < 1.0)
                {
                    working = working.Substring(end + 1).TrimStart(' ', '\t', ':', '-');
                }
            }
        }

        // LEVEL:  (handle the level we parsed plus a couple of common synonyms)
        if (!string.IsNullOrEmpty(level))
        {
            var prefixes = level switch
            {
                "Warning"      => new[] { "WARNING:", "WARN:" },
                "Catastrophic" => new[] { "CRITICAL:", "FATAL:", "CATASTROPHIC:" },
                "Error"        => new[] { "ERROR:" },
                "Verbose"      => new[] { "VERBOSE:", "TRACE:" },
                "Debug"        => new[] { "DEBUG:" },
                "Info"         => new[] { "INFO:", "INFORMATION:" },
                _              => new[] { level + ":" }
            };
            foreach (var p in prefixes)
            {
                if (working.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                {
                    working = working.Substring(p.Length).TrimStart();
                    break;
                }
            }
        }

        return working;
    }

    

    //This is really a view into SearchResult
    public int Index
    {
        get;set;
    }

    /// <summary>
    /// Stable row identity, independent of the current filter/sort/page (unlike <see cref="Index"/>,
    /// which is the displayed position). The SQLite <c>FilteredResults.Id</c> for disk-backed
    /// searches; the load-order position for in-memory ones. Used by record lookup, row tagging,
    /// and the MCP server as the durable handle for a row.
    /// </summary>
    public long RowId
    {
        get; set;
    }
    public string Time
    {
        get;set;
    }
    public string Provider
    {
        get; set;
    }
    public string TaskName
    {
        get; set;
    }
    public string Message
    {
        get; set;
    }
    public string Source
    {
        get; set;
    }

    public string Level
    {
        get; set;
    }
    public string MachineName
    {
        get; set;
    }
    public string Username
    {
        get; set;
    }
    public string OpCode
    {
        get; set;
    }
    public string SearchableData
    {
        get; set;
    }
    public DateTime LogTime
    {
        get; set;
    }
}

public class Recipe
{
    public int Num
    {
        get; set;
    }
    public string Ingredients
    {
        get; set;
    }
    public List<string> IngList
    {
        get; set;
    }
    public string Name
    {
        get; set;
    }
    public string Color
    {
        get; set;
    }
}
public class VariedImageSizeLayout : VirtualizingLayout
{
    public double Width { get; set; } = 500;
    protected override void OnItemsChangedCore(VirtualizingLayoutContext context, object source, NotifyCollectionChangedEventArgs args)
    {
        // The data collection has changed, so the bounds of all the indices are not valid anymore. 
        // We need to re-evaluate all the bounds and cache them during the next measure.
        m_cachedBounds.Clear();
        m_firstIndex = m_lastIndex = 0;
        cachedBoundsInvalid = true;
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        var viewport = context.RealizationRect;

        if (availableSize.Width != m_lastAvailableWidth || cachedBoundsInvalid)
        {
            UpdateCachedBounds(availableSize);
            m_lastAvailableWidth = availableSize.Width;
        }

        // Initialize column offsets
        var numColumns = Math.Max(1, (int)(availableSize.Width / Width));
        if (m_columnOffsets.Count == 0)
        {
            for (var i = 0; i < numColumns; i++)
            {
                m_columnOffsets.Add(0);
            }
        }

        m_firstIndex = GetStartIndex(viewport);
        var currentIndex = m_firstIndex;
        var nextOffset = -1.0;

        // Measure items from start index to when we hit the end of the viewport.
        while (currentIndex < context.ItemCount && nextOffset < viewport.Bottom)
        {
            var child = context.GetOrCreateElementAt(currentIndex);
            child.Measure(new Size(Width, availableSize.Height));

            if (currentIndex >= m_cachedBounds.Count)
            {
                // We do not have bounds for this index. Lay it out and cache it.
                var columnIndex = GetIndexOfLowestColumn(m_columnOffsets, out nextOffset);
                m_cachedBounds.Add(new Rect(columnIndex * Width, nextOffset, Width, child.DesiredSize.Height));
                m_columnOffsets[columnIndex] += child.DesiredSize.Height;
            }
            else
            {
                if (currentIndex + 1 == m_cachedBounds.Count)
                {
                    // Last element. Use the next offset.
                    GetIndexOfLowestColumn(m_columnOffsets, out nextOffset);
                }
                else
                {
                    nextOffset = m_cachedBounds[currentIndex + 1].Top;
                }
            }

            m_lastIndex = currentIndex;
            currentIndex++;
        }

        var extent = GetExtentSize(availableSize);
        return extent;
    }

    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        if (m_cachedBounds.Count > 0)
        {
            for (var index = m_firstIndex; index <= m_lastIndex; index++)
            {
                var child = context.GetOrCreateElementAt(index);
                child.Arrange(m_cachedBounds[index]);
            }
        }
        return finalSize;
    }

    private void UpdateCachedBounds(Size availableSize)
    {
        var numColumns = Math.Max(1, (int)(availableSize.Width / Width));
        m_columnOffsets.Clear();
        for (var i = 0; i < numColumns; i++)
        {
            m_columnOffsets.Add(0);
        }

        for (var index = 0; index < m_cachedBounds.Count; index++)
        {
            var columnIndex = GetIndexOfLowestColumn(m_columnOffsets, out var nextOffset);
            var oldHeight = m_cachedBounds[index].Height;
            m_cachedBounds[index] = new Rect(columnIndex * Width, nextOffset, Width, oldHeight);
            m_columnOffsets[columnIndex] += oldHeight;
        }

        cachedBoundsInvalid = false;
    }

    private int GetStartIndex(Rect viewport)
    {
        var startIndex = 0;
        if (m_cachedBounds.Count == 0)
        {
            startIndex = 0;
        }
        else
        {
            // find first index that intersects the viewport
            // perhaps this can be done more efficiently than walking
            // from the start of the list.
            for (var i = 0; i < m_cachedBounds.Count; i++)
            {
                var currentBounds = m_cachedBounds[i];
                if (currentBounds.Y < viewport.Bottom &&
                    currentBounds.Bottom > viewport.Top)
                {
                    startIndex = i;
                    break;
                }
            }
        }

        return startIndex;
    }

    private int GetIndexOfLowestColumn(List<double> columnOffsets, out double lowestOffset)
    {
        var lowestIndex = 0;
        lowestOffset = columnOffsets[lowestIndex];
        for (var index = 0; index < columnOffsets.Count; index++)
        {
            var currentOffset = columnOffsets[index];
            if (lowestOffset > currentOffset)
            {
                lowestOffset = currentOffset;
                lowestIndex = index;
            }
        }

        return lowestIndex;
    }

    private Size GetExtentSize(Size availableSize)
    {
        var largestColumnOffset = m_columnOffsets[0];
        for (var index = 0; index < m_columnOffsets.Count; index++)
        {
            var currentOffset = m_columnOffsets[index];
            if (largestColumnOffset < currentOffset)
            {
                largestColumnOffset = currentOffset;
            }
        }

        return new Size(availableSize.Width, largestColumnOffset);
    }

    int m_firstIndex = 0;
    int m_lastIndex = 0;
    double m_lastAvailableWidth = 0.0;
    readonly List<double> m_columnOffsets = new();
    readonly List<Rect> m_cachedBounds = new();
    private bool cachedBoundsInvalid = false;
}
