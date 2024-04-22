using findneedle;
using findneedle.Implementations;

namespace findneedletests
{
    [TestClass]
    public class SearchArgsTests
    {
        [TestMethod]
        public void TestBadInput()
        {
            Dictionary<string, string> input = new Dictionary<string, string>();
            input.Add("", "");
            SearchQuery q = new SearchQuery(input);
            Assert.IsTrue(q.GetLocations().Count == 0);
        }

        [TestMethod]
        public void TestLocalEventLog()
        {
            Dictionary<string, string> input = new Dictionary<string, string>();
            input.Add("location", "localeventlog");
            SearchQuery q = new SearchQuery(input);
            Assert.IsTrue(q.GetLocations().Count == 1);
            Assert.IsTrue(q.GetLocations()[0].GetType() == typeof(LocalEventLogLocation));
        }

        [TestMethod]
        public void TestLocalFolderLog()
        {
            Dictionary<string, string> input = new Dictionary<string, string>();
            input.Add("location", "path#C:\\windows");
            SearchQuery q = new SearchQuery(input);
            Assert.IsTrue(q.GetLocations().Count == 1);
            Assert.IsTrue(q.GetLocations()[0].GetType() == typeof(FolderLocation));
        }

        [TestMethod]
        public void TestBadLocalFolderLog()
        {
            Dictionary<string, string> input = new Dictionary<string, string>();
            input.Add("location", "path#C;windows");
            try
            {
                SearchQuery q = new SearchQuery(input);

            }
            catch (Exception)
            {
                Assert.IsTrue(true); //We expect to throw
                return;
            }
            Assert.IsFalse(true);

        }

        [TestMethod]
        public void TestRealFileLog()
        {
            Dictionary<string, string> input = new Dictionary<string, string>();
            input.Add("location", @"path#C:\\windows\\explorer.exe");

            SearchQuery q = new SearchQuery(input);
            Assert.IsTrue(q.GetLocations().Count == 1);
            Assert.IsTrue(q.GetLocations()[0].GetType() == typeof(FolderLocation));
        }

        [TestMethod]
        public void TestAddMultiplelFileLog()
        {
            Dictionary<string, string> input = new Dictionary<string, string>
            {
                { "location1", @"path#C:\\windows\\explorer.exe" },
                { "location2", @"path#C:\\windows\\system32" },
                { "location3", @"path#C:\\windows\\system32\\" }
            };

            SearchQuery q = new SearchQuery(input);


            Assert.AreEqual(3, q.GetLocations().Count);

        }

        [TestMethod]
        public void TestTimeSpanFilter()
        {
            Dictionary<string, string> input = new Dictionary<string, string>();
            input.Add("searchfilter", "time(2022-01-01 05:00:00Z, 2023-01-01 05:00:00Z)");

            SearchQuery q = new SearchQuery(input);
            Assert.IsTrue(q.GetFilters().Count == 1);
        }

        [TestMethod]
        public void TestTimeAgoFilter()
        {
            Dictionary<string, string> input = new Dictionary<string, string>();
            input.Add("searchfilter", "ago(2h)");

            SearchQuery q = new SearchQuery(input);
            Assert.IsTrue(q.GetFilters().Count == 1);
        }

        [TestMethod]
        public void TestKeywordFilter()
        {
            Dictionary<string, string> input = new Dictionary<string, string>();
            input.Add("keyword", "potato");

            SearchQuery q = new SearchQuery(input);
            Assert.IsTrue(q.GetFilters().Count == 1);
        }
    }
}