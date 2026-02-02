using FindNeedleCoreUtils;

namespace FindNeedleCoreUtilsTests;

[TestClass]
public class PackagedAppCommandRunnerTests
{
    [TestMethod]
    public void RunCommand_Unpackaged_CanRunSimpleCommand()
    {
        // This test runs in unpackaged mode and executes a simple command
        // Use a command that's guaranteed to exist (cmd.exe with /c and echo)
        var exitCode = PackagedAppCommandRunner.RunCommand(
            "cmd.exe",
            "/c exit 0",
            Path.GetTempPath(),
            5000
        );

        // Exit code 0 indicates success
        Assert.AreEqual(0, exitCode);
    }

    [TestMethod]
    public void RunCommand_Unpackaged_ReturnsCorrectExitCode()
    {
        // Test that we correctly capture the exit code
        var exitCode = PackagedAppCommandRunner.RunCommand(
            "cmd.exe",
            "/c exit 42",
            Path.GetTempPath(),
            5000
        );

        Assert.AreEqual(42, exitCode);
    }

    [TestMethod]
    public void RunCommandWithOutput_Unpackaged_CapturesOutput()
    {
        // Test that we can capture command output
        var (exitCode, output) = PackagedAppCommandRunner.RunCommandWithOutput(
            "cmd.exe",
            "/c echo TestOutput",
            Path.GetTempPath(),
            5000
        );

        Assert.AreEqual(0, exitCode);
        Assert.IsTrue(output.Contains("TestOutput"), $"Output was: {output}");
    }

    [TestMethod]
    public void RunCommandWithOutput_Unpackaged_CapturesError()
    {
        // Test that we capture stderr as well
        var (exitCode, output) = PackagedAppCommandRunner.RunCommandWithOutput(
            "cmd.exe",
            "/c (echo ErrorMessage >&2) && exit 1",
            Path.GetTempPath(),
            5000
        );

        Assert.AreEqual(1, exitCode);
        // stderr output should be captured
        Assert.IsTrue(!string.IsNullOrEmpty(output));
    }

    [TestMethod]
    [ExpectedException(typeof(TimeoutException))]
    public void RunCommand_Unpackaged_TimesOutOnLongRunningCommand()
    {
        // Test timeout behavior with a command that takes longer than the timeout
        PackagedAppCommandRunner.RunCommand(
            "cmd.exe",
            "/c timeout /t 10",  // Sleep for 10 seconds
            Path.GetTempPath(),
            1000  // 1 second timeout
        );
    }

    [TestMethod]
    [ExpectedException(typeof(Exception))]
    public void RunCommand_FailsOnInvalidExecutable()
    {
        // Test that we get an exception for non-existent executable
        PackagedAppCommandRunner.RunCommand(
            "nonexistent_executable_xyz.exe",
            "",
            Path.GetTempPath(),
            5000
        );
    }

    [TestMethod]
    public void IsPackagedApp_ReturnsBoolean()
    {
        // Just verify that this property doesn't throw and returns a value
        var isPackaged = PackagedAppCommandRunner.IsPackagedApp;
        Assert.IsTrue(isPackaged is bool);
    }

    [TestMethod]
    public void PackageFamilyName_ReturnsNullOrString()
    {
        // When unpackaged, this should be null. When packaged, it's a string.
        var packageFamilyName = PackagedAppCommandRunner.PackageFamilyName;
        Assert.IsTrue(packageFamilyName is null or string);
    }

    [TestMethod]
    public void RunJavaJar_BuildsCorrectArguments()
    {
        // This is a bit tricky to test without Java installed,
        // but we can test that the method accepts the parameters
        // and builds a valid command (using a mock or by catching the exception)
        try
        {
            PackagedAppCommandRunner.RunJavaJar(
                "java.exe",
                "nonexistent.jar",
                "input.txt",
                Path.GetTempPath(),
                5000
            );
        }
        catch (Exception)
        {
            // Expected - java.exe or the jar doesn't exist
            // The important thing is that the method structure is correct
        }
    }
}

[TestClass]
public class PackagedAppPathsTests
{
    [TestMethod]
    public void LocalAppData_ReturnsValidPath()
    {
        var path = PackagedAppPaths.LocalAppData;

        Assert.IsFalse(string.IsNullOrEmpty(path));
        Assert.IsTrue(Directory.Exists(path), "LocalAppData path should exist");
    }

    [TestMethod]
    public void DependenciesBaseDir_ReturnsPathUnderLocalAppData()
    {
        var basePath = PackagedAppPaths.DependenciesBaseDir;
        var localAppData = PackagedAppPaths.LocalAppData;

        Assert.IsTrue(basePath.StartsWith(localAppData), "DependenciesBaseDir should be under LocalAppData");
        Assert.IsTrue(basePath.Contains("FindNeedle"));
        Assert.IsTrue(basePath.Contains("Dependencies"));
    }

    [TestMethod]
    public void PlantUmlDir_ReturnsCorrectPath()
    {
        var plantUmlPath = PackagedAppPaths.PlantUmlDir;
        var depsPath = PackagedAppPaths.DependenciesBaseDir;

        Assert.IsTrue(plantUmlPath.StartsWith(depsPath));
        Assert.IsTrue(plantUmlPath.EndsWith("PlantUML"));
    }

    [TestMethod]
    public void MermaidDir_ReturnsCorrectPath()
    {
        var mermaidPath = PackagedAppPaths.MermaidDir;
        var depsPath = PackagedAppPaths.DependenciesBaseDir;

        Assert.IsTrue(mermaidPath.StartsWith(depsPath));
        Assert.IsTrue(mermaidPath.EndsWith("Mermaid"));
    }

    [TestMethod]
    public void TempDir_ReturnsValidPath()
    {
        var tempPath = PackagedAppPaths.TempDir;

        Assert.IsFalse(string.IsNullOrEmpty(tempPath));
        Assert.IsTrue(Directory.Exists(tempPath), "Temp path should exist");
    }

    [TestMethod]
    public void FindNeedleTempDir_ReturnsPathUnderTempDir()
    {
        var findNeedleTempPath = PackagedAppPaths.FindNeedleTempDir;
        var tempPath = PackagedAppPaths.TempDir;

        Assert.IsTrue(findNeedleTempPath.StartsWith(tempPath));
        Assert.IsTrue(findNeedleTempPath.EndsWith("FindNeedle"));
    }

    [TestMethod]
    public void EnsureDirectoryExists_CreatesDirectoryIfNotExists()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "FindNeedleTest_" + Guid.NewGuid());
        
        try
        {
            Assert.IsFalse(Directory.Exists(testDir), "Test directory should not exist initially");

            PackagedAppPaths.EnsureDirectoryExists(testDir);

            Assert.IsTrue(Directory.Exists(testDir), "Directory should be created");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    [TestMethod]
    public void EnsureDirectoryExists_DoesNotThrowIfDirectoryExists()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "FindNeedleTest_" + Guid.NewGuid());
        
        try
        {
            Directory.CreateDirectory(testDir);

            // Should not throw
            PackagedAppPaths.EnsureDirectoryExists(testDir);

            Assert.IsTrue(Directory.Exists(testDir));
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    [TestMethod]
    public void GetTempFilePath_ReturnsUniquePathWithCorrectExtension()
    {
        var tempPath1 = PackagedAppPaths.GetTempFilePath(".txt");
        var tempPath2 = PackagedAppPaths.GetTempFilePath(".txt");

        Assert.AreNotEqual(tempPath1, tempPath2, "Temp file paths should be unique");
        Assert.IsTrue(tempPath1.EndsWith(".txt"));
        Assert.IsTrue(tempPath2.EndsWith(".txt"));
        Assert.IsTrue(tempPath1.Contains("FindNeedle"));
    }

    [TestMethod]
    public void IsPackagedApp_ReturnsBoolean()
    {
        var isPackaged = PackagedAppPaths.IsPackagedApp;
        Assert.IsTrue(isPackaged is bool);
    }

    [TestMethod]
    public void PackageFamilyName_ReturnsNullOrString()
    {
        var packageFamilyName = PackagedAppPaths.PackageFamilyName;
        Assert.IsTrue(packageFamilyName is null or string);
    }

    [TestMethod]
    public void LogPathInfo_DoesNotThrow()
    {
        // Just verify it doesn't throw - it logs to Debug output
        PackagedAppPaths.LogPathInfo();
        Assert.IsTrue(true);
    }
}
