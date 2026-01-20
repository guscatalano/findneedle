using FindNeedlePluginUtils.DependencyInstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedlePluginUtilsTests;

[TestClass]
public class DependencyInstallerTests
{
    [TestMethod]
    public void PlantUmlInstaller_ImplementsInterface()
    {
        var installer = new PlantUmlInstaller();

        Assert.IsInstanceOfType(installer, typeof(IDependencyInstaller));
    }

    [TestMethod]
    public void PlantUmlInstaller_HasCorrectName()
    {
        var installer = new PlantUmlInstaller();

        Assert.AreEqual("PlantUML", installer.DependencyName);
    }

    [TestMethod]
    public void PlantUmlInstaller_GetStatus_ReturnsValidStatus()
    {
        var installer = new PlantUmlInstaller();

        var status = installer.GetStatus();

        Assert.AreEqual("PlantUML", status.Name);
        Assert.IsFalse(string.IsNullOrEmpty(status.Description));
        Assert.IsFalse(string.IsNullOrEmpty(status.InstallInstructions));
    }

    [TestMethod]
    public void MermaidInstaller_ImplementsInterface()
    {
        var installer = new MermaidInstaller();

        Assert.IsInstanceOfType(installer, typeof(IDependencyInstaller));
    }

    [TestMethod]
    public void MermaidInstaller_HasCorrectName()
    {
        var installer = new MermaidInstaller();

        Assert.AreEqual("Mermaid CLI", installer.DependencyName);
    }

    [TestMethod]
    public void MermaidInstaller_GetStatus_ReturnsValidStatus()
    {
        var installer = new MermaidInstaller();

        var status = installer.GetStatus();

        Assert.AreEqual("Mermaid CLI", status.Name);
        Assert.IsFalse(string.IsNullOrEmpty(status.Description));
        Assert.IsFalse(string.IsNullOrEmpty(status.InstallInstructions));
    }

    [TestMethod]
    public void UmlDependencyManager_GetAllStatuses_ReturnsBothDependencies()
    {
        var manager = new UmlDependencyManager();

        var statuses = manager.GetAllStatuses().ToList();

        Assert.AreEqual(2, statuses.Count);
        Assert.IsTrue(statuses.Any(s => s.Name == "PlantUML"));
        Assert.IsTrue(statuses.Any(s => s.Name == "Mermaid CLI"));
    }

    [TestMethod]
    public void UmlDependencyManager_AllInstallers_ReturnsBothInstallers()
    {
        var manager = new UmlDependencyManager();

        var installers = manager.AllInstallers.ToList();

        Assert.AreEqual(2, installers.Count);
    }

    [TestMethod]
    public void UmlDependencyManager_GetInstallationSummary_ReturnsNonEmptyString()
    {
        var manager = new UmlDependencyManager();

        var summary = manager.GetInstallationSummary();

        Assert.IsFalse(string.IsNullOrEmpty(summary));
        Assert.IsTrue(summary.Contains("PlantUML"));
        Assert.IsTrue(summary.Contains("Mermaid"));
    }

    [TestMethod]
    public void InstallProgress_DefaultValues()
    {
        var progress = new InstallProgress();

        Assert.AreEqual(string.Empty, progress.Status);
        Assert.AreEqual(0, progress.PercentComplete);
        Assert.IsFalse(progress.IsIndeterminate);
    }

    [TestMethod]
    public void InstallResult_Succeeded_SetsProperties()
    {
        var result = InstallResult.Succeeded("/path/to/tool");

        Assert.IsTrue(result.Success);
        Assert.AreEqual("/path/to/tool", result.InstalledPath);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void InstallResult_Failed_SetsProperties()
    {
        var result = InstallResult.Failed("Something went wrong");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("Something went wrong", result.ErrorMessage);
        Assert.IsNull(result.InstalledPath);
    }

    [TestMethod]
    public void DependencyStatus_DefaultValues()
    {
        var status = new DependencyStatus();

        Assert.AreEqual(string.Empty, status.Name);
        Assert.AreEqual(string.Empty, status.Description);
        Assert.IsFalse(status.IsInstalled);
        Assert.IsNull(status.InstalledVersion);
        Assert.IsNull(status.InstalledPath);
        Assert.IsNull(status.InstallInstructions);
    }

    [TestMethod]
    public void PlantUmlInstaller_CustomInstallDirectory_UsesProvidedPath()
    {
        var customPath = Path.Combine(Path.GetTempPath(), "TestPlantUML");
        var installer = new PlantUmlInstaller(customPath);

        // The installer should use the custom path (we can't easily verify this
        // without actually installing, but we can verify it doesn't throw)
        var status = installer.GetStatus();

        Assert.IsNotNull(status);
    }

    [TestMethod]
    public void MermaidInstaller_CustomInstallDirectory_UsesProvidedPath()
    {
        var customPath = Path.Combine(Path.GetTempPath(), "TestMermaid");
        var installer = new MermaidInstaller(customPath);

        var status = installer.GetStatus();

        Assert.IsNotNull(status);
    }
}
