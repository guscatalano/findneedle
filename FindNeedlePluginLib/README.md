# FindNeedlePluginLib

FindNeedlePluginLib is a core library for the Find Needle ecosystem, providing interfaces and base types for plugin development. It enables extensibility for search, filtering, processing, and output within the Find Needle application suite.

## Features
- **Plugin Interfaces:**
  - `IResultProcessor`: For implementing custom result processors.
  - `ISearchLocation`, `ISearchFilter`, `ISearchResult`: For defining search locations, filters, and result types.
  - `IFileExtensionProcessor`: For file-type-based processing plugins.
  - `IPluginDescription`: For describing plugin metadata.
- **Base Classes:**
  - Common base implementations and utility classes for plugin authors.
- **Notifications & Statistics:**
  - Interfaces and classes for reporting search progress, notifications, and statistics.
- **Test Utilities:**
  - Includes test classes and mocks to help with plugin development and testing.

## Usage
- Reference this library in your plugin or extension project.
- Implement the relevant interfaces to create new search, filter, processor, or output plugins.
- Use the provided base types and utilities to simplify plugin development.

## Example
```csharp
using findneedle.Interfaces;

public class MyCustomProcessor : IResultProcessor
{
    public void ProcessResults(List<ISearchResult> results) { /* ... */ }
    public string GetOutputFile(string optionalOutputFolder = "") => "output.txt";
    public string GetDescription() => "My custom processor";
    public string GetOutputText() => "Results...";
}
```

## Contributing
Contributions are welcome! Please submit issues or pull requests to help improve the library.

## License
This library is part of the Find Needle project and is released under the MIT License.
