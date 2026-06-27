using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// Runs once before any UI test. Sets FINDNEEDLE_NO_SINGLE_INSTANCE so every app instance the FlaUI
    /// tests launch is independent — otherwise the app's single-instancing redirects each relaunch to the
    /// still-alive owner and exits, and tests fail with "Could not find process" when run as a suite.
    /// Child processes inherit this process's environment, so setting it here covers all launches.
    /// </summary>
    [TestClass]
    public static class TestAssemblyInit
    {
        [AssemblyInitialize]
        public static void Init(TestContext _)
            => System.Environment.SetEnvironmentVariable("FINDNEEDLE_NO_SINGLE_INSTANCE", "1");
    }
}
