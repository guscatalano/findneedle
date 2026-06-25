using System.Collections.Generic;
using System.Linq;
using FindNeedleUX.Services;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using findneedle.Implementations;
using FindPluginCore.Searching.AutoRules;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Services;

/// <summary>
/// Tests for <see cref="MiddleLayerService.BuildAutoRuleContext"/> — it inspects the loaded locations
/// (by type and file extension) and reports the source kinds present, which drives which auto-rules
/// apply to a search. Uses real FolderLocation instances (their GetName returns the path).
/// </summary>
[TestClass]
[TestCategory("Services")]
public class BuildAutoRuleContextTests
{
    [TestMethod]
    public void DetectsSourceKinds_ByExtension_AndFolder()
    {
        var locations = new List<ISearchLocation>
        {
            new FolderLocation { path = @"C:\logs\boot.etl" },
            new FolderLocation { path = @"C:\logs\System.evtx" },
            new FolderLocation { path = @"C:\bundle\logs.zip" },
        };

        var ctx = MiddleLayerService.BuildAutoRuleContext(locations);

        Assert.IsTrue(ctx.SourceTypes.Contains(AutoRuleSourceKinds.Etw), "an .etl should register ETW");
        Assert.IsTrue(ctx.SourceTypes.Contains(AutoRuleSourceKinds.EventLog), "an .evtx should register Event Log");
        Assert.IsTrue(ctx.SourceTypes.Contains(AutoRuleSourceKinds.Zip), "a .zip should register Zip");
        Assert.IsTrue(ctx.SourceTypes.Contains(AutoRuleSourceKinds.Folder), "folder locations register Folder");
        Assert.IsTrue(ctx.Paths.Any(p => p.EndsWith("boot.etl")), "the location path is recorded");
    }

    [TestMethod]
    public void NullLocations_ReturnsEmptyContext()
    {
        var ctx = MiddleLayerService.BuildAutoRuleContext(null);
        Assert.IsNotNull(ctx);
        Assert.AreEqual(0, ctx.Paths.Count);
    }
}
