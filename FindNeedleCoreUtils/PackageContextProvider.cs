namespace FindNeedleCoreUtils;

/// <summary>
/// Abstraction for detecting packaged app status.
/// This allows tests to inject mock implementations to simulate different app contexts.
/// </summary>
public interface IPackageContextProvider
{
    /// <summary>
    /// Gets the package family name if running as a packaged app, or null if unpackaged.
    /// </summary>
    string? PackageFamilyName { get; }

    /// <summary>
    /// Returns true if the app is running as a packaged MSIX app.
    /// </summary>
    bool IsPackagedApp { get; }
}

/// <summary>
/// Production implementation that detects actual package context.
/// </summary>
public class ProductionPackageContextProvider : IPackageContextProvider
{
    private static string? _cachedPackageFamilyName;
    private static bool _packageCheckDone;

    public string? PackageFamilyName
    {
        get
        {
            if (!_packageCheckDone)
            {
                try
                {
                    _cachedPackageFamilyName = global::Windows.ApplicationModel.Package.Current.Id.FamilyName;
                }
                catch
                {
                    _cachedPackageFamilyName = null;
                }
                _packageCheckDone = true;
            }
            return _cachedPackageFamilyName;
        }
    }

    public bool IsPackagedApp => PackageFamilyName != null;
}

/// <summary>
/// Test implementation that allows simulating packaged or unpackaged context.
/// </summary>
public class TestPackageContextProvider : IPackageContextProvider
{
    private readonly string? _packageFamilyName;
    private readonly bool _isPackagedApp;

    public TestPackageContextProvider(bool isPackagedApp, string? packageFamilyName = null)
    {
        _isPackagedApp = isPackagedApp;
        _packageFamilyName = isPackagedApp ? (packageFamilyName ?? "TestApp_abc123") : null;
    }

    public string? PackageFamilyName => _packageFamilyName;
    public bool IsPackagedApp => _isPackagedApp;
}

/// <summary>
/// Global provider for package context detection.
/// Can be overridden for testing.
/// </summary>
public static class PackageContextProviderFactory
{
    private static IPackageContextProvider? _testProvider;

    public static IPackageContextProvider Current => 
        _testProvider ?? new ProductionPackageContextProvider();

    /// <summary>
    /// Set a test provider (use in tests only).
    /// </summary>
    public static void SetTestProvider(IPackageContextProvider? provider)
    {
        _testProvider = provider;
    }

    /// <summary>
    /// Reset to production provider.
    /// </summary>
    public static void ResetToProduction()
    {
        _testProvider = null;
    }
}
