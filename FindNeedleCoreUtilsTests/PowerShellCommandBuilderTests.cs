using FindNeedleCoreUtils;

namespace FindNeedleCoreUtilsTests;

[TestClass]
public class PowerShellCommandBuilderTests
{
    [TestMethod]
    public void EscapeForPowerShell_SingleQuotes_AreDoubled()
    {
        var input = "C:\\Program Files\\My'App\\file.txt";
        var result = PowerShellCommandBuilder.EscapeForPowerShell(input);

        Assert.AreEqual("C:\\Program Files\\My''App\\file.txt", result);
    }

    [TestMethod]
    public void EscapeForPowerShell_NoQuotes_Unchanged()
    {
        var input = "C:\\Program Files\\MyApp\\file.txt";
        var result = PowerShellCommandBuilder.EscapeForPowerShell(input);

        Assert.AreEqual(input, result);
    }

    [TestMethod]
    public void EscapeForPowerShell_MultipleSingleQuotes_AllDoubled()
    {
        var input = "It's a 'test' string";
        var result = PowerShellCommandBuilder.EscapeForPowerShell(input);

        Assert.AreEqual("It''s a ''test'' string", result);
    }

    [TestMethod]
    public void EscapeForPowerShell_EmptyString_ReturnsEmpty()
    {
        var result = PowerShellCommandBuilder.EscapeForPowerShell("");
        Assert.AreEqual("", result);
    }

    [TestMethod]
    public void BuildPathSetupCommand_NoPathAdditions_ReturnsEmpty()
    {
        var result = PowerShellCommandBuilder.BuildPathSetupCommand(null);
        Assert.AreEqual("", result);

        result = PowerShellCommandBuilder.BuildPathSetupCommand(Array.Empty<string>());
        Assert.AreEqual("", result);
    }

    [TestMethod]
    public void BuildPathSetupCommand_SinglePath_ReturnsCorrectFormat()
    {
        var paths = new[] { "C:\\NodeJS" };
        var result = PowerShellCommandBuilder.BuildPathSetupCommand(paths);

        Assert.IsTrue(result.Contains("$env:PATH"));
        Assert.IsTrue(result.Contains("C:\\NodeJS"));
        Assert.IsTrue(result.EndsWith("; "));
    }

    [TestMethod]
    public void BuildPathSetupCommand_MultiplePaths_CombinedWithSemicolon()
    {
        var paths = new[] { "C:\\NodeJS", "C:\\Python" };
        var result = PowerShellCommandBuilder.BuildPathSetupCommand(paths);

        Assert.IsTrue(result.Contains("C:\\NodeJS;C:\\Python"));
    }

    [TestMethod]
    public void BuildPathSetupCommand_PathWithQuotes_Escaped()
    {
        var paths = new[] { "C:\\My'Path\\NodeJS" };
        var result = PowerShellCommandBuilder.BuildPathSetupCommand(paths);

        // Single quotes should be doubled
        Assert.IsTrue(result.Contains("My''Path"));
    }

    [TestMethod]
    public void BuildInnerCommand_ReturnsValidPowerShellSyntax()
    {
        var innerCmd = PowerShellCommandBuilder.BuildInnerCommand(
            workingDirectory: "C:\\Work",
            executablePath: "C:\\Program Files\\app.exe",
            arguments: "--version",
            outputFile: "C:\\Temp\\output.txt",
            sentinelFile: "C:\\Temp\\sentinel.txt",
            pathAdditions: null
        );

        Assert.IsTrue(innerCmd.Contains("Set-Location"));
        Assert.IsTrue(innerCmd.Contains("&"));
        Assert.IsTrue(innerCmd.Contains("$LASTEXITCODE"));
        Assert.IsTrue(innerCmd.Contains("Out-File"));
    }

    [TestMethod]
    public void BuildInnerCommand_IncludesWorkingDirectory()
    {
        var innerCmd = PowerShellCommandBuilder.BuildInnerCommand(
            workingDirectory: "C:\\MyWork",
            executablePath: "app.exe",
            arguments: "",
            outputFile: "output.txt",
            sentinelFile: "sentinel.txt",
            pathAdditions: null
        );

        Assert.IsTrue(innerCmd.Contains("C:\\MyWork"));
    }

    [TestMethod]
    public void BuildInnerCommand_IncludesExecutableAndArguments()
    {
        var innerCmd = PowerShellCommandBuilder.BuildInnerCommand(
            workingDirectory: "C:\\Work",
            executablePath: "myapp.exe",
            arguments: "--verbose --output result.txt",
            outputFile: "output.txt",
            sentinelFile: "sentinel.txt",
            pathAdditions: null
        );

        Assert.IsTrue(innerCmd.Contains("myapp.exe"));
        Assert.IsTrue(innerCmd.Contains("--verbose --output result.txt"));
    }

    [TestMethod]
    public void BuildInnerCommand_WithPathAdditions_IncludesPathSetup()
    {
        var innerCmd = PowerShellCommandBuilder.BuildInnerCommand(
            workingDirectory: "C:\\Work",
            executablePath: "app.exe",
            arguments: "",
            outputFile: "output.txt",
            sentinelFile: "sentinel.txt",
            pathAdditions: new[] { "C:\\NodeJS" }
        );

        Assert.IsTrue(innerCmd.Contains("$env:PATH"));
        Assert.IsTrue(innerCmd.Contains("C:\\NodeJS"));
    }

    [TestMethod]
    public void BuildInnerCommand_OutputAndSentinelPaths_Included()
    {
        var innerCmd = PowerShellCommandBuilder.BuildInnerCommand(
            workingDirectory: "C:\\Work",
            executablePath: "app.exe",
            arguments: "",
            outputFile: "C:\\Temp\\my_output.txt",
            sentinelFile: "C:\\Temp\\my_sentinel.txt",
            pathAdditions: null
        );

        Assert.IsTrue(innerCmd.Contains("C:\\Temp\\my_output.txt"));
        Assert.IsTrue(innerCmd.Contains("C:\\Temp\\my_sentinel.txt"));
    }

    [TestMethod]
    public void BuildPackageContextCommand_ReturnsInvokeCommand()
    {
        var result = PowerShellCommandBuilder.BuildPackageContextCommand(
            packageFamilyName: "MyApp_abc123",
            innerCommand: "Write-Host 'test'"
        );

        Assert.IsTrue(result.Contains("Invoke-CommandInDesktopPackage"));
        Assert.IsTrue(result.Contains("MyApp_abc123"));
        Assert.IsTrue(result.Contains("powershell.exe"));
        Assert.IsTrue(result.Contains("-PreventBreakaway"));
    }

    [TestMethod]
    public void BuildPackageContextCommand_PackageFamilyNameEscaped()
    {
        var result = PowerShellCommandBuilder.BuildPackageContextCommand(
            packageFamilyName: "My'App_abc123",
            innerCommand: "Write-Host 'test'"
        );

        // Single quotes in package family name should be doubled
        Assert.IsTrue(result.Contains("My''App_abc123"));
    }

    [TestMethod]
    public void BuildPackageContextCommand_InnerCommandIncluded()
    {
        var innerCmd = "Set-Location 'C:\\Work'; Write-Host 'Done'";
        var result = PowerShellCommandBuilder.BuildPackageContextCommand(
            packageFamilyName: "MyApp",
            innerCommand: innerCmd
        );

        Assert.IsTrue(result.Contains(innerCmd));
    }

    [TestMethod]
    public void BuildPackageContextCommand_HasCorrectCommandAndAppId()
    {
        var result = PowerShellCommandBuilder.BuildPackageContextCommand(
            packageFamilyName: "MyApp",
            innerCommand: "test"
        );

        Assert.IsTrue(result.Contains("-Command 'powershell.exe'"));
        Assert.IsTrue(result.Contains("-AppId 'App'"));
    }
}
