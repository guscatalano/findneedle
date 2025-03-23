using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreTests;

public class TestGlobals
{
    public const string TEST_DEP_FOLDER = "TestDependencies";
    public const string TEST_DEP_PLUGIN = "TestProcessorPlugin.dll";
    public const string FAKE_LOAD_PLUGIN = "FakeLoadPlugin.exe";
    public const string TEST_DEP_PLUGIN_REL_PATH = TEST_DEP_FOLDER + "\\" + TEST_DEP_PLUGIN;
    public const string FAKE_LOAD_PLUGIN_REL_PATH = TEST_DEP_FOLDER + "\\" + FAKE_LOAD_PLUGIN;

    public const int TEST_DEP_PLUGIN_COUNT = 1;
}

