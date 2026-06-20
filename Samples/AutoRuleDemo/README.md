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

## UML diagram sample

`process-interaction-demo.log` draws a **Mermaid sequence diagram** of how processes hand off work
(client → broker → worker → store). It's powered by two bundled rules in `FindNeedleUX/CommonRules/`:
`process-interaction.rules.json` (emits the diagram) and `process-interaction-uml.rules.json` (the
participants + message rules).

1. **(To render an image)** open **Configure ▸ UML / Diagram Tools** and install **Mermaid**.
   Without it you still get the diagram as Mermaid text.
2. Open **Configure ▸ Auto-add rules**, master switch **On**, and enable
   **"Process interaction diagram"** (it also auto-adds for any path matching `*process-interaction*`).
3. Load `process-interaction-demo.log` and run the search.
4. The diagram is written to the output folder as a `.mmd` (and a rendered image if Mermaid is
   installed); view it under **Run & Results ▸ Processor Output**.

The generated diagram looks like:

```
Client ->> Broker : request (req=42)
Broker ->> Worker : dispatch job (worker pid=7880)
Worker ->> Store  : write result
Store  ->> Worker : persisted job 42
Worker ->> Broker : result
Broker ->> Client : response
```

## Condition-based matching

To see a non-`Always` rule auto-add by **file type**, add your own rule file
(Configure ▸ Auto-add rules ▸ *Add rule file…*) and set its condition to e.g. extension `.log` or a
path glob like `*auto-rule-demo*`; it will then auto-add whenever this file is loaded.
