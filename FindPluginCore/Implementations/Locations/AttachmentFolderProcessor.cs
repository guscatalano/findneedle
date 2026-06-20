using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
/// </summary>
public static class AttachmentFolderProcessor
{
    public static List<ISearchResult> ProcessFolder(string folder, CancellationToken cancellationToken = default)
    {
        ExpandZips(folder);
        var loc = new FolderLocation { path = folder };
        loc.SetExtensionProcessorList(
            PluginManager.GetSingleton().GetAllPluginsInstancesOfAType<IFileExtensionProcessor>());
        loc.LoadInMemory(cancellationToken);
        return loc.Search(cancellationToken);
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
}
