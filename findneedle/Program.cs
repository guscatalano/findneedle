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


        // --- WPP decode wiring (CLI) ---------------------------------------------------------------
        // Make custom ISymbolResolver plugins run on the DECODE path exactly as they do in the GUI, so an
        // external resolver author can hand this tool an ETL and prove it decodes. The provisioning core
        // lives in FindPluginCore; we register the seam here and feed it the symbol paths from the cmdline.
        //   --symbols=<_NT_SYMBOL_PATH-style path>     PDB folders / symbol servers to pull PDBs from
        //   --symbol-source=<folder;folder>            extra folders to sweep for binaries+PDBs
        var rawCmdArgs = Environment.GetCommandLineArgs();
        static string ArgVal(string[] a, string name)
        {
            foreach (var s in a ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                var t = s.Trim();
                if (t.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                {
                    var v = t.Substring(name.Length).Trim();
                    if (v.Length >= 2 && v.StartsWith("\"") && v.EndsWith("\"")) v = v.Substring(1, v.Length - 2);
                    return v;
                }
            }
            return string.Empty;
        }
        var symbolPath = ArgVal(rawCmdArgs, "--symbols=");
        var symbolSourcePath = ArgVal(rawCmdArgs, "--symbol-source=");

        // --resolver-timeout=<seconds>: the per-resolver hang backstop. A slow ISymbolResolver (large PDB /
        // symbol-server round trips) can take minutes; if it exceeds this it's abandoned and its symbols are
        // dropped. Default is generous (5 min) — raise it for an even slower resolver. Sets the env the core reads.
        var resolverTimeoutArg = ArgVal(rawCmdArgs, "--resolver-timeout=");
        if (!string.IsNullOrWhiteSpace(resolverTimeoutArg) && int.TryParse(resolverTimeoutArg, out var rtSec) && rtSec > 0)
        {
            Environment.SetEnvironmentVariable(FindPluginCore.Wpp.Symbols.WppSymbolResolver.ResolverTimeoutEnv, (rtSec * 1000).ToString());
            Console.WriteLine($"Symbol-resolver timeout: {rtSec}s");
        }

        // Set up the WDK trace-tool environment the SAME way the GUI does (shared TraceFormatEnv): put the
        // managed TMF cache on TRACE_FORMAT_SEARCH_PATH and --symbols on _NT_SYMBOL_PATH UP FRONT, so the CLI's
        // first decode behaves like the GUI's — not only after a provisioning miss. The ambient
        // TRACE_FORMAT_SEARCH_PATH the user set is preserved at the tail (that's the CLI's "TMF folder").
        // (This was the CLI-vs-GUI decode divergence: the GUI ran this at startup, the CLI ran nothing.)
        FindPluginCore.Wpp.Symbols.TraceFormatEnv.Apply(tmfFolder: null, symbolPath: symbolPath);

        // WPP decoder DEFAULT = managed (the pre-222 CLI behavior). Managed ALWAYS needs a TMF, so a trace
        // with missing WPP symbols reliably trips the missing-symbol detection and INVOKES the ISymbolResolver
        // provisioning seam — which is the whole point of the CLI for a resolver author. tracefmt can
        // self-resolve some traces (embedded format info) and then SKIP provisioning entirely, so it must not
        // be the default (that regressed resolver testing after 222). tracefmt is still available on demand:
        //   --wpp-decoder=tracefmt   (the WDK reference decoder) | auto (tracefmt when present) | compare
        var wppDecoderArg = ArgVal(rawCmdArgs, "--wpp-decoder=");
        if (!string.IsNullOrWhiteSpace(wppDecoderArg) && !WppDecoderParsing.IsKnown(wppDecoderArg))
            Console.WriteLine($"Unknown --wpp-decoder '{wppDecoderArg}'. Using managed. Valid: tracefmt, managed, auto, compare.");
        DecodeOptions.WppDecoder = string.IsNullOrWhiteSpace(wppDecoderArg)
            ? WppDecoder.Managed
            : WppDecoderParsing.FromArg(wppDecoderArg);
        Console.WriteLine($"WPP decoder: {DecodeOptions.WppDecoder}");

        // Raw-event decoders (IWppEventDecoder): a provider with no TMF at all can be formatted by a plugin.
        WppEventDecoding.Provider = () =>
            PluginManager.GetSingleton().GetAllPluginsInstancesOfAType<IWppEventDecoder>();

        // On-demand provisioning: when a WPP ETL misses TMFs, resolve them (built-in lookup + the
        // ISymbolResolver plugins), extract TMFs, refresh TRACE_FORMAT_SEARCH_PATH, and retry the decode
        // once. Wrapped in a counter so the summary below can prove the seam actually ran.
        int wppProvisionInvocations = 0, wppProvisionSucceeded = 0;
        var wppMissingGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        WppSymbolProvisioning.Handler = req =>
        {
            wppProvisionInvocations++;
            // NOTE: req.MissingMessageGuids is the UPFRONT discovery set (managed), which over-reports GUIDs a
            // self-resolving tracefmt decode doesn't actually need. So it does NOT seed the summary's "missing"
            // list — that comes solely from the sink (what the FINAL decode truly left unformatted).
            var made = FindPluginCore.Wpp.Symbols.WppSymbolResolver.TryProvision(req, symbolSourcePath, symbolPath);
            if (made) wppProvisionSucceeded++;
            return made;
        };
        // The provisioning seam above only fires on an ALL-unknown fail-fast, so it can't report a PARTIAL
        // decode (some TMFs present, many events still unformatted). The ETL processor reports what it
        // actually found into this ambient sink; reset it before the run and fold it into the summary.
        FindNeedlePluginLib.WppDecodeReport.Reset();
        // -------------------------------------------------------------------------------------------

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

        // Prepare output folder path so we can offer to open it later. Under the CURRENT WORKING DIRECTORY,
        // not next to the exe: a Store-installed CLI runs from a read-only folder (C:\Program Files\
        // WindowsApps\...), so RuleDSL/UML output written beside the exe would fail. Matches --out's default.
        var outputFolder = Path.Combine(Directory.GetCurrentDirectory(), "output");

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

        // Did the user ask for anything that WRITES files? A plain decode (no --rules, no --out) produces no
        // output files by design — so an empty output/ folder is expected, not a problem. Gate the
        // "no files / folder is empty" chatter on this so it isn't misleading.
        var outputExpected = cmdArgs != null && cmdArgs.Any(a =>
            a.StartsWith("--rules", StringComparison.OrdinalIgnoreCase)
            || a.StartsWith("--out=", StringComparison.OrdinalIgnoreCase)
            || a.StartsWith("--out-file=", StringComparison.OrdinalIgnoreCase));

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
                    // Only mention the output folder if the user asked for file output — otherwise it's noise
                    // (a plain decode writes no files, so "will be created" is misleading).
                    if (outputExpected)
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
                                    // Print concise relative path to the console. Do not log the same "Output written"
                                    // message here to avoid duplicating full paths on the console (they remain in
                                    // other component logs). Keep the console output short and user-friendly.
                                    try
                                    {
                                        var rel = Path.GetRelativePath(outputFolder, f);
                                        Console.WriteLine(rel);
                                    }
                                    catch
                                    {
                                        Console.WriteLine(Path.GetFileName(f));
                                    }
                                }
                        }
                        else if (outputExpected)
                        {
                            // Output WAS requested (--rules / --out) but nothing was written — that's worth flagging.
                            Logger.Instance.Log("No new output files were created.");
                            Console.WriteLine("No output files were created — check your --rules / --out arguments.");
                        }
                        // else: a plain decode with no --rules/--out writes no files by design. Say nothing about
                        // the output folder (it being empty is expected); the decoded rows are in the summary below.
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
        // --- Optional decoded-record output (--out=csv|json|txt|html; opt-in) ----------------------
        // Writes the decoded rows to a file so a resolver author can EYEBALL what decoded, not just read
        // an exit code. Reads from the same ResultStorage the exit-code count uses. --out-file=<path>
        // overrides the default (output/findneedle_decoded.<ext>).
        try
        {
            var outFormat = ArgVal(rawCmdArgs, "--out=");
            if (!string.IsNullOrWhiteSpace(outFormat))
            {
                if (!FindPluginCore.Output.ResultOutputWriter.IsSupported(outFormat))
                {
                    Console.WriteLine($"Unknown --out format '{outFormat}'. Use: {string.Join(", ", FindPluginCore.Output.ResultOutputWriter.SupportedFormats)}");
                }
                else
                {
                    var rows = new List<FindNeedlePluginLib.ISearchResult>();
                    var outStorage = x.GetType().GetProperty("ResultStorage")?.GetValue(x)
                                     as FindNeedlePluginLib.Interfaces.ISearchStorage;
                    outStorage?.GetFilteredResultsInBatches(b => rows.AddRange(b));

                    var outFile = ArgVal(rawCmdArgs, "--out-file=");
                    if (string.IsNullOrWhiteSpace(outFile))
                        // Default under the CURRENT WORKING DIRECTORY, not next to the exe: when installed from
                        // the Store the exe lives in a read-only folder (C:\Program Files\WindowsApps\...), so
                        // writing beside it fails. The CWD is writable and is where a CLI user expects output.
                        outFile = Path.Combine(Directory.GetCurrentDirectory(), "output",
                            "findneedle_decoded" + FindPluginCore.Output.ResultOutputWriter.ExtensionFor(outFormat));
                    FindPluginCore.Output.ResultOutputWriter.WriteToFile(rows, outFormat, outFile);
                    Console.WriteLine($"Wrote {rows.Count:N0} decoded rows to {outFile} ({outFormat.Trim().ToLowerInvariant()})");
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"--out failed: {ex.Message}"); }

        // --- Decode-proof summary: records decoded, WPP provisioning + resolvers consulted, and an exit
        // code an external ISymbolResolver author can assert on (0 = fully decoded). ------------------
        try
        {
            int rowsDecoded = 0;
            try
            {
                // The authoritative decoded-row count lives in the RESULT STORAGE. The AtSearch statistic is 0
                // in the "just decode, no rules" path — the lazy pipeline skips re-materializing filtered
                // results — which would otherwise pin this to "0 rows / exit 2" no matter what a symbol resolver
                // actually did, making the tool useless for proving a decode. Read storage first (same source
                // the GUI uses), and fall back to AtSearch only if there's no storage.
                var storage = x.GetType().GetProperty("ResultStorage")?.GetValue(x)
                              as FindNeedlePluginLib.Interfaces.ISearchStorage;
                if (storage != null)
                {
                    var st = storage.GetStatistics();
                    rowsDecoded = st.rawRecordCount > 0 ? st.rawRecordCount : st.filteredRecordCount;
                }
                if (rowsDecoded <= 0)
                    rowsDecoded = x.GetSearchStatistics().GetRecordsAtStep(SearchStep.AtSearch);
            }
            catch { }

            var resolvers = new List<string>();
            try
            {
                foreach (var r in PluginManager.GetSingleton().GetAllPluginsInstancesOfAType<ISymbolResolver>())
                    resolvers.Add(r.GetType().FullName ?? r.GetType().Name);
            }
            catch { }

            // Fold in what the decoder ACTUALLY saw (partial decodes never hit the provisioning seam above).
            long unresolvedEvents = 0;
            try
            {
                var (sinkGuids, sinkUnresolved) = FindNeedlePluginLib.WppDecodeReport.Snapshot();
                foreach (var g in sinkGuids) wppMissingGuids.Add(g);
                unresolvedEvents = sinkUnresolved;
            }
            catch { }

            Console.WriteLine();
            Console.WriteLine("=== WPP decode summary ===");
            Console.WriteLine($"  Records decoded (matched):           {rowsDecoded}");
            Console.WriteLine($"  ISymbolResolver plugins consulted:   {resolvers.Count}");
            foreach (var r in resolvers) Console.WriteLine($"      - {r}");
            Console.WriteLine($"  WPP symbol provisioning invoked:     {wppProvisionInvocations} time(s), made new symbols {wppProvisionSucceeded} time(s)");
            Console.WriteLine($"  Distinct missing message GUIDs seen: {wppMissingGuids.Count}");
            Console.WriteLine($"  WPP events left unformatted:         {unresolvedEvents}");
            if (wppMissingGuids.Count > 0)
                foreach (var g in wppMissingGuids.OrderBy(s => s)) Console.WriteLine($"      - {g}");

            // Exit reflects the actual decode: rows out + events left unformatted. Provisioning counts above
            // are a resolver diagnostic, not an input to the verdict — a trace tracefmt self-resolves can have
            // "provisioning invoked, 0 made" yet be fully decoded (exit 0).
            int exit = FindPluginCore.Diagnostics.DecodeProof.ComputeExitCode(rowsDecoded, unresolvedEvents);
            var verdict = FindPluginCore.Diagnostics.DecodeProof.Describe(exit);
            Console.WriteLine($"  Result: {verdict} (exit {exit})");
            Console.WriteLine("  Note: this is a WPP-only decode. 'Records decoded' counts WPP events whose symbols");
            Console.WriteLine("        resolved; manifest/kernel (non-WPP) events in the trace are NOT rendered here,");
            Console.WriteLine("        so 'fully decoded' means all WPP symbols resolved, not that every event is shown.");
            Console.WriteLine("==========================");
            Environment.ExitCode = exit;
        }
        catch { }

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