using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.WDK;

namespace ETWPluginTests;

public class ETWTestUtils
{
    public static void UseTestTraceFmt()
    {
        WDKFinder.TEST_MODE = true;
        WDKFinder.TEST_MODE_PASS_FMT_PATH = true;
        WDKFinder.TEST_MODE_FMT_PATH = "SampleWDK\\tracefmt.exe";
    }

    public static string GetSampleETLFile()
    {
        return Path.GetFullPath("SampleFiles\\test.etl");
    }
}
