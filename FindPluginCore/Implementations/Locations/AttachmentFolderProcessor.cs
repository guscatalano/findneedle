using System.Collections.Generic;
using System.Threading;
using findneedle.PluginSubsystem;
using FindNeedlePluginLib;

namespace findneedle.Implementations;

/// <summary>
/// Runs a folder of already-downloaded files (e.g. an issue's attachments) through the normal
/// file-extension pipeline: a <see cref="FolderLocation"/> wired with every registered
/// <see cref="IFileExtensionProcessor"/>, so .log/.etl/.evtx/.zip inside parse into search results.
/// Lets the online-source plugins reuse the exact local-file processing without re-implementing it.
/// </summary>
public static class AttachmentFolderProcessor
{
    public static List<ISearchResult> ProcessFolder(string folder, CancellationToken cancellationToken = default)
    {
        var loc = new FolderLocation { path = folder };
        loc.SetExtensionProcessorList(
            PluginManager.GetSingleton().GetAllPluginsInstancesOfAType<IFileExtensionProcessor>());
        loc.LoadInMemory(cancellationToken);
        return loc.Search(cancellationToken);
    }
}
