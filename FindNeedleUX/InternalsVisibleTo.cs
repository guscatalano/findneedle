using System.Runtime.CompilerServices;

// Exposes internal test seams (e.g. ResultsViewerSettings storage redirection) to the unit-test
// assembly without making them part of the public API.
[assembly: InternalsVisibleTo("FindNeedleUX.UnitTests")]
