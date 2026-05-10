# Sample logs

Synthetic plain-text log files for stress-testing the result viewers.

The format matches `FindNeedleRuleDSL/Examples/sample.log`:

```
[YYYY-MM-DD HH:MM:SS] LEVEL: message text
```

## Generate

```cmd
python Samples\generate_large_sample.py
```

Defaults: 500,000 lines (~35 MiB) at `Samples\large-sample.log`.

Options:
- `--lines N` — number of lines (default 500_000)
- `--out path` — output path (default `Samples\large-sample.log`)
- `--start ISO` — start timestamp (default `2026-04-01T00:00:00`)
- `--seed N` — RNG seed for reproducibility (default 0)

Examples:

```cmd
:: 50k lines, useful for quick UI checks
python Samples\generate_large_sample.py --lines 50000 --out Samples\medium-sample.log

:: 2M lines, true stress test
python Samples\generate_large_sample.py --lines 2000000 --out Samples\huge-sample.log
```

## Distribution

Messages are randomly drawn from per-level template pools to exercise filters
and search. Approximate severity distribution:

| Level    | %  | What it tests          |
|----------|----|------------------------|
| INFO     | 70 | bulk happy-path traffic |
| WARNING  | 15 | retries, slow queries, capacity |
| ERROR    | 10 | timeouts, auth failures, HTTP 5xx |
| CRITICAL |  5 | OOM, stack overflow, disk full |

About 20 distinct services and 24 distinct users rotate through the templates
so per-column filters (Provider/TaskName/Message) can meaningfully shrink the
result set.

## Git

The generated `.log` files are gitignored — only the script lives in source.
