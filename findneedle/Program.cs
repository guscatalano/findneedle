global using System.Diagnostics.CodeAnalysis;
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Text;
global using System.Threading.Tasks;
using findneedle;
using findneedle.Implementations;
using findneedle.PluginSubsystem;
using FindNeedleCoreUtils;
using FindPluginCore.Searching;
using FindNeedlePluginLib;
using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Reflection;

[ExcludeFromCodeCoverage]
internal class Program
{
    static void Main(string[] args)
    {

        var cancel = false;
        Console.CancelKeyPress += delegate {
            cancel = true;
            Console.WriteLine("Cancel received, exiting");
            Environment.Exit(0);
        };

        // Configure logger for console app: write detailed logs to file (already done by Logger),
        // but only show minimal, important messages on the console.
        Logger.Instance.LogCallback = line =>
        {
            // If user requested verbose output via --verbose, print everything
            if (args != null && args.Any(a => a.Equals("--verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("-v", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine(line);
                return;
            }

            // Minimal console output: errors, failures, final completion, outputs, warnings
            if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("search complete", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("output written", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Console.WriteLine(line);
            }
        };

        // Inform user where detailed log file is located
        try
        {
            var folder = FileIO.GetAppDataFindNeedlePluginFolder();
            var logfile = Path.Combine(folder, "findneedle_log.txt");
            Console.WriteLine($"Detailed log: {logfile}");
        }
        catch
        {
            // ignore
        }


        var x = SearchQueryCmdLine.ParseFromCommandLine(Environment.GetCommandLineArgs(), PluginManager.GetSingleton());
        // Fallback: if parse did not pick up --rules, check raw args and set on query
        try
        {
            var rawArgs = Environment.GetCommandLineArgs();
            foreach (var a in rawArgs)
            {
                if (string.IsNullOrWhiteSpace(a)) continue;
                var lower = a.ToLowerInvariant();
                if (lower.StartsWith("--rules=") || lower.StartsWith("rules="))
                {
                    var idx = a.IndexOf('=');
                    if (idx >= 0 && idx < a.Length - 1)
                    {
                        var val = a.Substring(idx + 1).Trim();
                        if (val.StartsWith("\"") && val.EndsWith("\""))
                            val = val.Substring(1, val.Length - 2);
                        try { val = FileIO.FindFullPathToFile(val); } catch { }
                        try
                        {
                            dynamic dx = x;
                            if (dx.RulesConfigPaths == null)
                                dx.RulesConfigPaths = new List<string>();
                            if (!((List<string>)dx.RulesConfigPaths).Contains(val, StringComparer.OrdinalIgnoreCase))
                            {
                                ((List<string>)dx.RulesConfigPaths).Add(val);
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }
        SearchQueryCmdLine.PrintToConsole(x);
        // Print what rule files (if any) were provided so user can confirm
        try
        {
            if (x is not null)
            {
                try
                {
                    var dyn = (dynamic)x;
                    var rp = dyn.RulesConfigPaths as List<string>;
                    if (rp != null && rp.Count > 0)
                    {
                        Console.WriteLine("Rules files:");
                        foreach (var r in rp)
                        {
                            Console.WriteLine("\t" + r);
                            Logger.Instance.Log($"Using rules file: {r}");
                        }
                    }
                }
                catch
                {
                    // ignore if ISearchQuery implementation doesn't expose RulesConfigPaths
                }
            }
        }
        catch { }
        PluginManager.GetSingleton().PrintToConsole();

        // Verify bundled DLLs are present so installer prompt and UML generation can run
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string? installersPath = null;
            string? umlDslPath = null;
            try { installersPath = Directory.EnumerateFiles(baseDir, "FindNeedleToolInstallers.dll", SearchOption.AllDirectories).FirstOrDefault(); } catch { }
            try { umlDslPath = Directory.EnumerateFiles(baseDir, "FindNeedleUmlDsl.dll", SearchOption.AllDirectories).FirstOrDefault(); } catch { }

            if (string.IsNullOrEmpty(installersPath))
            {
                Console.WriteLine("Warning: bundled installers (FindNeedleToolInstallers.dll) not found in application output. Installer prompt will not be available.");
                Logger.Instance.Log("Bundled installers not found: FindNeedleToolInstallers.dll not present in app base or subfolders. Build or copy the project output to enable automatic installs.");
            }
            else
            {
                Logger.Instance.Log($"Found FindNeedleToolInstallers: {installersPath}");
            }

            if (string.IsNullOrEmpty(umlDslPath))
            {
                Console.WriteLine("Warning: UML generator assembly (FindNeedleUmlDsl.dll) not found in application output. UML generation may be unavailable.");
                Logger.Instance.Log("UML assembly not found: FindNeedleUmlDsl.dll not present in app base or subfolders. Build or copy the project output to enable UML generation.");
            }
            else
            {
                Logger.Instance.Log($"Found FindNeedleUmlDsl: {umlDslPath}");
            }
        }
        catch { }

        // If any rules request UML image generation, offer to install missing UML tool dependencies
        try
        {
            bool requiresUmlImage = false;
            try
            {
                dynamic dx = x;
                var rp = dx.RulesConfigPaths as List<string>;
                if (rp != null)
                {
                    foreach (var rf in rp)
                    {
                        try
                        {
                            if (!File.Exists(rf)) continue;
                            using var doc = JsonDocument.Parse(File.ReadAllText(rf));
                            if (!doc.RootElement.TryGetProperty("sections", out var sections)) continue;
                            foreach (var sec in sections.EnumerateArray())
                            {
                                if (sec.ValueKind != JsonValueKind.Object) continue;
                                if (sec.TryGetProperty("purpose", out var pv) && pv.GetString() == "output")
                                {
                                    if (sec.TryGetProperty("rules", out var rulesEl) && rulesEl.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var rEl in rulesEl.EnumerateArray())
                                        {
                                            if (rEl.ValueKind != JsonValueKind.Object) continue;
                                            if (rEl.TryGetProperty("action", out var act) && act.ValueKind == JsonValueKind.Object)
                                            {
                                                if (act.TryGetProperty("type", out var t) && t.GetString()?.Equals("uml", StringComparison.OrdinalIgnoreCase) == true)
                                                {
                                                    if (act.TryGetProperty("generateImage", out var gen) && gen.ValueKind == JsonValueKind.True)
                                                    {
                                                        requiresUmlImage = true;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                if (requiresUmlImage) break;
                            }
                        }
                        catch { }
                        if (requiresUmlImage) break;
                    }
                }
            }
            catch { }

            if (requiresUmlImage)
            {
                try
                {
                    static bool IsExeOnPath(string exeName)
                    {
                        try
                        {
                            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                            var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                            var candidates = new[] { exeName, exeName + ".cmd", exeName + ".exe" };
                            foreach (var p in paths)
                            {
                                foreach (var c in candidates)
                                {
                                    var f = Path.Combine(p, c);
                                    if (File.Exists(f)) return true;
                                }
                            }
                        }
                        catch { }
                        return false;
                    }

                    // quick check for Mermaid CLI (mmdc) on PATH
                    var mermaidAvailable = IsExeOnPath("mmdc");

                    if (!mermaidAvailable)
                    {
                        // Try to use installers if available
                        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                        var installerAsmPath = Directory.EnumerateFiles(baseDir, "FindNeedleToolInstallers.dll", SearchOption.AllDirectories).FirstOrDefault();
                        if (!string.IsNullOrEmpty(installerAsmPath) && File.Exists(installerAsmPath))
                        {
                            var instAsm = Assembly.LoadFrom(installerAsmPath);
                            var managerType = instAsm.GetType("FindNeedleToolInstallers.UmlDependencyManager");
                        if (managerType != null)
                        {
                            object? manager = null;
                            try
                            {
                                manager = Activator.CreateInstance(managerType);
                            }
                            catch
                            {
                                try
                                {
                                    var ctors = managerType.GetConstructors().OrderBy(c => c.GetParameters().Length).ToList();
                                    foreach (var c in ctors)
                                    {
                                        var ps = c.GetParameters();
                                        var ctorArgs = ps.Select(p => (object?)null).ToArray();
                                        try
                                        {
                                            manager = c.Invoke(ctorArgs);
                                            if (manager != null) break;
                                        }
                                        catch { }
                                    }
                                }
                                catch { }
                            }
                            var areInstalledMethod = managerType.GetMethod("AreAllImageDependenciesInstalled");
                            var installed = areInstalledMethod != null && manager != null && (bool)areInstalledMethod.Invoke(manager, null)!;
                                if (!installed)
                                {
                                    Console.Write("Mermaid/PlantUML tooling is not available. Install now via bundled installers? (y/N): ");
                                    var resp = Console.ReadLine();
                                // Automatically install missing UML dependencies when detected
                                try
                                {
                                    Console.WriteLine("Mermaid/PlantUML tooling is not available. Installing missing tools via bundled installers...");
                                    var installMethod = managerType.GetMethod("InstallAllMissingAsync");
                                    if (installMethod != null)
                                    {
                                        var task = (System.Threading.Tasks.Task)installMethod.Invoke(manager, new object[] { null, System.Threading.CancellationToken.None })!;
                                        task.Wait();
                                        Console.WriteLine("Installation complete. Proceeding with search...");
                                    }
                                    else
                                    {
                                        Console.WriteLine("Installer API not available; UML image generation will be skipped.");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Installer failed: {ex.Message}. Proceeding without UML images.");
                                    Logger.Instance.Log($"Installer invocation failed: {ex.Message}");
                                }
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("Mermaid CLI (mmdc) not found on PATH and installers not available. UML images will be skipped. To enable image generation, install mmdc or use the UX Diagram Tools page.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log($"Error checking/installing UML dependencies: {ex.Message}");
                }
            }
        }
        catch { }

        // Show the concrete files that will be searched (short summary, not verbose).
        try
        {
            if (x != null && x.Locations != null && x.Locations.Count > 0)
            {
                Console.WriteLine("Input locations and sample files:");
                foreach (var loc in x.Locations)
                {
                    try
                    {
                        var locName = loc.GetName();
                        Console.WriteLine($"  Location: {locName}");
                        // If it's a directory, enumerate files (robustly) and show a short sample
                        if (Directory.Exists(locName))
                        {
                            var files = FileIO.GetAllFiles(locName, path => { /* ignore errors */ }).ToList();
                            var total = files.Count;
                            var sampleCount = Math.Min(20, total);
                            if (sampleCount > 0)
                            {
                                Console.WriteLine($"    Showing {sampleCount} of {total} files:");
                                for (int i = 0; i < sampleCount; i++)
                                {
                                    Console.WriteLine($"      {files[i]}");
                                }
                                if (total > sampleCount)
                                {
                                    Console.WriteLine($"      ... and {total - sampleCount} more files");
                                }
                            }
                            else
                            {
                                Console.WriteLine("    (no files found)");
                            }
                        }
                        else if (File.Exists(locName))
                        {
                            Console.WriteLine($"    File: {locName}");
                        }
                        else
                        {
                            Console.WriteLine("    (location does not exist or is not accessible)");
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        // Prepare output folder path so we can offer to open it later
        var outputBase = AppDomain.CurrentDomain.BaseDirectory;
        var outputFolder = Path.Combine(outputBase, "output");

        // Support a --force / -f flag to skip interactive confirmations (useful for scripting)
        var cmdArgs = Environment.GetCommandLineArgs();
        var force = cmdArgs != null && cmdArgs.Any(a => a.Equals("--force", StringComparison.OrdinalIgnoreCase) || a.Equals("-f", StringComparison.OrdinalIgnoreCase) || a.Equals("--yes", StringComparison.OrdinalIgnoreCase) || a.Equals("-y", StringComparison.OrdinalIgnoreCase));

        // Support a flag to clear existing output before running: --clear-existing-output, --clear-output, --clean-output, -c
        var clearExisting = cmdArgs != null && cmdArgs.Any(a =>
            a.Equals("--clear-existing-output", StringComparison.OrdinalIgnoreCase)
            || a.Equals("--clear-output", StringComparison.OrdinalIgnoreCase)
            || a.Equals("--clean-output", StringComparison.OrdinalIgnoreCase)
            || a.Equals("-c", StringComparison.OrdinalIgnoreCase)
        );

        if (!force)
        {
            Console.WriteLine("If correct, please enter to search, otherwise ctrl-c to exit");
            var input = Console.ReadLine();
            if (cancel || input == null) // input will be null when it's control+c
            {
                // user cancelled, exit early
                Environment.Exit(0);
            }
        }
        else
        {
            Logger.Instance.Log("Force flag present: skipping confirmation to start search");
        }

        // Proceed with search (either forced or after user confirmation)
        // Note: keep original cancel handling in case of Ctrl-C during run
        if (cancel) Environment.Exit(0);
        // If requested, clear existing output files before we enumerate/create output folder
        if (clearExisting)
        {
            try
            {
                if (Directory.Exists(outputFolder))
                {
                    var files = Directory.GetFiles(outputFolder);
                    foreach (var f in files)
                    {
                        try
                        {
                            File.Delete(f);
                            Logger.Instance.Log($"Deleted existing output file: {f}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Instance.Log($"Failed to delete output file {f}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    try { Directory.CreateDirectory(outputFolder); } catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"Error clearing existing output files: {ex.Message}");
            }
        }
            // Enumerate output folder before running so user can see what existed
            try
            {
                var beforeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (Directory.Exists(outputFolder))
                {
                    Logger.Instance.Log($"Existing output folder: {outputFolder}");
                    var existing = Directory.GetFiles(outputFolder).ToList();
                    if (existing.Count == 0)
                    {
                        // no console output for existing files
                    }
                    foreach (var f in existing)
                    {
                        beforeFiles.Add(f);
                        Logger.Instance.Log($"Existing output file: {f}");
                    }
                }
                else
                {
                    Logger.Instance.Log($"Output folder does not exist (will be created): {outputFolder}");
                    Console.WriteLine($"Output folder does not exist (will be created): {outputFolder}");
                }

                Console.WriteLine("Searching...");
                x.RunThrough();

                // After run, enumerate output folder and show differences
                try
                {
                    var afterFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (Directory.Exists(outputFolder))
                    {
                        foreach (var f in Directory.GetFiles(outputFolder))
                        {
                            afterFiles.Add(f);
                        }

                        var added = afterFiles.Except(beforeFiles).ToList();
                        if (added.Count > 0)
                        {
                            Logger.Instance.Log($"New output files ({added.Count}):");
                            Console.WriteLine("Output files written:");
                            foreach (var f in added)
                            {
                                Logger.Instance.Log($"Output written: {f}");
                                Console.WriteLine(f);
                            }
                        }
                        else
                        {
                            Logger.Instance.Log("No new output files were created.");
                            Console.WriteLine("No new output files were created.");
                            // Also print all current files to help user locate outputs
                            if (afterFiles.Count > 0)
                            {
                                Console.WriteLine("Current output files:");
                                foreach (var f in afterFiles)
                                {
                                    Console.WriteLine(f);
                                }
                            }
                            else
                            {
                                Console.WriteLine("Output folder is empty.");
                            }
                        }
                    }
                    else
                    {
                        Logger.Instance.Log($"Output folder still does not exist after run: {outputFolder}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log($"Error enumerating output folder after run: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"Error preparing output listing: {ex.Message}");
                Console.WriteLine("Searching...");
                x.RunThrough();
            }
        Console.WriteLine("Done");

        try
        {
            // If forced, skip the prompt to open output folder
            if (force)
            {
                Logger.Instance.Log("Force flag present: skipping prompt to open output folder");
            }
            else
            {
                // Default is No: only open when user explicitly answers 'y' or 'Y'
                Console.Write("Open output folder? (y/N): ");
                var openResp = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(openResp) && openResp.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Directory.Exists(outputFolder))
                    {
                        Console.WriteLine($"Output folder does not exist: {outputFolder}");
                    }
                    else
                    {
                        try
                        {
                            var psi = new ProcessStartInfo { FileName = outputFolder, UseShellExecute = true };
                            Process.Start(psi);
                        }
                        catch (Exception ex)
                        {
                            Logger.Instance.Log($"Failed to open output folder: {ex.Message}");
                            Console.WriteLine("Failed to open output folder: " + ex.Message);
                        }
                    }
                }
            }
        }
        catch { }


    }
}