# FindNeedle — Who Uses It & What They're Trying to Do
### A plain-English version (for design conversations, no technical background needed)

> This is the friendly companion to `personas-and-use-cases.md`. Same people, same jobs — but with
> the jargon translated into everyday language. If you're a designer coming in fresh, **start here.**

---

## The one-sentence version

**FindNeedle helps people search and make sense of "logs" — the detailed diaries that software writes
about what it's doing — especially when those diaries are huge, cryptic, or come in a dozen different
formats.** Think of it as a super-powered "Find in Files" built for the messiest, largest, most
technical records a computer produces.

Why it exists: when something on a Windows computer breaks, the answer is usually buried somewhere in
these records — but they can be millions of lines long, written in shorthand, and scattered across many
files. Regular tools (Notepad, search) fall over. FindNeedle is for finding that one needle.

---

## A tiny glossary (only the words you'll actually need)

| Word | In plain English |
|------|------------------|
| **Log** | A diary a program keeps: a list of timestamped things that happened ("10:02 — tried to open port 8080 — failed"). |
| **Event** | One line/entry in that diary — one thing that happened, with a time. |
| **Trace** | A *very* detailed log, usually recorded while reproducing a problem. Can be enormous (tens of millions of events). |
| **Provider / source** | *Who wrote the entries.* One trace can contain entries from many different components — like a group chat with many participants. |
| **Level** | How serious an entry is: Error, Warning, Info, etc. (like severity tags). |
| **Filter** | Narrowing the giant list down to just what you care about (by word, time, severity, who wrote it). |
| **Rule** | A saved "recipe" that automatically tags, hides, or pulls out things you always look for — so you don't redo it by hand. |
| **Symbols** | The "decoder ring." Some traces are written in shorthand codes; symbols translate them into readable text. |
| **Crash dump** | A snapshot of a computer's memory taken at the moment it crashed. Sometimes the only evidence left. |
| **AI agent (MCP)** | Letting an AI assistant do the searching and first-pass analysis for you, hands-free. |

That's it. If a persona mentions something else technical, you can safely treat it as "a kind of log."

---

## The people who use FindNeedle

Six composite users. They're not real individuals — they're patterns of real needs. Each card is about
*what they want and how they feel*, not the technology.

### 🧑‍🔧 Priya — the deep specialist
**Who:** An engineer who builds the low-level guts of Windows (drivers). She lives in the most detailed,
most cryptic traces there are.
**What she wants:** To follow one thread of activity through a massive recording and find *the* moment it
went wrong — fast.
**What drives her nuts:** Waiting minutes to load a giant file when she only needs a sliver of it. Tools
that "just work" but won't tell her *how* — she needs to see exactly what the tool tried and where it got
stuck.
**A good day:** She opens a trace, loads only the two participants she cares about in seconds, sees the
shorthand translated to readable text, and follows the thread end-to-end — ideally as a simple diagram.
> *"Don't show me 40 million things I'll never read. Show me my two, and tell me exactly what you
> couldn't decode."*

### 🎧 Marcus — the firefighter
**Who:** A support/escalation engineer. Customers send him big bundles of logs when something broke, and
he's got a stack of cases and a ticking clock.
**What he wants:** Drop the whole messy bundle in, run one search, and find the failure and what led to it.
**What drives him nuts:** Every log is a different format; he ends up hand-searching files one by one; and
he re-does the exact same detective work for every similar case with nothing saved.
**A good day:** One search covers every file at once; he filters to the failure; and a saved recipe makes
the *next* identical case a single click.
> *"I have 20 cases in the queue. I don't want to become an expert — I want to open the bundle, find the
> error, and see what happened right before it."*

### 🤖 Elena — the automator
**Who:** A test/automation engineer. Her systems run thousands of tests; every failure spits out logs, and
she wants the analysis to happen *automatically and consistently*, not in someone's head.
**What she wants:** To capture "what a good analyst would look for" as reusable recipes, run them
automatically on every failure, and hand engineers a pre-sorted, highlighted result — or let an AI do the
first pass.
**What drives her nuts:** Analysis knowledge trapped in people's heads; results that can't be reproduced;
no clean way for a script or an AI to drive the tool.
**A good day:** A tidy set of saved recipes runs on every failure and highlights the known problems, and an
AI assistant can open a result and summarize it.
> *"If a person can recognize this failure, I can write a recipe for it — and never do it by hand again."*

### 👩‍💻 Sam — the everyday developer
**Who:** A developer debugging his own app. He knows his own logs; anything fancier is a distraction.
**What he wants:** Open today's log, jump to the red error line, read the few lines around it, move on.
**What drives him nuts:** Normal editors choke on big logs and can't filter by severity or time. Anything
that opens with a wall of columns and controls feels like overkill.
**A good day:** Double-click the file → results appear → click "Error" or type a word → read → done. Zero
setup.
> *"I just want to open the file and find the red line. I don't want a whole workflow."*

### 📊 Dana — the scale wrangler
**Who:** A performance engineer working with *enormous* recordings — hours long, tens of millions of
entries, dozens of participants.
**What she wants:** To peek inside a huge file first — who's in it, how big each part is, what time span —
then load only the slice worth looking at.
**What drives her nuts:** Loading everything takes forever and eats memory; she wastes time loading parts
she'll throw away; she can't see "how big is each part" before committing.
**A good day:** She previews the file, sees the parts and their sizes, picks the two she needs, and the
load is a fraction of the whole — then searching within it is instant.
> *"Tell me what's in the file and how big each part is *before* I wait five minutes to load all of it."*

### 🛡️ Raj — the investigator *(secondary)*
**Who:** A security/incident responder piecing together "what happened, and when" from mixed evidence —
system records, network captures, sometimes a crash snapshot.
**What he wants:** Put every piece of evidence on one timeline, zoom to the moment of the incident, and
export it as proof.
**A good day:** All the evidence in one time-ordered view, filtered to the incident window, ready to attach
to a report.

---

## What they're trying to get done (the top 10 jobs)

Each one is a short story: the situation, why it matters, what "good" feels like, and what's clunky today.
(These map 1-to-1 to UC1–UC10 in the technical doc.)

**1. Open a log and find the problem** — *Sam, Marcus, Elena*
Open a file, get to the error with zero setup, read the lines around it. → *Good:* under ten seconds, no
configuration. *Clunky:* the results screen can feel busy on first open (we've been calming this down).

**2. Peek inside a giant file, then load only the useful slice** — *Dana, Priya*
Before loading a massive recording, see who's in it and how big each part is, and choose just the parts you
need. → *Good:* the preview is complete and the trimmed load is fast and reversible. *Clunky:* recently the
preview under-counted parts on big files (now fixed).

**3. Translate a cryptic trace into readable text** — *Priya*
Take a shorthand recording and turn it into plain messages — and show clearly which bits could and couldn't
be decoded. → *Good:* readable results plus an honest "here's what I tried and what worked." *Clunky:* the
decoder setup is inherently fiddly.

**4. Search one query across a whole messy bundle** — *Marcus, Raj*
Drop in a folder or zip of many differently-formatted logs and search all of them at once. → *Good:* one
search spans every file; the error and its context show together. *Clunky:* there are two different "add
your files" screens that need clearer signposting.

**5. Put everything on one timeline** — *Marcus, Dana, Raj*
Combine several sources and read them in time order; zoom to the moment of interest. → *Good:* one correct,
time-ordered view. *Clunky (real gap):* there's **no visual timeline/"activity over time" bar** in the app
yet — you sort and set a time window by hand. This is a strong opportunity.

**6. Slice the results down to what matters** — *everyone*
Once a log is loaded, narrow it: by word, by who wrote it, by severity, by time. → *Good:* fast even on
millions of entries; the controls are easy to find and it's obvious when a filter is on. *This is where
people spend the most time* — so it deserves the most polish.

**7. Save "what I always look for" as a reusable recipe** — *Elena, Marcus*
Turn recurring detective work into a saved recipe that auto-tags or extracts things every time. → *Good:*
reproducible; a new pattern is a recipe edit, not a coding project. *Clunky:* the recipe screen is
expert-only today — needs a friendlier on-ramp (a starter template, a live "would this match?" preview).

**8. Recover evidence from a crash snapshot** — *Priya, Raj*
When no recording was running, pull the hidden records out of a crash snapshot. → *Good:* evidence
recovered when there'd otherwise be none. *Niche but high-value.*

**9. Turn a sequence of events into a picture** — *Priya, Elena*
Instead of ten thousand lines, show "who talked to whom, in what order" as a simple diagram. → *Good:* a
readable picture built from the actual events. *Clunky:* today you have to hand-write the recipe for it.

**10. Let an AI assistant do the first pass** — *Elena*
Hand the searching, filtering, and summarizing to an AI assistant — hands-free or alongside you. → *Good:*
a repeatable first-pass triage; you and the assistant see the same thing. *Clunky:* it's powerful but
hidden and hard to discover (even the on/off button is a cryptic acronym).

---

## Who cares about what (at a glance)

| Job | Priya | Marcus | Elena | Sam | Dana | Raj |
|-----|:--:|:--:|:--:|:--:|:--:|:--:|
| 1. Open & find the problem | · | ● | · | ● | · | · |
| 2. Peek & load only a slice | ● | · | | | ● | |
| 3. Decode a cryptic trace | ● | | | | · | |
| 4. Search a whole bundle | | ● | · | | | ● |
| 5. One timeline | · | ● | · | | ● | ● |
| 6. Slice the results | ● | ● | ● | ● | ● | ● |
| 7. Save a reusable recipe | | ● | ● | | | · |
| 8. Recover from a crash snapshot | ● | | | | | ● |
| 9. Make a picture of a sequence | ● | | ● | | | |
| 10. Let an AI do the first pass | | · | ● | | · | |

● = a big deal for them · = nice to have

---

## The biggest opportunities (where a designer can help most)

In plain terms, ranked by how many people it helps:

1. **Make "slicing the results" (job 6) effortless and obvious** — it's where everyone spends the most time.
2. **Reassure people during long loads (job 1/2)** — show real progress ("scanned 1.2M rows, almost there")
   instead of a spinner that looks frozen.
3. **Add a visual timeline (job 5)** — a little "activity over time" bar you can click to zoom. The data's
   already there; there's just no picture yet.
4. **A friendly on-ramp for recipes (job 7)** — templates and a live "this would match 1,240 rows" preview,
   so the powerful part isn't expert-only.
5. **Give newcomers an obvious first step (job 1)** — a clear "Open a log" starting point instead of hunting
   through menus.

There's a fuller, prioritized list in `ux-backlog.md`, and before/after picture-mockups of each proposed
change in the image `ba-mockups.png`.

---

## How to use this with your designer

- **Read the people first**, then the ten jobs. Ask: for each job, *what would make this feel effortless?*
- Don't worry about the technology — if a term is unfamiliar, it's almost certainly "a kind of log," and
  the glossary covers the rest.
- The most valuable design work is **jobs 1, 5, 6, and 7** — they touch the most people and have the most
  room to feel better.
- Everything here is a *draft based on how the tool behaves today* — the best next step is to sanity-check
  it against a couple of real users and adjust.
