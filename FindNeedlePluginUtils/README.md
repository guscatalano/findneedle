# FindNeedlePluginUtils

This library provides utility classes for Find Needle plugin and extension development. Below is a description of every class in this library:

## Classes

### 1. `PlantUMLGenerator`
Generates UML diagrams using PlantUML.
- **GenerateUML(string umlinput)**: Generates a UML diagram from a PlantUML file and returns the path to the output image.
- **IsSupported()**: Checks if PlantUML and Java are available on the system.
- **GetPlantUMLPath()**: Returns the configured or default path to the PlantUML JAR file, using PluginManager/PluginConfig if available.
- **IsJavaRuntimeInstalled()**: Checks if the Java runtime is installed.

---

This library is intended to be used as a utility layer for Find Needle plugin authors, providing helpers for common plugin-related tasks.
