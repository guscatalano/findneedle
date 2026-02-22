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

        Console.WriteLine("If correct, please enter to search, otherwise ctrl-c to exit");
        var input = Console.ReadLine();
        if (!cancel || input != null) //input will be null when its control+c
        {
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
        }
        Console.WriteLine("Done");

        try
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
        catch { }


    }
}