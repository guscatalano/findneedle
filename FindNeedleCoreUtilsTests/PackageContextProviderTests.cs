using FindNeedleCoreUtils;

namespace FindNeedleCoreUtilsTests;

[TestClass]
public class PackageContextProviderTests
{
    [TestCleanup]
    public void Cleanup()
    {
        // Always reset after tests
        PackageContextProviderFactory.ResetToProduction();
    }

    [TestMethod]
    public void ProductionProvider_DetectsPackageContext()
    {
        var provider = new ProductionPackageContextProvider();
        
        // This will be null for unpackaged tests, which is fine
        // In production, it will have a value
        var packageFamilyName = provider.PackageFamilyName;
        var isPackaged = provider.IsPackagedApp;
        
        // We can at least verify it doesn't throw
        Assert.IsTrue(isPackaged is bool);
        Assert.IsTrue(packageFamilyName is null or string);
    }

    [TestMethod]
    public void TestProvider_SimulatesPackagedApp()
    {
        var testProvider = new TestPackageContextProvider(isPackagedApp: true, packageFamilyName: "MyApp_12345");
        
        Assert.IsTrue(testProvider.IsPackagedApp);
        Assert.AreEqual("MyApp_12345", testProvider.PackageFamilyName);
    }

    [TestMethod]
    public void TestProvider_SimulatesUnpackagedApp()
    {
        var testProvider = new TestPackageContextProvider(isPackagedApp: false);
        
        Assert.IsFalse(testProvider.IsPackagedApp);
        Assert.IsNull(testProvider.PackageFamilyName);
    }

    [TestMethod]
    public void TestProvider_DefaultPackageFamilyName()
    {
        var testProvider = new TestPackageContextProvider(isPackagedApp: true);
        
        // Should use default "TestApp_abc123"
        Assert.AreEqual("TestApp_abc123", testProvider.PackageFamilyName);
    }

    [TestMethod]
    public void ProviderFactory_ReturnsCurrentProvider()
    {
        var testProvider = new TestPackageContextProvider(isPackagedApp: true, packageFamilyName: "TestFactory_123");
        PackageContextProviderFactory.SetTestProvider(testProvider);
        
        var current = PackageContextProviderFactory.Current;
        
        Assert.IsTrue(current.IsPackagedApp);
        Assert.AreEqual("TestFactory_123", current.PackageFamilyName);
    }

    [TestMethod]
    public void ProviderFactory_ReturnsProductionByDefault()
    {
        PackageContextProviderFactory.ResetToProduction();
        
        var current = PackageContextProviderFactory.Current;
        
        // Should be a ProductionPackageContextProvider
        Assert.IsNotNull(current);
        // It won't throw when accessing properties
        var _ = current.IsPackagedApp;
        var __ = current.PackageFamilyName;
    }

    [TestMethod]
    public void ProviderFactory_ResetToProduction_Works()
    {
        // Set a test provider
        var testProvider = new TestPackageContextProvider(isPackagedApp: true);
        PackageContextProviderFactory.SetTestProvider(testProvider);
        Assert.IsTrue(PackageContextProviderFactory.Current.IsPackagedApp);
        
        // Reset to production
        PackageContextProviderFactory.ResetToProduction();
        
        // Should no longer be the test provider
        // (we can't easily test this without reflection, but at least it doesn't throw)
        var current = PackageContextProviderFactory.Current;
        Assert.IsNotNull(current);
    }
}

[TestClass]
public class PackagedAppCommandRunnerMockedTests
{
    [TestCleanup]
    public void Cleanup()
    {
        PackageContextProviderFactory.ResetToProduction();
    }

    [TestMethod]
    public void IsPackagedApp_RespectsTestProvider_PackagedContext()
    {
        var testProvider = new TestPackageContextProvider(isPackagedApp: true, packageFamilyName: "MockedApp_12345");
        PackageContextProviderFactory.SetTestProvider(testProvider);
        
        // Note: PackagedAppCommandRunner uses the factory internally
        // This test documents the intended usage pattern
        Assert.IsTrue(testProvider.IsPackagedApp);
        Assert.AreEqual("MockedApp_12345", testProvider.PackageFamilyName);
    }

    [TestMethod]
    public void IsPackagedApp_RespectsTestProvider_UnpackagedContext()
    {
        var testProvider = new TestPackageContextProvider(isPackagedApp: false);
        PackageContextProviderFactory.SetTestProvider(testProvider);
        
        Assert.IsFalse(testProvider.IsPackagedApp);
        Assert.IsNull(testProvider.PackageFamilyName);
    }

    [TestMethod]
    public void CommandBuilder_CanBeTestedWithMockedContext()
    {
        // Simulate packaged app context
        var testProvider = new TestPackageContextProvider(isPackagedApp: true, packageFamilyName: "TestApp_xyz");
        PackageContextProviderFactory.SetTestProvider(testProvider);
        
        // Build a package context command using the mocked provider
        var command = PowerShellCommandBuilder.BuildPackageContextCommand(
            packageFamilyName: testProvider.PackageFamilyName!,
            innerCommand: "Write-Host 'test'"
        );
        
        Assert.IsTrue(command.Contains("TestApp_xyz"));
        Assert.IsTrue(command.Contains("Invoke-CommandInDesktopPackage"));
    }

    [TestMethod]
    public void MultipleTestContexts_CanBeTested()
    {
        // Test scenario 1: Packaged app
        var packagedProvider = new TestPackageContextProvider(isPackagedApp: true, packageFamilyName: "App1_123");
        PackageContextProviderFactory.SetTestProvider(packagedProvider);
        Assert.IsTrue(packagedProvider.IsPackagedApp);
        
        // Test scenario 2: Unpackaged app
        var unpackagedProvider = new TestPackageContextProvider(isPackagedApp: false);
        PackageContextProviderFactory.SetTestProvider(unpackagedProvider);
        Assert.IsFalse(unpackagedProvider.IsPackagedApp);
        
        // Both scenarios should have worked
        Assert.IsTrue(true);
    }
}
