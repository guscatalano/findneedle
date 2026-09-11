using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Services;

/// <summary>
/// Naming a workspace. Before RenameWorkspace existed the name came ONLY from the file name on save or
/// open, so an unsaved workspace was permanently "Untitled workspace" with no way to call it anything.
/// [DoNotParallelize] - MiddleLayerService state is process-wide static.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
[DoNotParallelize]
public class WorkspaceRenameTests
{
    [TestCleanup]
    public void Cleanup() => MiddleLayerService.RenameWorkspace(null);

    [TestMethod]
    public void Rename_SetsTheDisplayName()
    {
        MiddleLayerService.RenameWorkspace("kernel-trace");
        Assert.AreEqual("kernel-trace", MiddleLayerService.WorkspaceName);
        Assert.AreEqual("kernel-trace", MiddleLayerService.WorkspaceDisplayName);
    }

    [TestMethod]
    public void Rename_TrimsSurroundingWhitespace()
    {
        MiddleLayerService.RenameWorkspace("   spaced   ");
        Assert.AreEqual("spaced", MiddleLayerService.WorkspaceName);
    }

    [TestMethod]
    public void Rename_BlankGoesBackToUntitled()
    {
        MiddleLayerService.RenameWorkspace("something");
        MiddleLayerService.RenameWorkspace("   ");
        Assert.IsNull(MiddleLayerService.WorkspaceName);
        Assert.AreEqual("Untitled workspace", MiddleLayerService.WorkspaceDisplayName);
    }

    [TestMethod]
    public void Rename_RaisesStateChanged_SoTheChipRefreshes()
    {
        MiddleLayerService.RenameWorkspace(null);
        int fired = 0;
        void Handler() => fired++;
        MiddleLayerService.StateChanged += Handler;
        try
        {
            MiddleLayerService.RenameWorkspace("named");
            Assert.AreEqual(1, fired, "the chip and status strip listen to StateChanged");

            MiddleLayerService.RenameWorkspace("named"); // same value
            Assert.AreEqual(1, fired, "an unchanged name should not churn every listener");
        }
        finally { MiddleLayerService.StateChanged -= Handler; }
    }
}
