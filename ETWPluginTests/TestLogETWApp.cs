using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.IO;

namespace ETWPluginTests;

[TestClass]
public class TestLogETWApp
{
    static TestLogETWApp()
    {
        // Register assembly resolver to find LogETWApp in TestDependencies folder
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            if (args.Name.StartsWith("LogETWApp"))
            {
                string testDepsPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "TestDependencies",
                    "LogETWApp.dll"
                );
                if (File.Exists(testDepsPath))
                {
                    return Assembly.LoadFrom(testDepsPath);
                }
            }
            return null;
        };
    }

    [TestMethod]
    public void BasicTest()
    {
        LogETWApp.LogSomeStuff.LogFor10Seconds();
        Assert.IsTrue(true); //We did not crash so yay
    }
}

