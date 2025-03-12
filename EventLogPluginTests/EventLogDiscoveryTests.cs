using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Implementations.Discovery;

namespace EventLogPluginTests
{
    [TestClass]
    public class EventLogDiscoveryTests
    {

        [TestMethod]
        public void TestListing()
        {
            var ret = EventLogDiscovery.GetAllEventLogs();
            Assert.IsTrue(ret.Count > 0);
        }

        [TestMethod]
        public void TestListingSorted()
        {
            var ret = EventLogDiscovery.GetAllEventLogs();
            Assert.IsTrue(ret.Count > 2);
            var sorted = new List<string>(ret);
            sorted.Sort(); 
            //They should be the same :)
            CollectionAssert.AreEqual(ret.ToList(), sorted.ToList());
        }
    }
}
