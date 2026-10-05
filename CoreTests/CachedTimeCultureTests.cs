using System;
using System.Globalization;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// Cached rows store their timestamp with ToString("o") and read it back with
/// DateTime.Parse(s, null, DateTimeStyles.RoundtripKind) - a null provider, so the CURRENT culture.
/// That looks like a bug waiting to happen: on a machine set to Thai (Buddhist calendar) or Saudi
/// (Umm al-Qura), "2026" is not 2026 CE, and every row read out of a cached search would carry a date
/// centuries off - on that user's machine only, which is the worst kind.
///
/// It was measured, and it does NOT happen: with RoundtripKind the framework parses ISO-8601 by the
/// format, not by the culture's calendar, so the round-trip is exact under every culture tried. This
/// test exists to keep that true, because the safety lives in the FORMAT. Change the stored value to
/// anything culture-sensitive - a bare "yyyy-MM-dd HH:mm:ss", a ToString() with no format - and the
/// null provider becomes the bug it looks like.
/// </summary>
[TestClass]
[TestCategory("Storage")]
public class CachedTimeCultureTests
{
    private static void InCulture(string name, Action body)
    {
        var prev = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo(name);
            body();
        }
        finally { Thread.CurrentThread.CurrentCulture = prev; }
    }

    [TestMethod]
    public void CachedTimestamps_RoundTripUnderAnyCulture()
    {
        var original = new DateTime(2026, 10, 4, 9, 0, 1, DateTimeKind.Utc);
        var stored = original.ToString("o");   // exactly what SqliteStorage writes

        foreach (var culture in new[] { "th-TH", "ar-SA", "de-DE", "fa-IR", "en-US" })
        {
            InCulture(culture, () =>
            {
                // The production read path: null provider, i.e. the current culture.
                var asProduction = DateTime.Parse(stored, null, DateTimeStyles.RoundtripKind);
                Assert.AreEqual(original, asProduction, $"the cache read path shifted the timestamp under {culture}");
                Assert.AreEqual(DateTimeKind.Utc, asProduction.Kind, $"kind was lost under {culture}");

                // And the same string parsed invariantly, so a divergence between the two is caught here
                // rather than on one user's machine.
                var asInvariant = DateTime.Parse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                Assert.AreEqual(asInvariant, asProduction, $"culture-sensitive parse diverged under {culture}");
            });
        }
    }
}
