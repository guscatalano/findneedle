using System;
using System.Threading.Tasks;

namespace FindNeedleUX.Services;

/// <summary>One entry in the Ctrl+K command palette: a labelled action the user can jump to by typing.
/// <see cref="Keywords"/> are extra search terms (not shown) so a command is findable by synonyms.</summary>
public sealed class PaletteCommand
{
    public string Label { get; init; } = "";
    public string Category { get; init; } = "";
    public string Keywords { get; init; } = "";
    public Func<Task> Run { get; init; } = () => Task.CompletedTask;

    /// <summary>Everything the filter matches against, lower-cased once for cheap substring checks.</summary>
    public string Haystack => (Label + " " + Category + " " + Keywords).ToLowerInvariant();
}
