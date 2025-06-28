# FindNeedleCoreUtils

This library provides core utility classes for file I/O, temporary storage management, text manipulation, and unit conversions. Below is a description of every class and enum in this library:

## Classes and Enums

### 1. `FileIO`
Provides static methods for file and directory operations.
- **GetAppDataFindNeedlePluginFolder()**: Returns the path to the AppData folder for FindNeedlePlugin, creating it if necessary.
- **FindFullPathToFile(string fileName, bool throwError=false)**: Attempts to resolve the full path to a file by searching several common locations.
- **GetAllFilesErrorCallback**: Delegate for error handling during file enumeration.
- **GetAllFiles(string path, GetAllFilesErrorCallback? errorHandler = null)**: Recursively enumerates all files in a directory tree, optionally handling errors with a callback.

### 2. `TempStorage`
Manages temporary storage directories for the application.
- **GetSingleton()**: Returns a singleton instance of TempStorage.
- **GetMainTempPath()**: Gets the main temp path from the singleton instance.
- **DeleteSomeTempPath(string randomDir)**: Deletes a temp directory and waits until it is removed.
- **GetNewTempPath(string hint)**: Generates a new temp path with a hint for identification.
- **GenerateRandomFolderName(string hint)**: Generates a random folder name using a hint and timestamp.
- **GenerateNewPath(string root, string hint = "FindNeedleTemp")**: Generates a new unique temp path under the specified root.
- **GetExistingMainTempPath()**: Returns the current main temp path.
- **Dispose()**: Cleans up the temp path by deleting it.

### 3. `TextManipulation`
Provides static methods for parsing and manipulating text, especially for command-line arguments.
- **ParseCommandLineIntoDictionary(string[] args)**: Parses command-line arguments into a list of key-value pairs.
- **SplitApart(string text)**: Splits a comma-separated string into a list, removing invalid characters.
- **ReplaceInvalidChars(string text)**: Removes certain invalid characters and trims whitespace from a string.

#### `CommandLineArgument`
A simple class representing a key-value pair for command-line arguments.
- **key**: The argument key.
- **value**: The argument value.

### 4. `ByteUtils`
Provides static methods for working with byte sizes.
- **BytesToFriendlyString(long value, int decimalPlaces = 1)**: Converts a byte value to a human-readable string (e.g., "1.5 MB").

### 5. `TimeAgoUnit` (enum)
Enumerates time units for "time ago" calculations.
- **Second, Minute, Hour, Day**: Units of time.

---

This library is intended to be used as a foundational utility layer for the FindNeedle ecosystem, providing robust and reusable helpers for common tasks.
