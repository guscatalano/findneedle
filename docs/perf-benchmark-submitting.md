# Running & submitting the FindNeedle performance benchmark

FindNeedle ships a built-in benchmark so anyone can measure how it performs on *their* machine and
compare against a published reference — a **community benchmark**. This page is the contributor + author
how-to. Design details live in `perf-benchmark-design.md`.

## Run it (contributors)

1. Open FindNeedle → **Diagnostics → Performance benchmark**.
2. Pick a workload:
   - **Quick** — 100k + 1M rows (~30 s)
   - **Full** — up to 5M rows (~1–2 min, ~350 MB temp)
   - **Stress** — up to 10M rows (~several min, ~700 MB temp)
   - Close other apps for cleaner numbers — the report records the CPU already in use so a busy-machine
     run stays interpretable, but a quiet machine is best.
3. Click **Run benchmark**. When it finishes:
   - **Open HTML report** — the human-readable results.
   - **Show JSON to submit** — opens the result file in Explorer.

The synthetic log is generated on your machine and deleted afterward. The result file contains **only
hardware specs + timings — no log content, file paths, or usernames** (see the schema in
`FindPluginCore/Diagnostics/PerfBench/PerfBenchResult.cs`), so it's safe to share.

## Submit it

Attach the JSON (`%LocalAppData%\FindNeedle\perfbench\findneedle-perfbench-*.json`) to the
**"Benchmark submissions" GitHub Discussion**. A short note on your machine (CPU, SSD vs HDD, laptop on
battery vs plugged in) helps, though the JSON already captures the hardware.

Copy/paste template for the discussion post:

```
**Machine:** <CPU> · <cores> cores · <RAM> GB · <SSD/HDD> · <plugged-in/battery>
**Preset:** Quick / Full / Stress
**Notes:** <anything unusual — antivirus, VM, other heavy apps running, etc.>

<attach the findneedle-perfbench-*.json file>
```

## What's comparable

- **Ratios** (`ftsVsScan`, `µs/row`) are **hardware-invariant** — they measure the software and aggregate
  across every submission regardless of machine.
- **Milliseconds** are **per-machine** — only compare them within a similar hardware class, and discount
  runs whose `systemLoad` shows the machine was busy.
- Only submissions with the **same `benchmarkVersion`** are comparable (the field gates it).

## Aggregate submissions (author)

Download the attached JSONs into a folder and run:

```powershell
./tools/perfbench/aggregate.ps1 -Dir ./submissions -BenchmarkVersion 1
```

It prints cross-machine ratios (comparable across all hardware) and absolute 1M-ingest ms bucketed by
machine (comparable only within a hardware class), each with the idle-CPU it ran at.

## Publishing the reference run (author)

The reference is your canonical machine's run, published so contributors have something to compare to:

1. Run the benchmark (Full is a good reference workload) on the reference machine.
2. Commit the JSON to `tools/perfbench/reference/` and link its HTML report from the Discussion's first
   post. Re-publish whenever `benchmarkVersion` changes (older references stop being comparable).
