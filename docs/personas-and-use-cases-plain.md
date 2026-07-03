# FindNeedle — Who Uses It & What They're Trying to Do
### A plain-English version (for design conversations, no technical background needed)

> The friendly companion to `personas-and-use-cases.md`. Same people, same jobs — jargon translated into
> everyday language. If you're a designer coming in fresh, **start here.**
>
> **What's new (v1.4):** we trimmed six made-up people down to **four**, plus **two we're betting on**.
> More importantly, we now know **one of them is real** — Priya is actually the person who *built* the
> tool, so her card isn't a guess anymore. And a second (Marcus) is backed up by that same person's real
> day-job experience. The rest are still educated guesses. We mark which is which, because pretending we
> know is how design goes wrong.

---

## The one-sentence version

**FindNeedle helps people search and make sense of "logs" — the detailed diaries software writes about
what it's doing — especially when those diaries are huge, cryptic, or come in a dozen different formats.**
Think "super-powered Find-in-Files" for the messiest, largest, most technical records a computer produces.

Why it exists: when something on a Windows computer breaks, the answer is usually buried in these records —
millions of lines, written in shorthand, scattered across many files. Notepad and normal search fall over.
FindNeedle is for finding that one needle. The two hardest versions of that: **"where in this pile did it
go wrong?"** and **"in what order did the events unfold to cause this?"**

---

## A tiny glossary (only the words you'll actually need)

| Word | In plain English |
|------|------------------|
| **Log** | A diary a program keeps: timestamped things that happened ("10:02 — tried to open port 8080 — failed"). |
| **Event** | One line in that diary — one thing that happened, with a time. |
| **Trace** | A *very* detailed log, recorded while reproducing a problem. Can be enormous (tens of millions of events). |
| **Bundle** | A pile of many different logs, zipped together — what a customer sends when something broke. |
| **Level** | How serious an entry is: Error, Warning, Info (severity tags). |
| **Filter** | Narrowing the giant list to just what you care about (by word, time, severity, who wrote it). |
| **Recipe (a "rule")** | A saved set of instructions that automatically tags, hides, or pulls out the things you always look for — so you don't redo it by hand. **This is the heart of the tool.** |
| **Flow picture (a "diagram")** | Instead of 10,000 lines, a simple picture of *who did what, in what order* — A called B, then B called C, and C failed. |
| **AI assistant** | Letting an AI do the searching — and, the big new idea, **write the recipe and draw the flow picture for you**. |

If a persona mentions anything else technical, safely treat it as "a kind of log."

---

## The people who use FindNeedle

Four main people, plus two we're betting on. Each card says *what they want and how they feel* — and,
new this round, **how sure we are they're real.**

### 🧑‍🔧 Priya — the deep specialist  ✅ *this one is real (she built the tool)*
**Who:** An engineer who works on the low-level guts of Windows. She lives in the most detailed, most
cryptic traces there are.
**What she's *actually* doing (8 times out of 10):** reading through a log, noting the **times** and the
**order** things happened, and reconstructing **how the failure unfolded** — like a chain: *A called B,
then B called C, and while that was happening a fourth thing barged in and made C fail.*
**What drives her nuts:** doing that reconstruction **by hand is tedious** — copying down timestamps,
hand-tracing the order. That tedium is the whole reason she built the "recipe" idea: to have the tool find
those events automatically and **draw the flow picture** for her.
**The honest truth (her words):** *"It doesn't make my job easier yet."* The recipe-and-picture idea
exists, but you still have to hand-write the recipe — so the tedium isn't actually gone. **Fixing that is
the single most important thing in this whole document.**
> *"I go through a log noting times and sequences to figure out how the error happened. It's tedious —
> that's why I built the recipe idea, to do that for me and draw the diagram."*

### 🎧 Marcus — the firefighter  ◐ *backed up by real experience*
**Who:** A support engineer. Customers send big **bundles** of logs when something broke; he has a stack
of cases and a ticking clock.
**What he's *actually* doing:** he gets a bundle he didn't write, and **he doesn't know *where* in the
pile the problem is.** The job is to *locate* it — one search across every file to find where and when it
went wrong. (This is the literal "find the needle.")
**What drives him nuts:** every log is a different format; he ends up hand-searching files one by one; and
he redoes the same detective work for every similar case with nothing saved.
**A good day:** one search covers every file at once; he narrows to the failure; and a saved recipe makes
the *next* identical case a single click.
> *"A lot of the time we get a bundle of logs, and we don't know where in the logs the issue happened."*
> *(This one's real — it's the author's own day-job experience.)*

### 🤖 Elena — the automator  ○ *educated guess*
**Who:** A test/automation engineer. Her systems run thousands of tests; every failure spits out logs, and
she wants the analysis to happen *automatically and consistently*, not live in someone's head.
**What she wants:** capture "what a good analyst looks for" as reusable recipes, run them automatically on
every failure, and hand engineers a pre-sorted, highlighted result — or let an AI do the first pass.
**What drives her nuts:** analysis knowledge trapped in people's heads; results that can't be reproduced;
no clean way for a script or an AI to drive the tool.
> *"If a person can recognize this failure, I can write a recipe for it — and never do it by hand again."*

### 👩‍💻 Sam — the everyday developer  ○ *educated guess*
**Who:** A developer debugging his own app. He knows his own logs; anything fancier is a distraction.
**What he wants:** open today's log, jump to the red error line, read the few lines around it, move on.
**What drives him nuts:** normal editors choke on big logs and can't filter by severity or time; a wall of
columns and controls feels like overkill.
**A good day:** double-click → results appear → click "Error" or type a word → read → done. Zero setup.
> *"I just want to open the file and find the red line. I don't want a whole workflow."*
**Why we keep him even though he's the least intense:** he's the *counterweight* — he's why the tool has to
stay calm and simple by default, instead of turning into a cockpit for the experts.

> *(Two people from the old version were folded in: **Dana the scale-wrangler** — the "huge files" needs —
> is now part of Priya; **Raj the investigator** — mixed evidence, timelines — is now part of Marcus. Same
> needs, fewer cards.)*

---

## The big idea: the "recipe" spectrum, and the AI shortcut

Here's the insight that ties everything together — worth reading even if you skip the rest.

The tool's real magic is: **turn a messy log into a recipe → and turn that recipe into a flow picture.**
The catch is *who writes the recipe*. People line up on a spectrum by how comfortable they are with that:

| Can't write a recipe | Writes it by hand (but it's tedious) | Wants an AI to write it | Writes them like code |
|---|---|---|---|
| **Beginner** 🐣 *(a bet)* | **Priya** 🧑‍🔧 *(real)* | **AI-Driven** 🪄 *(a bet)* | **Elena** 🤖 *(guess)* |
| just wants the answer, no learning | wants the tedium gone | "here's a log — you figure out the recipe and draw the picture" | wants them saved, versioned, repeatable |

**The two we're betting on (not yet proven, but promising):**
- **🐣 The Beginner** — someone reading a log who *doesn't know how to write a recipe* (and maybe doesn't
  know recipes exist). They want the payoff — the flow picture, the "how did this happen" — without having
  to learn anything.
- **🪄 The AI-Driven analyst** — someone who lets an **AI read the log, write the recipe, and draw the
  picture**, then just *reviews and corrects* it. Hands-off authoring.

**The shortcut that serves everyone at once (the single most valuable thing we could build):**
> If an **AI can read the log, write the recipe, and draw the flow picture** for you, then —
> - the **Beginner** gets the answer without learning anything,
> - **Priya's** tedium disappears (that's her #1 complaint, *"it doesn't make my job easier yet"*), and
> - the **AI-Driven** person's whole way of working simply *is* this.
>
> One capability, three people helped, and it fixes the one real user's biggest gripe. That's why it's the
> top bet. **The one catch:** people have to *trust* the AI's recipe — if it's wrong or opaque, everyone
> goes back to doing it by hand.

---

## What they're trying to get done (the top jobs)

Short stories: the situation, why it matters, what "good" feels like, what's clunky today. (These map to
UC1–UC13 in the technical doc.)

**1. Open a log and find the problem** — *Sam, Marcus*
Open a file, get to the error with zero setup, read the lines around it. → *Good:* under ten seconds, no
configuration. *Clunky:* the results screen can feel busy on first open (we've been calming it down).

**2. Peek inside a giant file, then load only the useful slice** — *Priya (big-file mode)*
Before loading a massive recording, see who's in it and how big each part is, and choose just the parts you
need. → *Good:* the preview is complete; the trimmed load is fast and reversible.

**3. Translate a cryptic trace into readable text** — *Priya*
Turn shorthand into plain messages, and show which bits could and couldn't be decoded. → *Note:* it turns
out this is **plumbing, not the point** — Priya needs readable text, but it's a means to the real job
(reconstructing the sequence), not the goal.

**4. Search one query across a whole messy bundle** — *Marcus*
Drop in a folder/zip of many differently-formatted logs and search all at once — because *you don't know
where the problem is*. → *Good:* one search spans every file; the error and its context show together.

**5. Put everything on one timeline** — *Marcus, Priya*
Combine several sources, read them in time order, zoom to the moment of interest. → *Good:* one correct,
time-ordered view, with a clickable "activity over time" bar (this one shipped).

**6. Slice the results down to what matters** — *everyone*
Narrow a loaded log by word, who wrote it, severity, time. → *Good:* fast even on millions of entries;
controls easy to find; obvious when a filter is on. **This is where people spend the most time — most
polish goes here.**

**7. Save "what I always look for" as a reusable recipe** — *Priya, Elena, Marcus* → **the heart of it**
Turn recurring detective work into a saved recipe that auto-tags/extracts. → *Good:* reproducible; a new
pattern is a recipe edit, not a coding project. *Clunky:* **writing the recipe is still expert-only and
tedious — the exact gap the AI shortcut above would close.**

**8. Recover evidence from a crash snapshot** — *Priya, Marcus (forensic)*
When no recording was running, pull the hidden records out of a crash snapshot. → *Niche but high-value.*

**9. Turn a sequence of events into a flow picture** — *Priya, Elena* → **Priya's actual goal**
Instead of ten thousand lines, show "who did what, in what order" as a picture. → *Good:* a readable
picture built from the real events. *Clunky:* today you have to hand-write the recipe for it — which is
exactly why it doesn't save Priya any effort yet.

**10. Let an AI assistant do the first pass** — *Elena, and the AI-Driven bet*
Hand the searching, filtering — and ideally the *recipe-writing and picture-drawing* — to an AI. → *Good:*
a repeatable first pass; you and the assistant see the same thing. *Clunky:* it's powerful but hidden and
hard to discover.

**Plus two everyday ones the numbers say we under-rated:** *reusing a previous search or saved setup* (all
four people do this constantly), and *"why is this slow?"* (Priya, when a big file drags).

---

## Who cares about what (at a glance)

| Job | Priya ✅ | Marcus ◐ | Elena ○ | Sam ○ |
|-----|:--:|:--:|:--:|:--:|
| 1. Open & find the problem | · | ● | · | ● |
| 2. Peek & load only a slice | ● | · | | |
| 4. Search a whole bundle | | ● | · | |
| 5. One timeline | ● | ● | · | |
| 6. Slice the results | ● | ● | ● | ● |
| 7. Save a reusable recipe | ● | ● | ● | |
| 9. Make a flow picture | ● | | ● | |
| 10. Let an AI do the first pass | · | · | ● | |

● = a big deal for them · = nice to have · (✅ real / ◐ backed up / ○ educated guess)
*(The two bets — Beginner and AI-Driven — sit on the "recipe" spectrum above; we've kept them out of this
table until we know they're real.)*

---

## The biggest opportunities (where a designer can help most)

Ranked by leverage — how many people it helps, and whether it fixes something we *know* is broken:

1. **Let the AI write the recipe and draw the picture (jobs 7 + 9 + 10).** The single highest-value idea:
   it helps the beginner, removes the one real user's tedium, and is the whole point for the AI-driven
   person. Design challenge: make the AI's recipe **trustworthy and easy to correct**, not a black box.
2. **Make "slicing the results" (job 6) effortless and obvious** — it's where everyone spends the most time.
3. **A friendly on-ramp for recipes (job 7)** — templates, and a live "this would match 1,240 rows"
   preview, so the powerful part isn't expert-only (a stepping stone toward #1).
4. **Reassure people during long loads (jobs 1/2)** — show real progress ("scanned 1.2M rows, almost
   there") instead of a spinner that looks frozen.
5. **Give newcomers an obvious first step (job 1)** — a clear "Open a log" starting point, not a menu hunt.

There's a fuller, prioritized list in `ux-backlog.md`.

---

## How to use this with your designer

- **Read the four people first** (and note who's *real* vs a *guess*), then the "recipe spectrum" idea,
  then the jobs. For each job ask: *what would make this feel effortless?*
- Don't worry about the technology — unfamiliar term ≈ "a kind of log"; the glossary covers the rest.
- The most valuable design work is **the AI-writes-the-recipe idea (jobs 7/9/10)** and **jobs 1, 5, 6** —
  they touch the most people and have the most room to feel better.
- Be honest about certainty: **Priya is real, Marcus is backed up, everyone else is a guess.** The best
  next step is to check the guesses against a couple of real users and adjust.
