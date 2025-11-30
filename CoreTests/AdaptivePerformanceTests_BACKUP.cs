// This is a minimal working version - the original file was corrupted
// You need to restore from Git or implement the missing methods:
// - Helper classes: PerformanceDataPoint, StressTestDataPoint, TestResults
// - Export methods: ExportToCsv, ExportStressTestToCsv, ExportComparativeResultsToCsv
// - HTML generation: GeneratePlotlyHtml, GenerateStressTestPlotlyHtml, GenerateComparativeHtml
// - Statistics: GetMedian, GetStandardDeviation
// - Variables: totalGetStatisticsTimeMs, getStatisticsCallCount
//
// CRITICAL BUG FOUND:
// In StressTest_UntilSevereDegradation warmup phase (around line 257):
// writeTimings.Add(sw.Elapsed.TotalMilliseconds);  // DUPLICATE - REMOVE THIS LINE
// var writeTime = sw.Elapsed.TotalMilliseconds;
// writeTimings.Add(writeTime);  // Keep only this one
//
// This bug causes each write time to be added TWICE, corrupting all statistics!
