using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;

namespace findneedle.WDK;

public class TraceFmtResult
{
    public string? outputfile
    {
        get;set;
    }
    public string? summaryfile
    {
        get; set;
    }

    /// <summary>tracefmt's own stdout/stderr — the "Searching for TMF files on path…" /
    /// "Examining &lt;tmf&gt;… N found" narrative used for the symbol-resolution log.</summary>
    public string? ConsoleOutput
    {
        get; set;
    }

    /// <summary>dbghelp's verbose symbol-search trace (the DBGHELP_LOG output), if captured — the
    /// WinDbg "!sym noisy"-style per-path probing/result for the PDBs tracefmt tried to load.</summary>
    public string? SymbolDiagnostics
    {
        get; set;
    }

    public void ParseSummaryFile()
    {
        if (string.IsNullOrEmpty(summaryfile))
        {
            throw new ArgumentNullException(nameof(summaryfile), "Summary file path cannot be null or empty.");
        }

        var maxtries = 10000;
        List<string> summary = new List<string>();

        while (maxtries > 0)
        {
            try
            {
                FileStream x = File.OpenRead(summaryfile);

                using var reader = new StreamReader(x);
                string? line;

                while ((line = reader.ReadLine()) != null)
                {
                    summary.Add(line);
                }

                break;
            }
            catch (Exception)
            {
                Thread.Sleep(100);
                maxtries--;
                //tracefmt is still writing, wait
            }
        }
        if (maxtries == 0)
        {
            throw new Exception("Couldnt open summary file");
        }

        ProcessedFile = summary[1].Trim();
        TotalBuffersProcessed = Int32.Parse(summary[2].Substring(summary[2].LastIndexOf(" ")).Trim());
        TotalEventsProcessed = Int32.Parse(summary[3].Substring(summary[2].LastIndexOf(" ")).Trim());
        TotalEventsLost = Int32.Parse(summary[4].Substring(summary[2].LastIndexOf(" ")).Trim());
        TotalFormatErrors = Int32.Parse(summary[5].Substring(summary[2].LastIndexOf(" ")).Trim());
        TotalFormatsUnknown = Int32.Parse(summary[6].Substring(summary[2].LastIndexOf(" ")).Trim());
        TotalElapsedTime = summary[7].Replace("Elapsed", "").Replace("Time", "").Trim();
    }

    public string? ProcessedFile
    {
    get; set; 
    }

    public int TotalBuffersProcessed
    {
        get; set;
    }

    public int TotalEventsProcessed
    {
        get; set;
    }

    public int TotalEventsLost
    {
        get; set;
    }

    public int TotalFormatErrors
    {
        get; set;
    }

    public int TotalFormatsUnknown
    {
        get; set;
    }

    public string? TotalElapsedTime
    {
        get; set;
    }

}

public class TraceFmt
{
    /// <summary>
    /// Fast decodability pre-scan: run tracefmt over just the first <paramref name="sampleBytes"/> of
    /// the ETL (a copied prefix) instead of the whole file, to estimate how much is formattable BEFORE
    /// committing to the full (slow) decode. tracefmt is processing-bound, so an 8 MB prefix returns in
    /// a fraction of a second yet still reports the events/unknowns ratio and the missing message GUIDs.
    /// Returns counts in the TraceFmtResult (sample-scoped), or null if tracefmt isn't available.
    /// </summary>
    public static TraceFmtResult PreScan(string etl, string temppath, SearchProgressSink? progressSink = null, long sampleBytes = 8L * 1024 * 1024)
    {
        string traceFmtPath;
        try { traceFmtPath = GetRunnableTracefmt(); } catch { return null!; }
        if (string.IsNullOrEmpty(traceFmtPath) || !File.Exists(traceFmtPath) || !File.Exists(etl)) return null!;

        var dir = Path.Combine(temppath, "prescan");
        try
        {
            Directory.CreateDirectory(dir);
            var sample = Path.Combine(dir, "sample.etl");
            using (var fs = File.OpenRead(etl))
            {
                long len = Math.Min(sampleBytes, fs.Length);
                var buf = new byte[len];
                int off = 0, r;
                while (off < len && (r = fs.Read(buf, off, (int)(len - off))) > 0) off += r;
                using var outfs = File.Create(sample);
                outfs.Write(buf, 0, off);
            }

            // Don't let an unset symbol path fall back to "srv*" (network) during the quick pre-scan —
            // pass an empty local dir as -r in that case. TMF search (TRACE_FORMAT_SEARCH_PATH) is
            // inherited regardless, so a configured local TMF/symbol path still resolves.
            var sym = Environment.GetEnvironmentVariable("_NT_SYMBOL_PATH");
            var rArg = string.IsNullOrWhiteSpace(sym) ? dir : sym;

            var st = new ProcessStartInfo
            {
                FileName = traceFmtPath,
                Arguments = $"\"{sample}\" -r \"{rArg}\"",
                WorkingDirectory = dir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            var dbg = EnableDbghelpLog(st, dir);
            var p = Process.Start(st);
            if (p == null) return null!;
            string o = p.StandardOutput.ReadToEnd();
            string er = p.StandardError.ReadToEnd();
            p.WaitForExit();

            var result = new TraceFmtResult
            {
                ConsoleOutput = string.IsNullOrWhiteSpace(er) ? o : (o + Environment.NewLine + er),
                SymbolDiagnostics = ReadDbghelpLog(dbg),
                outputfile = Path.Combine(dir, "FmtFile.txt"),
                summaryfile = Path.Combine(dir, "FmtSum.txt"),
            };
            try { if (File.Exists(result.summaryfile)) result.ParseSummaryFile(); } catch { /* counts stay 0 */ }
            return result;
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"TraceFmt pre-scan failed for {etl}: {ex.Message}");
            return null!;
        }
    }

    public static TraceFmtResult ParseSimpleETL(string etl, string temppath, SearchProgressSink? progressSink = null)
    {
        progressSink?.NotifyProgress(0, $"Starting TraceFmt for {etl}");
        string traceFmtPath = string.Empty;
        try
        {
            traceFmtPath = GetRunnableTracefmt();
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Warning: Could not determine tracefmt path: {ex.Message}");
            progressSink?.NotifyProgress(100, "Warning: TraceFmt (tracefmt.exe) was not found. ETL parsing will be skipped.");
            return null!;
        }
        if (string.IsNullOrEmpty(traceFmtPath) || !File.Exists(traceFmtPath))
        {
            Logger.Instance.Log("Warning: TraceFmt (tracefmt.exe) was not found. ETL parsing will be skipped.");
            progressSink?.NotifyProgress(100, "Warning: TraceFmt (tracefmt.exe) was not found. ETL parsing will be skipped.");
            return null!;
        }
        if (!File.Exists(etl))
        {
            throw new Exception("Cant find etl");
        }
        TraceFmtResult result = new TraceFmtResult();
        ProcessStartInfo st = new ProcessStartInfo
        {
            FileName = traceFmtPath,
            Arguments = etl,
            WorkingDirectory = temppath,
            // Capture tracefmt's narration (which TMF paths it searched, which TMFs it examined/found)
            // for the symbol-resolution log surfaced in the UI.
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        var dbg = EnableDbghelpLog(st, temppath);
        Process? p = Process.Start(st);
        if(p == null)
        {
            throw new Exception("???");
        }
        progressSink?.NotifyProgress(10, "TraceFmt process started");
        string consoleOut = p.StandardOutput.ReadToEnd();
        string consoleErr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        result.ConsoleOutput = string.IsNullOrWhiteSpace(consoleErr) ? consoleOut : (consoleOut + Environment.NewLine + consoleErr);
        result.SymbolDiagnostics = ReadDbghelpLog(dbg);
        progressSink?.NotifyProgress(80, "TraceFmt process finished");
        if(p.ExitCode != 0)
        {
            throw new Exception("exit code was not 0 for tracefmt!");
        }
        result.outputfile = Path.Combine(temppath, "FmtFile.txt");
        result.summaryfile = Path.Combine(temppath, "FmtSum.txt");
        if (!File.Exists(result.outputfile))
        {
            throw new Exception("FmtFile output was not there!");
        }
        if (!File.Exists(result.summaryfile))
        {
            throw new Exception("FmtSum output was not there!");
        }
        progressSink?.NotifyProgress(90, "Parsing summary file");
        result.ParseSummaryFile();
        progressSink?.NotifyProgress(100, "TraceFmt parsing complete");
        return result;
    }

    private static string _runnableTracefmt;
    private static readonly object _assembleLock = new();

    /// <summary>tracefmt.exe co-located with a matched dbghelp + symsrv so the SYMBOL SERVER actually works.
    /// The WDK bin tracefmt's own dir has dbghelp but NO symsrv (dbghelp loads symsrv from its own dir, so the
    /// server can't load — "symsrv.dll load failure"). The WDK Debuggers\x64 ships the matched pair, so we
    /// assemble (once, cached under app data) a dir = tracefmt.exe + the Debuggers dbghelp/symsrv/dbgcore.
    /// All WDK components — nothing is redistributed. Falls back to the plain WDK tracefmt (local TMFs only,
    /// no server) if the Debuggers pair isn't present or assembly fails.</summary>
    private static string GetRunnableTracefmt()
    {
        var plain = WDKFinder.GetTraceFmtPath();
        if (string.IsNullOrEmpty(plain) || !File.Exists(plain)) return plain; // not installed → caller handles
        try
        {
            lock (_assembleLock)
            {
                if (_runnableTracefmt != null && File.Exists(_runnableTracefmt)
                    && File.GetLastWriteTimeUtc(plain) <= File.GetLastWriteTimeUtc(_runnableTracefmt))
                    return _runnableTracefmt;

                var dbgr = WDKFinder.GetDebuggersX64Path();
                if (string.IsNullOrEmpty(dbgr) || dbgr == WDKFinder.NOT_FOUND_STRING
                    || !File.Exists(Path.Combine(dbgr, "symsrv.dll")) || !File.Exists(Path.Combine(dbgr, "dbghelp.dll")))
                    return _runnableTracefmt = plain; // no WDK symsrv available → plain tracefmt (no server)

                var cache = Path.Combine(FileIO.GetAppDataFindNeedlePluginFolder(), "tracefmt-engine");
                Directory.CreateDirectory(cache);
                var dest = Path.Combine(cache, "tracefmt.exe");
                bool stale = !File.Exists(dest) || File.GetLastWriteTimeUtc(plain) > File.GetLastWriteTimeUtc(dest);
                if (stale)
                {
                    File.Copy(plain, dest, true);
                    // tracefmt.exe's ONLY non-system import is dbghelp.dll (verified: its other imports —
                    // tdh, winhttp, ws2_32, advapi32, version, ntdll, CRT — are all in System32). dbghelp in
                    // turn loads symsrv/dbgcore/srcsrv from its own dir. So this is tracefmt's COMPLETE private
                    // dependency closure, not a guess — it won't "break if MS adds a DLL" because tracefmt's
                    // only private dep is the dbghelp family, which we copy whole from the WDK Debuggers.
                    foreach (var dll in new[] { "dbghelp.dll", "symsrv.dll", "dbgcore.dll", "srcsrv.dll", "symsrvcache.dll" })
                    {
                        var s = Path.Combine(dbgr, dll);
                        if (File.Exists(s)) File.Copy(s, Path.Combine(cache, dll), true);
                    }
                    Logger.Instance.Log($"Assembled tracefmt symbol engine at {cache} (WDK bin tracefmt + WDK Debuggers dbghelp family)");
                }
                return _runnableTracefmt = dest;
            }
        }
        catch (Exception ex) { Logger.Instance.Log($"tracefmt engine assemble failed, using plain tracefmt: {ex.Message}"); return plain; }
    }

    /// <summary>Point dbghelp at a log file (and turn its debug output on) for the tracefmt child, so we
    /// capture the WinDbg "!sym noisy"-style per-path symbol search/result. Returns the log path to read
    /// after the child exits. Requires UseShellExecute=false (so the child inherits this env).</summary>
    private static string EnableDbghelpLog(ProcessStartInfo st, string dir)
    {
        var log = Path.Combine(dir, "dbghelp.log");
        try { if (File.Exists(log)) File.Delete(log); } catch { /* stale; will be overwritten */ }
        st.Environment["DBGHELP_LOG"] = log;     // dbghelp writes its symbol-search trace here
        st.Environment["DBGHELP_DBGOUT"] = "1";  // verbose
        return log;
    }

    private static string ReadDbghelpLog(string log)
    {
        try { return File.Exists(log) ? File.ReadAllText(log) : null; }
        catch { return null; }
    }
}
