using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedleCoreUtils;

namespace CoreTests;

[TestClass]
public sealed class ByteUtilTests
{

    [TestMethod]
    public void TestByteConversion()
    {
        Assert.AreEqual("0 bytes", ByteUtils.BytesToFriendlyString(0, 0));
        Assert.AreEqual("1 bytes", ByteUtils.BytesToFriendlyString(1, 0));
        Assert.AreEqual("1 KB", ByteUtils.BytesToFriendlyString(1024, 0));
        Assert.AreEqual("1.5 KB", ByteUtils.BytesToFriendlyString(1536));
        Assert.AreEqual("1.5 MB", ByteUtils.BytesToFriendlyString(1572864));
        Assert.AreEqual("1.5 GB", ByteUtils.BytesToFriendlyString(1610612736));
        Assert.AreEqual("1.5 TB", ByteUtils.BytesToFriendlyString(1649267441664));
        Assert.AreEqual("1.5 PB", ByteUtils.BytesToFriendlyString(1688849860263936));
        Assert.AreEqual("1.5 EB", ByteUtils.BytesToFriendlyString(1729382256910270464));
    }
}
