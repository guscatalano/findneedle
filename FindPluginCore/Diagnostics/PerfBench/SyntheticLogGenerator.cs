using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace FindPluginCore.Diagnostics.PerfBench;

/// <summary>
/// Deterministic synthetic log generator for the performance benchmark. Output is a pure function of
/// the row index — <b>byte-identical for a given row count on every machine and run</b> — so the
/// benchmark measures the same data everywhere. No RNG state, invariant culture, fixed epoch, UTF-8
/// (no BOM), LF line endings.
///
/// Line shape mirrors the proven fixture format (<c>[yyyy-MM-dd HH:mm:ss] LEVEL: …</c>) so FindNeedle's
/// text parser reads timestamp + level, then adds a token distribution the runner uses for the two
/// search modes:
///   • a <see cref="CommonToken"/> on every line  → a "matches (nearly) everything" worst-case query;
///   • a rare <see cref="RareTokenPrefix"/>k planted every <see cref="RareTokenEvery"/> rows → a
///     selective query that hits few rows (exercises the FTS index vs. a full scan).
/// Timestamps advance 1 s/row over a fixed window, so a time-scope scenario keeps a deterministic
/// fraction of rows.
/// </summary>
public static class SyntheticLogGenerator
{
    /// <summary>Bump if the line format changes (invalidates byte-for-byte comparability).</summary>
    public const int DatasetVersion = 1;

    public const long RareTokenEvery = 50_000;      // NEEDLE_k cadence (selective query)
    public const string RareTokenPrefix = "NEEDLE_";
    public const string CommonToken = "entry";       // on every line (worst-case query)

    private static readonly DateTime Start = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly string[] Words =
    {
        "request", "response", "cache", "connection", "timeout", "retry", "session", "buffer",
        "packet", "thread", "handle", "socket", "queue", "commit", "flush", "token",
    };

    /// <summary>Write <paramref name="rows"/> deterministic lines to <paramref name="path"/>.</summary>
    public static void Write(string path, long rows)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var w = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1 << 20);
        var sb = new StringBuilder(160);
        for (long i = 0; i < rows; i++)
        {
            sb.Clear();
            sb.Append('[')
              .Append(Time(i).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
              .Append("] ").Append(LevelName(i)).Append(": ");
            AppendMessage(sb, i);
            w.Write(sb.ToString());
            w.Write('\n'); // explicit LF — never Environment.NewLine (would break determinism cross-platform)
        }
    }

    /// <summary>The deterministic timestamp of row <paramref name="i"/> (1 s/row over a fixed window).</summary>
    public static DateTime Time(long i) => Start.AddSeconds(i);

    /// <summary>The message body of row <paramref name="i"/> (the part after "LEVEL: "), for the
    /// direct-to-storage runner path — same content the file line carries.</summary>
    public static string Message(long i)
    {
        var sb = new StringBuilder(96);
        AppendMessage(sb, i);
        return sb.ToString();
    }

    private static void AppendMessage(StringBuilder sb, long i)
    {
        sb.Append(CommonToken).Append(' ')
          .Append(Word(i, 0)).Append(' ').Append(Word(i, 1)).Append(' ').Append(Word(i, 2))
          .Append(" line ").Append(i);
        if (i % RareTokenEvery == 0)
            sb.Append(' ').Append(RareTokenPrefix).Append(i / RareTokenEvery);
    }

    /// <summary>Rows a selective <c>NEEDLE_</c> query should match, for a given size.</summary>
    public static long RareTokenCount(long rows) => rows <= 0 ? 0 : ((rows - 1) / RareTokenEvery) + 1;

    /// <summary>Deterministic, index-derived level name (order matters — first match wins).</summary>
    public static string LevelName(long i)
    {
        if (i % 20 == 0) return "ERROR";
        if (i % 7 == 0) return "WARN";
        if (i % 13 == 0) return "VERBOSE";
        return "INFO";
    }

    private static string Word(long i, int slot)
    {
        unchecked
        {
            // Knuth multiplicative hash of (index, slot) — deterministic, well-spread, no RNG state.
            ulong h = (ulong)i * 2654435761UL + (ulong)(slot + 1) * 40503UL;
            return Words[(int)(h % (ulong)Words.Length)];
        }
    }
}
