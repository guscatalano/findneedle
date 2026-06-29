using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// Regression guard for the About → Logs crash: <see cref="Logger"/> is written from many threads
/// (background searches, the off-thread metadata warm), while the Logs page iterates <see cref="Logger.LogCache"/>.
/// A live AsReadOnly() view + unsynchronized List.Add threw "Collection was modified" mid-iteration → crash.
/// LogCache must return a stable snapshot and Log() must be thread-safe.
/// </summary>
[TestClass]
public class LoggerConcurrencyTests
{
    [TestMethod]
    [TestCategory("Storage")]
    public void Log_FromManyThreads_WhileIteratingCache_DoesNotThrow()
    {
        var logger = Logger.Instance;
        var cts = new CancellationTokenSource();
        Exception? captured = null;

        // Writers: hammer Log() from several threads (like concurrent search/decode logging).
        var writers = Enumerable.Range(0, 4).Select(i => Task.Run(() =>
        {
            int n = 0;
            while (!cts.IsCancellationRequested) logger.Log($"thread {i} line {n++}");
        })).ToArray();

        // Reader: do exactly what LogsPage does — iterate the cache — repeatedly, while writers run.
        var reader = Task.Run(() =>
        {
            try
            {
                for (int k = 0; k < 500; k++)
                    foreach (var line in logger.LogCache) { _ = line.Length; }
            }
            catch (Exception ex) { captured = ex; }
        });

        reader.Wait();
        cts.Cancel();
        Task.WaitAll(writers);

        Assert.IsNull(captured, $"iterating LogCache while logging threw: {captured}");
    }
}
