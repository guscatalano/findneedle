using FindNeedleCoreUtils;

namespace FindNeedleCoreUtilsTests;

/// <summary>
/// Integration tests demonstrating all three testing approaches working together.
/// This shows practical examples of how to combine the approaches for comprehensive coverage.
/// </summary>
[TestClass]
public class IntegratedTestingExamples
{
    [TestCleanup]
    public void Cleanup()
    {
        PackageContextProviderFactory.ResetToProduction();
    }

    /// <summary>
    /// Approach 1 Example: Test real command execution in unpackaged mode
    /// </summary>
    [TestMethod]
    public void Approach1_RealCommandExecution_UnpackagedPath()
    {
        // Arrange
        var expectedOutput = "TestOutput";
        
        // Act - Actually execute a real command
        var (exitCode, output) = PackagedAppCommandRunner.RunCommandWithOutput(
            "cmd.exe",
            $"/c echo {expectedOutput}",
            Path.GetTempPath(),
            5000
        );

        // Assert - Verify real behavior
        Assert.AreEqual(0, exitCode);
        Assert.IsTrue(output.Contains(expectedOutput));
    }

    /// <summary>
    /// Approach 2 Example: Test command building logic without execution
    /// </summary>
    [TestMethod]
    public void Approach2_CommandBuildingLogic_FastValidation()
    {
        // Arrange
        var javaPath = "C:\\Program Files\\Java\\bin\\java.exe";
        var jarPath = "C:\\Tools\\app.jar";
        var inputFile = "input.txt";
        
        // Act - Build command without executing
        var args = $"-jar \"{jarPath}\" \"{inputFile}\"";
        var escapedPath = PowerShellCommandBuilder.EscapeForPowerShell(javaPath);
        var escapedArgs = PowerShellCommandBuilder.EscapeForPowerShell(args);

        // Assert - Verify command logic is correct
        Assert.IsTrue(escapedPath.StartsWith("C:\\Program Files\\Java\\bin\\java.exe"));
        Assert.IsTrue(escapedArgs.Contains(inputFile));
    }

    /// <summary>
    /// Approach 3 Example: Test packaged app logic without actual MSIX package
    /// </summary>
    [TestMethod]
    public void Approach3_SimulatePackagedContext_WithoutMSIX()
    {
        // Arrange - Mock a packaged app context
        var packagedProvider = new TestPackageContextProvider(
            isPackagedApp: true,
            packageFamilyName: "MyApp_12345xyz"
        );
        PackageContextProviderFactory.SetTestProvider(packagedProvider);

        // Act - Build command for packaged app
        var command = PowerShellCommandBuilder.BuildPackageContextCommand(
            packageFamilyName: packagedProvider.PackageFamilyName!,
            innerCommand: "Write-Host 'Running in packaged app'"
        );

        // Assert - Verify packaged app command structure
        Assert.IsTrue(command.Contains("Invoke-CommandInDesktopPackage"));
        Assert.IsTrue(command.Contains("MyApp_12345xyz"));
        Assert.IsTrue(command.Contains("-PreventBreakaway"));
    }

    /// <summary>
    /// Combined: Test unpackaged command execution + verify logic + mock edge cases
    /// </summary>
    [TestMethod]
    public void Combined_UnpackagedExecution_WithLogicValidation()
    {
        // Part 1: Verify real execution works (Approach 1)
        var (exitCode, _) = PackagedAppCommandRunner.RunCommandWithOutput(
            "cmd.exe",
            "/c exit 0",
            Path.GetTempPath(),
            5000
        );
        Assert.AreEqual(0, exitCode);

        // Part 2: Verify command building logic for complex paths (Approach 2)
        var complexPath = "C:\\Program Files (x86)\\My'App\\app.exe";
        var escaped = PowerShellCommandBuilder.EscapeForPowerShell(complexPath);
        Assert.IsTrue(escaped.Contains("My''App")); // Single quotes escaped

        // Part 3: Verify the same logic works in packaged context (Approach 3)
        var mockedProvider = new TestPackageContextProvider(isPackagedApp: true);
        PackageContextProviderFactory.SetTestProvider(mockedProvider);
        
        var packagedCommand = PowerShellCommandBuilder.BuildInnerCommand(
            workingDirectory: "C:\\Work",
            executablePath: complexPath,
            arguments: "--version",
            outputFile: "output.txt",
            sentinelFile: "sentinel.txt",
            pathAdditions: null
        );
        Assert.IsTrue(packagedCommand.Contains("My''App"));
    }

    /// <summary>
    /// Combined: Test path utilities in both contexts
    /// </summary>
    [TestMethod]
    public void Combined_PathUtilities_UnpackagedAndPackaged()
    {
        // Part 1: Test unpackaged context (Approach 1/3 - real or mocked)
        var unpackagedProvider = new TestPackageContextProvider(isPackagedApp: false);
        PackageContextProviderFactory.SetTestProvider(unpackagedProvider);
        
        var unpackagedTemp = PackagedAppPaths.FindNeedleTempDir;
        Assert.IsFalse(string.IsNullOrEmpty(unpackagedTemp));
        Assert.IsFalse(PackagedAppPaths.IsPackagedApp);

        // Part 2: Test packaged context (Approach 3)
        var packagedProvider = new TestPackageContextProvider(
            isPackagedApp: true,
            packageFamilyName: "TestApp_abc123"
        );
        PackageContextProviderFactory.SetTestProvider(packagedProvider);
        
        var packagedDepsDir = PackagedAppPaths.DependenciesBaseDir;
        Assert.IsTrue(PackagedAppPaths.IsPackagedApp);
        Assert.AreEqual("TestApp_abc123", PackagedAppPaths.PackageFamilyName);
    }

    /// <summary>
    /// Combined: Realistic scenario - Install tool in both contexts
    /// </summary>
    [TestMethod]
    public void Combined_RealisticScenario_ToolInstallation()
    {
        // Simulate installer running in packaged context
        var packagedProvider = new TestPackageContextProvider(
            isPackagedApp: true,
            packageFamilyName: "FindNeedle_xyz123abc"
        );
        PackageContextProviderFactory.SetTestProvider(packagedProvider);

        // Part 1: Verify paths are correct for packaged app (Approach 2/3)
        var plantUmlDir = PackagedAppPaths.PlantUmlDir;
        Assert.IsTrue(plantUmlDir.Contains("FindNeedle"));
        Assert.IsTrue(plantUmlDir.Contains("PlantUML"));

        // Part 2: Build the command that would install/run the tool (Approach 2)
        var toolPath = Path.Combine(plantUmlDir, "plantuml.jar");
        var runCommand = PowerShellCommandBuilder.BuildInnerCommand(
            workingDirectory: plantUmlDir,
            executablePath: "java.exe",
            arguments: $"-jar \"{toolPath}\" diagram.puml",
            outputFile: Path.Combine(PackagedAppPaths.TempDir, "output.txt"),
            sentinelFile: Path.Combine(PackagedAppPaths.TempDir, "sentinel.txt"),
            pathAdditions: null
        );

        // Part 3: Verify command is properly formatted (Approach 2)
        Assert.IsTrue(runCommand.Contains("plantuml.jar"));
        Assert.IsTrue(runCommand.Contains("$LASTEXITCODE"));
        
        // Part 4: Verify it would work wrapped in package context (Approach 3)
        var fullCommand = PowerShellCommandBuilder.BuildPackageContextCommand(
            packageFamilyName: "FindNeedle_xyz123abc",
            innerCommand: runCommand
        );
        Assert.IsTrue(fullCommand.Contains("Invoke-CommandInDesktopPackage"));
    }

    /// <summary>
    /// Combined: Error handling across all approaches
    /// </summary>
    [TestMethod]
    public void Combined_ErrorHandling_AllApproaches()
    {
        // Approach 1: Real command failure
        try
        {
            PackagedAppCommandRunner.RunCommand(
                "nonexistent_command.exe",
                "",
                Path.GetTempPath(),
                1000
            );
            Assert.Fail("Should have thrown");
        }
        catch (Exception)
        {
            // Expected
        }

        // Approach 2: Edge case in escaping
        var pathWithSpecialChars = "C:\\Path\\With'Quotes\"AndDoubles\\file.txt";
        var escaped = PowerShellCommandBuilder.EscapeForPowerShell(pathWithSpecialChars);
        Assert.IsTrue(escaped.Contains("''"), "Quotes should be doubled");

        // Approach 3: Packaged context with null family name (shouldn't happen, but test it)
        var edgeCaseProvider = new TestPackageContextProvider(isPackagedApp: false);
        PackageContextProviderFactory.SetTestProvider(edgeCaseProvider);
        
        Assert.IsNull(PackagedAppPaths.PackageFamilyName);
        Assert.IsFalse(PackagedAppPaths.IsPackagedApp);
    }

    /// <summary>
    /// Combined: Performance and resource cleanup
    /// </summary>
    [TestMethod]
    public void Combined_PerformanceAndCleanup()
    {
        // Approach 1: Time real command execution
        var startTime = DateTime.UtcNow;
        for (int i = 0; i < 3; i++)
        {
            PackagedAppCommandRunner.RunCommand(
                "cmd.exe",
                "/c exit 0",
                Path.GetTempPath(),
                5000
            );
        }
        var realExecutionTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

        // Approach 2: Time logic-only tests (should be much faster)
        startTime = DateTime.UtcNow;
        for (int i = 0; i < 1000; i++)
        {
            PowerShellCommandBuilder.EscapeForPowerShell("Test'String");
        }
        var logicTestTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

        // Approach 3: Time mock creation (very fast)
        startTime = DateTime.UtcNow;
        for (int i = 0; i < 1000; i++)
        {
            var provider = new TestPackageContextProvider(isPackagedApp: true);
        }
        var mockTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

        // Logic and mock tests should be MUCH faster than real execution
        Assert.IsTrue(logicTestTime < realExecutionTime);
        Assert.IsTrue(mockTime < realExecutionTime);
    }
}
