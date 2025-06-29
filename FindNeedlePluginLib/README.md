# FindNeedlePluginLib

FindNeedlePluginLib is a core library for the Find Needle ecosystem, providing interfaces and base types for plugin development. It enables extensibility for search, filtering, processing, and output within the Find Needle application suite.

## Features
- **Plugin Interfaces:**
  - Interfaces for result processors, search locations, filters, results, file extension processors, plugin metadata, command-line parsing, and output.
- **Base Classes & Structs:**
  - Common base implementations and utility classes for plugin authors.
- **Notifications & Statistics:**
  - Classes and enums for reporting search progress, notifications, and statistics.
- **Test Utilities:**
  - Includes test classes and mocks to help with plugin development and testing.

## Classes, Interfaces, Structs, and Enums

### Interfaces
- **IResultProcessor**: For implementing custom result processors.
  - `void ProcessResults(List<ISearchResult> results)`
  - `string GetOutputFile(string optionalOutputFolder = "")`
  - `string GetDescription()`
  - `string GetOutputText()`
- **ISearchLocation** (abstract class): Represents a searchable location.
  - `void LoadInMemory()`
  - `List<ISearchResult> Search(ISearchQuery? searchQuery = null)`
  - `void SetSearchDepth(SearchLocationDepth depth)`
  - `SearchLocationDepth GetSearchDepth()`
  - `string GetDescription()`
  - `string GetName()`
  - `void ClearStatistics()`
  - `List<ReportFromComponent> ReportStatistics()`
- **ISearchFilter**: For defining search filters.
  - `bool Filter(ISearchResult entry)`
  - `string GetDescription()`
  - `string GetName()`
- **ISearchResult**: Represents a single search result.
  - `DateTime GetLogTime()`
  - `string GetMachineName()`
  - `void WriteToConsole()`
  - `Level GetLevel()`
  - `string GetUsername()`
  - `string GetTaskName()`
  - `string GetOpCode()`
  - `string GetSource()`
  - `string GetSearchableData()`
  - `string GetMessage()`
  - `string GetResultSource()`
- **IFileExtensionProcessor**: For file-type-based processing plugins.
  - `List<string> RegisterForExtensions()`
  - `void OpenFile(string fileName)`
  - `bool CheckFileFormat()`
  - `void LoadInMemory()`
  - `void DoPreProcessing()`
  - `List<ISearchResult> GetResults()`
  - `string GetFileName()`
  - `Dictionary<string, int> GetProviderCount()`
- **IPluginDescription**: For describing plugin metadata.
  - `string GetPluginTextDescription()`
  - `string GetPluginFriendlyName()`
  - `string GetPluginClassName()`
  - Static helpers for plugin description serialization and validation.
- **ISearchOutput**: For output plugins.
  - `void WriteAllOutput(List<ISearchResult> result)`
  - `void WriteOutput(ISearchResult result)`
  - `string GetOutputFileName()`
- **ICommandLineParser**: For plugins supporting command-line parsing.
  - `CommandLineRegistration RegisterCommandHandler()`
  - `void ParseCommandParameterIntoQuery(string parameter)`
  - `void Clone(ICommandLineParser original)`
- **IReportStatistics**: For reporting statistics from components.
  - `void ClearStatistics()`
  - `List<ReportFromComponent> ReportStatistics()`

### Structs & Classes
- **PluginDescription**: Struct holding plugin metadata.
- **CommandLineRegistration**: Class for command-line handler registration.
- **ReportFromComponent**: Class for reporting statistics from a component.
- **SearchStatistics**: Class for tracking and reporting search statistics.
- **MemorySnapshot**: Class for capturing memory usage snapshots.
- **SearchProgressSink**: Class for reporting progress (numeric/text) during search.
- **SearchStepNotificationSink**: Class for reporting step changes during search.

### Enums
- **Level**: Severity level for search results (`Catastrophic`, `Error`, `Warning`, `Info`, `Verbose`).
- **SearchLocationDepth**: How deep to search a location (`Shallow`, `Intermediate`, `Deep`, `Crush`).
- **SearchStep**: Steps in the search process (`AtLoad`, `AtSearch`, `AtLaunch`, `AtProcessor`, `AtOutput`, `Total`).
- **CommandLineHandlerType**: Type of command-line handler (`Location`, `Filter`, `Processor`).

### Test Classes
- **FakeSearchResult**: Mock implementation of `ISearchResult` for testing.
- **FakePluginDescription**: Mock implementation of `IPluginDescription` for testing.
- **FakeSearchQuery**: Mock implementation of `ISearchQuery` for testing.
- **FakeCmdLineParser**: Mock implementation for command-line parser testing.

## Usage
- Reference this library in your plugin or extension project.
- Implement the relevant interfaces to create new search, filter, processor, or output plugins.
- Use the provided base types and utilities to simplify plugin development.

## Example using FindNeedlePluginLib;

public class MyCustomProcessor : IResultProcessor
{
    public void ProcessResults(List<ISearchResult> results) { /* ... */ }
    public string GetOutputFile(string optionalOutputFolder = "") => "output.txt";
    public string GetDescription() => "My custom processor";
    public string GetOutputText() => "Results...";
}
## Contributing
Contributions are welcome! Please submit issues or pull requests to help improve the library.

## License
This library is part of the Find Needle project and is released under the MIT License.
