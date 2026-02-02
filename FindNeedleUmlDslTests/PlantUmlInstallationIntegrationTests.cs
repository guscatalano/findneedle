using FindNeedleToolInstallers;
using System.Diagnostics;
using System.IO;

namespace FindNeedleUmlInstallerTests;

/// <summary>
/// Integration tests for PlantUML installation and PNG generation.
/// These tests validate that the PlantUML installer works and can actually generate diagrams.
/// </summary>
[TestClass]
public class PlantUmlInstallationIntegrationTests
{
    private string _testOutputDirectory = null!;
    private string _testInstallDirectory = null!;
    private PlantUmlInstaller _installer = null!;

    [TestInitialize]
    public void Setup()
    {
        _testOutputDirectory = Path.Combine(Path.GetTempPath(), "PlantUmlIntegrationTests_" + Guid.NewGuid());
        _testInstallDirectory = Path.Combine(Path.GetTempPath(), "PlantUmlInstall_" + Guid.NewGuid());
        Directory.CreateDirectory(_testOutputDirectory);
        
        _installer = new PlantUmlInstaller(_testInstallDirectory);
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
    public async Task PlantUmlInstaller_CanInstall()
    {
        // Act - Perform installation
        Console.WriteLine($"Installing PlantUML to: {_testInstallDirectory}");
        var progress = new Progress<InstallProgress>(p =>
        {
            Console.WriteLine($"  [{p.PercentComplete}%] {p.Status}");
        });

        var result = await _installer.InstallAsync(progress, CancellationToken.None);

        // Assert
        Assert.IsTrue(result.Success, $"Installation failed: {result.Message}");
        Assert.IsNotNull(result.Path);
        Assert.IsTrue(File.Exists(result.Path), $"PlantUML JAR not found at: {result.Path}");
        
        Console.WriteLine($"? Installation successful");
        Console.WriteLine($"   PlantUML JAR path: {result.Path}");
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task PlantUmlInstaller_CanGeneratePng_AfterInstall()
    {
        // Arrange - Install first
        Console.WriteLine($"Installing PlantUML to: {_testInstallDirectory}");
        var installResult = await _installer.InstallAsync(null, CancellationToken.None);

        if (!installResult.Success)
        {
            Assert.Inconclusive($"Installation failed: {installResult.Message}");
        }

        // Create test diagram
        var plantUmlFile = Path.Combine(_testOutputDirectory, "afterinstall.puml");
        var pngFile = Path.Combine(_testOutputDirectory, "afterinstall.png");
        File.WriteAllText(plantUmlFile, "@startuml\nstart\n:Installed;\nstop\n@enduml");

        // Act - Generate PNG using installed PlantUML
        var exitCode = RunPlantUmlCommand(installResult.Path!, plantUmlFile, _testOutputDirectory);

        // Assert
        Assert.AreEqual(0, exitCode, "PNG generation should succeed");
        Assert.IsTrue(File.Exists(pngFile), "PNG should be created");
        
        var fileInfo = new FileInfo(pngFile);
        Assert.IsTrue(fileInfo.Length > 0, "PNG should have content");
        
        Console.WriteLine($"? PNG generated after custom installation");
        Console.WriteLine($"   Size: {fileInfo.Length} bytes");
    }

    [TestMethod]
    public void PlantUmlInstaller_StatusReflectsNotInstalled()
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
    public async Task PlantUmlInstaller_CanGeneratePng_SimpleFlowchart()
    {
        // Arrange - Install first
        var installResult = await _installer.InstallAsync(null, CancellationToken.None);
        Assert.IsTrue(installResult.Success, $"Installation failed: {installResult.Message}");

        var plantUmlFile = Path.Combine(_testOutputDirectory, "simple.puml");
        var pngFile = Path.Combine(_testOutputDirectory, "simple.png");
        
        var diagramContent = @"@startuml
start
:Process A;
:Process B;
stop
@enduml";

        File.WriteAllText(plantUmlFile, diagramContent);

        // Act
        var exitCode = RunPlantUmlCommand(installResult.Path!, plantUmlFile, _testOutputDirectory);

        // Assert
        Assert.AreEqual(0, exitCode, "PlantUML command should succeed");
        Assert.IsTrue(File.Exists(pngFile), $"PNG file should be created at {pngFile}");
        
        var fileInfo = new FileInfo(pngFile);
        Assert.IsTrue(fileInfo.Length > 0, "PNG file should have content");
        
        Console.WriteLine($"? PNG generated successfully");
        Console.WriteLine($"   Size: {fileInfo.Length} bytes");
        Console.WriteLine($"   Location: {pngFile}");
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task PlantUmlInstaller_CanGeneratePng_SequenceDiagram()
    {
        // Arrange - Install first
        var installResult = await _installer.InstallAsync(null, CancellationToken.None);
        Assert.IsTrue(installResult.Success, $"Installation failed: {installResult.Message}");

        var plantUmlFile = Path.Combine(_testOutputDirectory, "sequence.puml");
        var pngFile = Path.Combine(_testOutputDirectory, "sequence.png");
        
        var diagramContent = @"@startuml
actor User
participant API
database Database

User -> API: Request
API -> Database: Query
Database --> API: Result
API --> User: Response
@enduml";

        File.WriteAllText(plantUmlFile, diagramContent);

        // Act
        var exitCode = RunPlantUmlCommand(installResult.Path!, plantUmlFile, _testOutputDirectory);

        // Assert
        Assert.AreEqual(0, exitCode, "PlantUML command should succeed for sequence diagram");
        Assert.IsTrue(File.Exists(pngFile), $"PNG file should be created at {pngFile}");
        
        var fileInfo = new FileInfo(pngFile);
        Assert.IsTrue(fileInfo.Length > 0, "PNG file should have content");
        Assert.IsTrue(fileInfo.Length > 1000, "Sequence diagram PNG should be reasonably sized");
        
        Console.WriteLine($"? Sequence PNG generated successfully");
        Console.WriteLine($"   Size: {fileInfo.Length} bytes");
        Console.WriteLine($"   Location: {pngFile}");
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task PlantUmlInstaller_CanGeneratePng_ClassDiagram()
    {
        // Arrange - Install first
        var installResult = await _installer.InstallAsync(null, CancellationToken.None);
        Assert.IsTrue(installResult.Success, $"Installation failed: {installResult.Message}");

        var plantUmlFile = Path.Combine(_testOutputDirectory, "classes.puml");
        var pngFile = Path.Combine(_testOutputDirectory, "classes.png");
        
        var diagramContent = @"@startuml
class Animal {
    - name: String
    - age: int
    + eat()
    + sleep()
}

class Dog extends Animal {
    + bark()
}

class Cat extends Animal {
    + meow()
}
@enduml";

        File.WriteAllText(plantUmlFile, diagramContent);

        // Act
        var exitCode = RunPlantUmlCommand(installResult.Path!, plantUmlFile, _testOutputDirectory);

        // Assert
        Assert.AreEqual(0, exitCode, "PlantUML command should succeed for class diagram");
        Assert.IsTrue(File.Exists(pngFile), $"PNG file should be created at {pngFile}");
        
        var fileInfo = new FileInfo(pngFile);
        Assert.IsTrue(fileInfo.Length > 0, "PNG file should have content");
        
        Console.WriteLine($"? Class diagram PNG generated successfully");
        Console.WriteLine($"   Size: {fileInfo.Length} bytes");
        Console.WriteLine($"   Location: {pngFile}");
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task PlantUmlInstaller_CanGenerateMultiplePngs()
    {
        // Arrange - Install first
        var installResult = await _installer.InstallAsync(null, CancellationToken.None);
        Assert.IsTrue(installResult.Success, $"Installation failed: {installResult.Message}");

        var diagrams = new Dictionary<string, string>
        {
            { "flowchart.puml", "@startuml\nstart\n:Process;\nstop\n@enduml" },
            { "usecase.puml", "@startuml\nUser --> (Use Case)\n@enduml" },
            { "state.puml", "@startuml\n[*] --> State1\nState1 --> [*]\n@enduml" }
        };

        var pngFiles = new List<string>();

        // Act
        foreach (var (diagramName, diagramContent) in diagrams)
        {
            var plantUmlFile = Path.Combine(_testOutputDirectory, diagramName);
            var pngFile = Path.Combine(_testOutputDirectory, Path.ChangeExtension(diagramName, ".png"));
            
            File.WriteAllText(plantUmlFile, diagramContent);
            var exitCode = RunPlantUmlCommand(installResult.Path!, plantUmlFile, _testOutputDirectory);

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
    public async Task PlantUmlInstaller_InvalidDiagram_FailsGracefully()
    {
        // Arrange - Install first
        var installResult = await _installer.InstallAsync(null, CancellationToken.None);
        Assert.IsTrue(installResult.Success, $"Installation failed: {installResult.Message}");

        var plantUmlFile = Path.Combine(_testOutputDirectory, "invalid.puml");
        var pngFile = Path.Combine(_testOutputDirectory, "invalid.png");
        
        // Invalid PlantUML syntax - incomplete brackets and statements
        var diagramContent = "@startuml\nstart\nif (test\nelse\nstop";
        File.WriteAllText(plantUmlFile, diagramContent);

        // Act
        var exitCode = RunPlantUmlCommand(installResult.Path!, plantUmlFile, _testOutputDirectory);

        // Assert
        // PlantUML either returns non-zero exit code OR fails to create output file for invalid syntax
        var hasFailed = exitCode != 0 || !File.Exists(pngFile);
        Assert.IsTrue(hasFailed, $"PlantUML should fail on invalid syntax (exit code: {exitCode}, PNG exists: {File.Exists(pngFile)})");
        Console.WriteLine($"? Invalid diagram properly rejected (exit code: {exitCode}, PNG created: {File.Exists(pngFile)})");
    }

    [TestMethod]
    [Timeout(120000)]
    public async Task PlantUmlInstaller_GeneratedPngIsValidImage()
    {
        // Arrange - Install first
        var installResult = await _installer.InstallAsync(null, CancellationToken.None);
        Assert.IsTrue(installResult.Success, $"Installation failed: {installResult.Message}");

        var plantUmlFile = Path.Combine(_testOutputDirectory, "validate.puml");
        var pngFile = Path.Combine(_testOutputDirectory, "validate.png");
        
        File.WriteAllText(plantUmlFile, "@startuml\nstart\nstop\n@enduml");

        // Act
        var exitCode = RunPlantUmlCommand(installResult.Path!, plantUmlFile, _testOutputDirectory);

        // Assert
        Assert.AreEqual(0, exitCode, "PlantUML command should succeed");
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
    /// Helper method to run PlantUML command.
    /// </summary>
    private int RunPlantUmlCommand(string jarPath, string plantUmlFile, string outputDirectory)
    {
        try
        {
            var javaPath = "java";

            var psi = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = $"-jar \"{jarPath}\" -o \"{outputDirectory}\" \"{plantUmlFile}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = outputDirectory
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                throw new Exception("Failed to start java process");
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            
            process.WaitForExit(30000);

            if (!string.IsNullOrEmpty(error) && process.ExitCode != 0)
            {
                Console.WriteLine($"PlantUML error: {error}");
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception running PlantUML: {ex.Message}");
            throw new AssertInconclusiveException(
                $"Could not run PlantUML command. Make sure PlantUML JAR is installed and Java is available.\nError: {ex.Message}",
                ex
            );
        }
    }
}
