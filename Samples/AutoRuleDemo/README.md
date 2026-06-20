# Auto-add rules demo

`auto-rule-demo.log` is a small plain-text log for exercising the **auto-add rules** feature
(Configure ▸ Auto-add rules).

## Try it

1. Open **Configure ▸ Auto-add rules** and turn the master switch **On**.
2. Enable the built-in **"Hide debug / verbose noise"** rule (condition: *Always* — it applies to
   every search).
3. Load `auto-rule-demo.log` (Logs ▸ Open log file…).
4. The `DEBUG` / `TRACE` / `VERBOSE` lines are filtered out automatically (the rule excludes them,
   except lines that also look like errors). The rule shows up as auto-added for the search.

The file also contains crash markers (`access violation (0xc0000005)`, `crash detected`) so the
built-in **"ETW crash / fault highlighter"** rule has something to surface — that rule's condition is
`.etl` / ETW, so point it at an `.etl` capture to see it match by file type.

## Condition-based matching

To see a non-`Always` rule auto-add by **file type**, add your own rule file
(Configure ▸ Auto-add rules ▸ *Add rule file…*) and set its condition to e.g. extension `.log` or a
path glob like `*auto-rule-demo*`; it will then auto-add whenever this file is loaded.
