using System;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;

namespace FindNeedleUXTests.Services;

/// <summary>
/// Verifies <see cref="FileIntegration"/> writes and removes the per-user (HKCU) Explorer-integration
/// keys. The test host runs unpackaged, so the writes are real — each test cleans up in its finally.
/// </summary>
[TestClass]
public class FileIntegrationTests
{
    [TestMethod]
    public void OpenWith_RegisterThenUnregister_TogglesProgId()
    {
        if (FileIntegration.IsPackaged) { Assert.Inconclusive("packaged host virtualizes the registry"); return; }
        try
        {
            FileIntegration.SetOpenWith(true);
            using (var prog = Registry.CurrentUser.OpenSubKey(@"Software\Classes\FindNeedle.LogFile"))
                Assert.IsNotNull(prog, "ProgId key should exist after register");
            using (var ow = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.etl\OpenWithProgids"))
            {
                Assert.IsNotNull(ow, "OpenWithProgids should exist for .etl");
                Assert.IsTrue(Array.IndexOf(ow.GetValueNames(), "FindNeedle.LogFile") >= 0,
                    ".etl should list the FindNeedle ProgId");
            }
        }
        finally { FileIntegration.SetOpenWith(false); }

        using (var prog = Registry.CurrentUser.OpenSubKey(@"Software\Classes\FindNeedle.LogFile"))
            Assert.IsNull(prog, "ProgId key should be gone after unregister");
    }

    [TestMethod]
    public void ContextMenu_RegisterThenUnregister_TogglesVerb()
    {
        if (FileIntegration.IsPackaged) { Assert.Inconclusive("packaged host virtualizes the registry"); return; }
        try
        {
            FileIntegration.SetContextMenu(true);
            using var cmd = Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\SystemFileAssociations\.etl\shell\OpenInFindNeedle\command");
            Assert.IsNotNull(cmd, "the verb command should exist after register");
            Assert.IsTrue(((cmd.GetValue("") as string) ?? "").Contains("%1"),
                "the command should pass the file path");
        }
        finally { FileIntegration.SetContextMenu(false); }

        using (var verb = Registry.CurrentUser.OpenSubKey(
            @"Software\Classes\SystemFileAssociations\.etl\shell\OpenInFindNeedle"))
            Assert.IsNull(verb, "the verb should be gone after unregister");
    }
}
