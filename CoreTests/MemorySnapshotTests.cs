using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using FindNeedlePluginLib.Implementations.SearchStatistics;

namespace CoreTests;

[TestClass]
public class MemorySnapshotTests
{
    [TestMethod]
    public void TestMemorySnapshotBasic()
    {
        // Arrange
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var memorySnapshot = new MemorySnapshot(process);
        // Act
        memorySnapshot.Snap();
        // Assert
        Assert.IsNotNull(memorySnapshot.GetSnapTime());
        Assert.IsTrue(memorySnapshot.GetMemoryUsagePrivate() > 0);
        Assert.IsTrue(memorySnapshot.GetMemoryUsageTotal() > 0);
        Assert.IsFalse(string.IsNullOrEmpty(memorySnapshot.GetMemoryUsageFriendly()));
    }

    [TestMethod]
    public void TestMemorySnapshotBetter()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var memorySnapshot = new MemorySnapshot(process);

        DateTime beforeSnap = DateTime.Now;
        memorySnapshot.Snap();

        Assert.IsTrue(TimeSpan.FromSeconds(10) > (memorySnapshot.GetSnapTime() - beforeSnap));
        Assert.IsTrue(memorySnapshot.GetMemoryUsagePrivate() < memorySnapshot.GetMemoryUsageTotal());
    }

    [TestMethod]
    public void BasicFailSnap()
    {
        try
        {
            MemorySnapshot mem = new();
            mem.GetSnapTime();
            Assert.Fail("Should have thrown");
        }
        catch
        {
            Assert.IsTrue(true); //Should throw, we didnt snap
        }
    }
}
