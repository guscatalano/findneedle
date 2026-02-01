namespace FindNeedlePluginLib;

/// <summary>
/// Specifies the output type for UML generation.
/// </summary>
public enum UmlOutputType
{
    /// <summary>
    /// Generate an image file (PNG, SVG, etc.). May require external tools.
    /// </summary>
    ImageFile,

    /// <summary>
    /// Generate an HTML file for browser viewing. Usually always available.
    /// </summary>
    Browser
}

/// <summary>
/// Interface for UML diagram generators.
/// </summary>
public interface IUMLGenerator
{
    /// <summary>
    /// Gets the name of the UML generator.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the supported file extension for input files (e.g., ".pu", ".mmd").
    /// </summary>
    string InputFileExtension { get; }

    /// <summary>
    /// Gets the output types supported by this generator.
    /// </summary>
    UmlOutputType[] SupportedOutputTypes { get; }

    /// <summary>
    /// Generates a UML diagram from the specified input file.
    /// </summary>
    /// <param name="inputPath">Path to the input file containing UML markup.</param>
    /// <param name="outputType">The desired output type.</param>
    /// <returns>Path to the generated output file.</returns>
    string GenerateUML(string inputPath, UmlOutputType outputType = UmlOutputType.ImageFile);

    /// <summary>
    /// Checks if a specific output type is supported on the current system.
    /// </summary>
    /// <param name="outputType">The output type to check.</param>
    /// <returns>True if the output type can be used; otherwise, false.</returns>
    bool IsSupported(UmlOutputType outputType);
}
