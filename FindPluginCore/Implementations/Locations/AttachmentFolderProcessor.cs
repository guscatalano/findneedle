using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using findneedle.PluginSubsystem;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;

namespace findneedle.Implementations;

/// <summary>
/// Runs a folder of already-downloaded files (e.g. an issue's attachments) through the normal
/// file-extension pipeline: a <see cref="FolderLocation"/> wired with every registered
/// <see cref="IFileExtensionProcessor"/>, so .log/.etl/.evtx inside parse into search results.
/// Any .zip is expanded in place first (the pipeline's zip recursion isn't wired for this path),
/// so a zipped bundle of logs — a very common attachment — opens too.
///
/// When a <c>friendlySource</c> is given (e.g. "GitHub owner/repo#3"), each result's reported source
/// has the throwaway temp path rewritten to that origin, so the viewer's Source reads meaningfully
/// instead of "C:\…\Temp\ghissue_…\app.log".
/// </summary>
public static class AttachmentFolderProcessor
{
    public static List<ISearchResult> ProcessFolder(string folder, CancellationToken cancellationToken = default,
                                                    string? friendlySource = null)
    {
        ExpandZips(folder);
        var loc = new FolderLocation { path = folder };
        loc.SetExtensionProcessorList(
            PluginManager.GetSingleton().GetAllPluginsInstancesOfAType<IFileExtensionProcessor>());
        loc.LoadInMemory(cancellationToken);
        var results = loc.Search(cancellationToken);

        if (string.IsNullOrEmpty(friendlySource)) return results;
        var root = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return results.Select(r => (ISearchResult)new RebasedSourceResult(r, root, friendlySource!)).ToList();
    }

    /// <summary>Extract every .zip into a sibling subfolder and delete the archive, repeating a few
    /// times so nested zips also unpack. FolderLocation recurses into subfolders, so the extracted
    /// files are then parsed by their normal processors.</summary>
    private static void ExpandZips(string folder)
    {
        for (int pass = 0; pass < 4; pass++)
        {
            var zips = Directory.GetFiles(folder, "*.zip", SearchOption.AllDirectories);
            if (zips.Length == 0) return;
            foreach (var zip in zips)
            {
                try
                {
                    var dest = Path.Combine(Path.GetDirectoryName(zip)!,
                        Path.GetFileNameWithoutExtension(zip) + "_unzipped");
                    Directory.CreateDirectory(dest);
                    ZipFile.ExtractToDirectory(zip, dest, overwriteFiles: true);
                    File.Delete(zip);
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log($"AttachmentFolderProcessor: failed to expand {zip}: {ex.Message}");
                    try { File.Delete(zip); } catch { } // avoid re-trying a bad zip forever
                }
            }
        }
    }

    /// <summary>Wraps a result so its source string has the temp-folder root rewritten to a friendly
    /// origin; everything else delegates to the inner result.</summary>
    private sealed class RebasedSourceResult : ISearchResult
    {
        private readonly ISearchResult _inner;
        private readonly string _tempRoot;
        private readonly string _origin;

        public RebasedSourceResult(ISearchResult inner, string tempRoot, string origin)
        { _inner = inner; _tempRoot = tempRoot; _origin = origin; }

        public string GetResultSource()
        {
            var s = _inner.GetResultSource() ?? "";
            if (string.IsNullOrEmpty(_tempRoot) || !s.Contains(_tempRoot, StringComparison.OrdinalIgnoreCase))
                return s;
            return s.Replace(_tempRoot, _origin, StringComparison.OrdinalIgnoreCase)
                    .Replace("_unzipped" + Path.DirectorySeparatorChar, "/")
                    .Replace(Path.DirectorySeparatorChar, '/');
        }

        public DateTime GetLogTime() => _inner.GetLogTime();
        public string GetMachineName() => _inner.GetMachineName();
        public void WriteToConsole() => _inner.WriteToConsole();
        public Level GetLevel() => _inner.GetLevel();
        public string GetUsername() => _inner.GetUsername();
        public string GetTaskName() => _inner.GetTaskName();
        public string GetOpCode() => _inner.GetOpCode();
        public string GetSource() => _inner.GetSource();
        public string GetSearchableData() => _inner.GetSearchableData();
        public string GetMessage() => _inner.GetMessage();
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
        public string GetStructuredData() => _inner.GetStructuredData();
    }
}
