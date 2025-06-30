using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace findneedle.ETWPlugin;

public class EtwProviderExtractionResult
{
    public List<string> Providers { get; set; } = new();
    public List<string> Attempts { get; set; } = new();
}

public static class EtwProviderExtractor
{
    public static EtwProviderExtractionResult GetEtwProvidersFromBinary(string binaryPath)
    {
        var providers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var attempts = new List<string>();
        if (!File.Exists(binaryPath))
            throw new FileNotFoundException($"File not found: {binaryPath}");

        // 1. Try .NET manifest resources
        attempts.Add(".NET manifest resource (reflection)");
        bool isManaged = false;
        try
        {
            var assembly = Assembly.LoadFrom(binaryPath);
            isManaged = true;
            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                if (resourceName.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream == null) continue;
                    using var reader = new StreamReader(stream);
                    var manifestXml = reader.ReadToEnd();
                    foreach (var p in ParseManifestXml(manifestXml))
                        providers.Add(p);
                }
            }
        }
        catch { /* Not a .NET assembly or no manifest, continue */ }

        // 2. Try native manifest extraction with mt.exe
        bool mtWorked = false;
        string tempManifest = Path.GetTempFileName();
        attempts.Add("mt.exe -inputresource:\"<binary>\";#1 -out:<temp>");
        try
        {
            var mtExe = "mt.exe"; // Assumes mt.exe is in PATH
            var psi = new ProcessStartInfo
            {
                FileName = mtExe,
                Arguments = $"-inputresource:\"{binaryPath}\";#1 -out:\"{tempManifest}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var proc = Process.Start(psi))
            {
                if (proc != null)
                {
                    proc.WaitForExit(5000);
                    if (proc.ExitCode == 0 && File.Exists(tempManifest))
                    {
                        var manifestXml = File.ReadAllText(tempManifest);
                        foreach (var p in ParseManifestXml(manifestXml))
                            providers.Add(p);
                        mtWorked = true;
                    }
                }
            }
        }
        catch { /* mt.exe not found or failed, skip */ }
        finally
        {
            try { if (File.Exists(tempManifest)) File.Delete(tempManifest); } catch { }
        }

        // 3. If mt.exe did not work, try wevtutil im <binary>
        attempts.Add("wevtutil.exe im <binary>");
        if (!mtWorked)
        {
            try
            {
                var wevtutilExe = "wevtutil.exe";
                var psi = new ProcessStartInfo
                {
                    FileName = wevtutilExe,
                    Arguments = $"im \"{binaryPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var proc = Process.Start(psi))
                {
                    if (proc != null)
                    {
                        string output = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit(5000);
                        foreach (var p in ParseManifestXml(output))
                            providers.Add(p);
                    }
                }
            }
            catch { /* wevtutil not found or failed, skip */ }
        }

        // 4. If native, try EtwNativeProviderScanner
        if (!isManaged)
        {
            attempts.Add("Native binary scan (EtwNativeProviderScanner)");
            try
            {
                var nativeProviders = EtwNativeProviderScanner.ExtractNativeEtwProviders(binaryPath);
                foreach (var (guid, name) in nativeProviders)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        providers.Add($"{name} ({guid})");
                    else
                        providers.Add(guid.ToString());
                }
            }
            catch { /* ignore errors */ }
        }

        return new EtwProviderExtractionResult
        {
            Providers = new List<string>(providers),
            Attempts = attempts
        };
    }

    public static bool IsManagedBinary(string binaryPath)
    {
        if (!File.Exists(binaryPath))
            throw new FileNotFoundException($"File not found: {binaryPath}");
        try
        {
            // Try to load as .NET assembly
            AssemblyName.GetAssemblyName(binaryPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> ParseManifestXml(string manifestXml)
    {
        if (string.IsNullOrWhiteSpace(manifestXml))
            yield break;
        List<string> results = new();
        try
        {
            XDocument doc = XDocument.Parse(manifestXml);
            foreach (var provider in doc.Descendants("provider"))
            {
                var name = provider.Attribute("name")?.Value;
                var guid = provider.Attribute("guid")?.Value;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    results.Add(!string.IsNullOrWhiteSpace(guid) ? $"{name} ({guid})" : name);
                }
            }
        }
        catch
        {
            // Not valid XML, skip
        }
        foreach (var r in results)
            yield return r;
    }
}
