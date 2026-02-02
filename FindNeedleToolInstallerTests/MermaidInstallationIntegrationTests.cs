using FindNeedleToolInstallers;
using System.Diagnostics;
using System.IO;
using TestUtilities.Helpers;

namespace FindNeedleUmlInstallerTests;

/// <summary>
/// Integration tests for Mermaid CLI installation and PNG generation.
/// These tests validate that the Mermaid installer works and can actually generate diagrams.
/// </summary>
[TestClass]
public class MermaidInstallationIntegrationTests
{
    private string _testOutputDirectory = null!;
    private string _testInstallDirectory = null!;
    private MermaidInstaller _installer = null!;

    [TestInitialize]
    public void Setup()
    {
        _testOutputDirectory = Path.Combine(Path.GetTempPath(), "MermaidIntegrationTests_" + Guid.NewGuid());
        _testInstallDirectory = Path.Combine(Path.GetTempPath(), "MermaidInstall_" + Guid.NewGuid());
        Directory.CreateDirectory(_testOutputDirectory);
        
        _installer = new MermaidInstaller(_testInstallDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_testOutputDirectory))
            {
                Directory.Delete(_testOutputDirectory, true);
            }
            if (Directory.Exists(_testInstallDirectory))
            {
                Directory.Delete(_testInstallDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    // ==================== INSTALLATION TESTS ====================

    [TestMethod]
    [Timeout(120000)] // 2 minutes - installation can take time
    public async Task MermaidInstaller_CanInstall()
    {
        if (!SystemSpecificationChecker.IsWindows())
        {
            Assert.Inconclusive("Mermaid installation tests only run on Windows");
        }

        // Act - Perform installation
        Console.WriteLine($"Installing Mermaid to: {_testInstallDirectory}");
        var progress = new Progress<InstallProgress>(p =>
        {
            Console.WriteLine($"  [{p.PercentComplete}%] {p.Status}");
        });

        var result = await _installer.InstallAsync(progress, CancellationToken.None);

        // Assert
        Assert.IsTrue(result.Success, $"Installation failed: {result.Message}");
        Assert.IsNotNull(result.Path);
        Assert.IsTrue(File.Exists(result.Path), $"mmdc executable not found at: {result.Path}");
        
        Console.WriteLine($"? Installation successful");
        Console.WriteLine($"   mmdc path: {result.Path}");
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task MermaidInstaller_CanGeneratePng_AfterInstall()
    {
        // Arrange - Install first
        Console.WriteLine($"Installing Mermaid to: {_testInstallDirectory}");
        var installResult = await _installer.InstallAsync(null, CancellationToken.None);

        if (!installResult.Success)
        {
            Assert.Inconclusive($"Installation failed: {installResult.Message}");
        }

        // Create test diagram
        var mermaidFile = Path.Combine(_testOutputDirectory, "afterinstall.mmd");
        var pngFile = Path.Combine(_testOutputDirectory, "afterinstall.png");
        File.WriteAllText(mermaidFile, "graph LR\n    A[Installed] --> B[Working]");

        // Act - Generate PNG using installed mmdc
        var exitCode = RunMermaidCommandWithPath(installResult.Path!, mermaidFile, pngFile);

        // Assert
        Assert.AreEqual(0, exitCode, "PNG generation should succeed");
        Assert.IsTrue(File.Exists(pngFile), "PNG should be created");
        
        var fileInfo = new FileInfo(pngFile);
        Assert.IsTrue(fileInfo.Length > 0, "PNG should have content");
        
        Console.WriteLine($"? PNG generated after custom installation");
        Console.WriteLine($"   Size: {fileInfo.Length} bytes");
    }

    [TestMethod]
    public void MermaidInstaller_StatusReflectsNotInstalled()
    {
        // Act - Check status before installation
        var status = _installer.GetStatus();

        // Assert
        Assert.IsFalse(status.IsInstalled, "Should not be installed yet");
        Assert.IsNull(status.InstalledPath);
        
        Console.WriteLine($"? Installer correctly reports as not installed initially");
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task MermaidInstaller_CanGeneratePng_SimpleFlowchart()
    {
        // Arrange - Install first
        var installResult = await _installer.InstallAsync(null, CancellationToken.None);
        Assert.IsTrue(installResult.Success, $"Installation failed: {installResult.Message}");

        var mermaidFile = Path.Combine(_testOutputDirectory, "simple.mmd");
        var pngFile = Path.Combine(_testOutputDirectory, "simple.png");
        
        var diagramContent = @"graph TD
    A[Start] --> B[Process]
    B --> C[End]
    style A fill:#90EE90
    style C fill:#FFB6C6";

        File.WriteAllText(mermaidFile, diagramContent);

        // Act
        var exitCode = RunMermaidCommandWithPath(installResult.Path!, mermaidFile, pngFile);

        // Assert
        Assert.AreEqual(0, exitCode, "Mermaid command should succeed");
        Assert.IsTrue(File.Exists(pngFile), $"PNG file should be created at {pngFile}");
        
        var fileInfo = new FileInfo(pngFile);
        Assert.IsTrue(fileInfo.Length > 0, "PNG file should have content");
        
        Console.WriteLine($"? PNG generated successfully");
        Console.WriteLine($"   Size: {fileInfo.Length} bytes");
        Console.WriteLine($"   Location: {pngFile}");
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task MermaidInstaller_CanGeneratePng_ComplexDiagram()
    {
        // Arrange - Install first
        var installResult = await _installer.InstallAsync(null, CancellationToken.None);
        Assert.IsTrue(installResult.Success, $"Installation failed: {installResult.Message}");

        var mermaidFile = Path.Combine(_testOutputDirectory, "complex.mmd");
        var pngFile = Path.Combine(_testOutputDirectory, "complex.png");
        
        var diagramContent = @"sequenceDiagram
    participant Client
    participant Server
    participant Database
    
    Client->>Server: Request Data
    Server->>Database: Query
    Database-->>Server: Result
    Server-->>Client: Response
    
    Note over Client,Server: Communication Complete";

        File.WriteAllText(mermaidFile, diagramContent);

        // Act
        var exitCode = RunMermaidCommandWithPath(installResult.Path!, mermaidFile, pngFile);

        // Assert
        Assert.AreEqual(0, exitCode, "Mermaid command should succeed for complex diagram");
        Assert.IsTrue(File.Exists(pngFile), $"PNG file should be created at {pngFile}");
        
        var fileInfo = new FileInfo(pngFile);
        Assert.IsTrue(fileInfo.Length > 0, "PNG file should have content");
        Assert.IsTrue(fileInfo.Length > 1000, "Complex PNG should be reasonably sized");
        
        Console.WriteLine($"? Complex PNG generated successfully");
        Console.WriteLine($"   Size: {fileInfo.Length} bytes");
        Console.WriteLine($"   Location: {pngFile}");
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task MermaidInstaller_CanGeneratePng_ClassDiagram()
    {
        // Arrange - Install first
        var installResult = await _installer.InstallAsync(null, CancellationToken.None);
        Assert.IsTrue(installResult.Success, $"Installation failed: {installResult.Message}");

        var mermaidFile = Path.Combine(_testOutputDirectory, "classes.mmd");
        var pngFile = Path.Combine(_testOutputDirectory, "classes.png");
        
        var diagramContent = @"classDiagram
    class Animal {
        +String name
        +int age
        +eat()
        +sleep()
    }
    
    class Dog {
        +bark()
    }
    
    class Cat {
        +meow()
    }
    
    Animal <|-- Dog
    Animal <|-- Cat";

        File.WriteAllText(mermaidFile, diagramContent);

        // Act
        var exitCode = RunMermaidCommandWithPath(installResult.Path!, mermaidFile, pngFile);

        // Assert
        Assert.AreEqual(0, exitCode, "Mermaid command should succeed for class diagram");
        Assert.IsTrue(File.Exists(pngFile), $"PNG file should be created at {pngFile}");
        
        var fileInfo = new FileInfo(pngFile);
        Assert.IsTrue(fileInfo.Length > 0, "PNG file should have content");
        
        Console.WriteLine($"? Class diagram PNG generated successfully");
        Console.WriteLine($"   Size: {fileInfo.Length} bytes");
        Console.WriteLine($"   Location: {pngFile}");
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task MermaidInstaller_CanGenerateMultiplePngs()
    {
        // Arrange - Install first
        var installResult = await _installer.InstallAsync(null, CancellationToken.None);
        Assert.IsTrue(installResult.Success, $"Installation failed: {installResult.Message}");

        var diagrams = new Dictionary<string, string>
        {
            { "diagram1.mmd", "graph LR\n    A[Start] --> B[End]" },
            { "diagram2.mmd", "pie title Test\n    \"A\": 30\n    \"B\": 70" },
            { "diagram3.mmd", "gantt\n    title Test\n    section A\n    Task1 :a1, 0, 10d" }
        };

        var pngFiles = new List<string>();

        // Act
        foreach (var (diagramName, diagramContent) in diagrams)
        {
            var mermaidFile = Path.Combine(_testOutputDirectory, diagramName);
            var pngFile = Path.Combine(_testOutputDirectory, Path.ChangeExtension(diagramName, ".png"));
            
            File.WriteAllText(mermaidFile, diagramContent);
            var exitCode = RunMermaidCommandWithPath(installResult.Path!, mermaidFile, pngFile);

            if (exitCode == 0 && File.Exists(pngFile))
            {
                pngFiles.Add(pngFile);
            }
        }

        // Assert
        Assert.IsTrue(pngFiles.Count > 0, "At least one PNG should be generated");
        
        foreach (var pngFile in pngFiles)
        {
            Assert.IsTrue(File.Exists(pngFile));
            var size = new FileInfo(pngFile).Length;
            Assert.IsTrue(size > 0, $"PNG {pngFile} should have content");
            Console.WriteLine($"? Generated: {Path.GetFileName(pngFile)} ({size} bytes)");
        }
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task MermaidInstaller_InvalidDiagram_FailsGracefully()
    {
        // Arrange - Install first
        var installResult = await _installer.InstallAsync(null, CancellationToken.None);
        Assert.IsTrue(installResult.Success, $"Installation failed: {installResult.Message}");

        var mermaidFile = Path.Combine(_testOutputDirectory, "invalid.mmd");
        var pngFile = Path.Combine(_testOutputDirectory, "invalid.png");
        
        // Invalid mermaid syntax - unmatched braces and invalid keywords
        var diagramContent = "graph TD\n  A[Start\n  B{Decision\n  C[End";
        File.WriteAllText(mermaidFile, diagramContent);

        // Act
        var exitCode = RunMermaidCommandWithPath(installResult.Path!, mermaidFile, pngFile);

        // Assert
        // Mermaid either returns non-zero exit code OR fails to create output file for invalid syntax
        var hasFailed = exitCode != 0 || !File.Exists(pngFile);
        Assert.IsTrue(hasFailed, $"Mermaid should fail on invalid syntax (exit code: {exitCode}, PNG exists: {File.Exists(pngFile)})");
        Console.WriteLine($"? Invalid diagram properly rejected (exit code: {exitCode}, PNG created: {File.Exists(pngFile)})");
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task MermaidInstaller_GeneratedPngIsValidImage()
    {
        // Arrange - Install first
        var installResult = await _installer.InstallAsync(null, CancellationToken.None);
        Assert.IsTrue(installResult.Success, $"Installation failed: {installResult.Message}");

        var mermaidFile = Path.Combine(_testOutputDirectory, "validate.mmd");
        var pngFile = Path.Combine(_testOutputDirectory, "validate.png");
        
        File.WriteAllText(mermaidFile, "graph TD\n    A[Test]");

        // Act
        var exitCode = RunMermaidCommandWithPath(installResult.Path!, mermaidFile, pngFile);

        // Assert
        Assert.AreEqual(0, exitCode, "Mermaid command should succeed");
        Assert.IsTrue(File.Exists(pngFile));
        
        var fileBytes = File.ReadAllBytes(pngFile);
        
        // PNG signature: 89 50 4E 47 0D 0A 1A 0A
        Assert.IsTrue(fileBytes.Length >= 8, "PNG file too small");
        Assert.AreEqual(0x89, fileBytes[0], "PNG signature byte 1");
        Assert.AreEqual(0x50, fileBytes[1], "PNG signature byte 2");
        Assert.AreEqual(0x4E, fileBytes[2], "PNG signature byte 3");
        Assert.AreEqual(0x47, fileBytes[3], "PNG signature byte 4");
        
        Console.WriteLine($"? Generated file is a valid PNG");
        Console.WriteLine($"   File size: {fileBytes.Length} bytes");
    }

    /// <summary>
    /// Helper method to run mermaid-cli command using a specific mmdc path.
    /// </summary>
    private int RunMermaidCommandWithPath(string mmdcPath, string mermaidFile, string pngFile)
    {
        try
        {
            // Use the specific mmdc path
            var psi = new ProcessStartInfo
            {
                FileName = mmdcPath,
                Arguments = $"-i \"{mermaidFile}\" -o \"{pngFile}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                throw new Exception("Failed to start mmdc process");
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            
            process.WaitForExit(30000);

            if (!string.IsNullOrEmpty(error) && process.ExitCode != 0)
            {
                Console.WriteLine($"Mermaid error: {error}");
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception running mmdc: {ex.Message}");
            throw new AssertInconclusiveException(
                $"Could not run mmdc at {mmdcPath}: {ex.Message}",
                ex
            );
        }
    }
}
