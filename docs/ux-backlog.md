# FindNeedle — Prioritized UX Backlog

> **v2 — re-ranked after the persona work (`personas-and-use-cases.md` v1.4) and reconciled with what
> actually shipped.** The ranking rule changed. It used to be *impact × frequency, then effort*. It now
> leads with **who we've confirmed is real and what serves them**:
> 1. **Does it advance the linchpin** — the *author-a-rule → auto-draw-the-sequence-diagram* loop
>    (**UC7 → UC9**), ideally AI-assisted? That single capability fixes the **one confirmed user's** (P1)
>    stated gap *"it doesn't make my job easier yet"* and serves three personas (Beginner P5, P1, AI-Driven
>    P6). It is the **highest-leverage bet in the product.**
> 2. Then **impact × frequency** across the personas (UC6 filter, UC1 open, UC12 reuse, UC5 correlate are
>    widest — see the weighted matrix).
> 3. Then **effort**: S (hours), M (a day-ish), L (multi-day / structural).
>
> **Persona legend (with confidence):** P1 Trace Expert ✅*confirmed* · P2 Support Triager ◐*corroborated*
> · P3 Automator ○ · P4 Casual Dev ○ · P5 Beginner / P6 AI-Driven ☐*bets*. UC1–UC13 per the personas doc.

---

## 0 · Already shipped

State so the backlog reflects reality. All on `master`.

- **Correctness (P0):** unclickable Cancel fixed; zero-source guard; triage empty-selection guard
  (+ check/uncheck-all, kernel-unchecked note, "scan whole file" escape). — *UC1, UC2*
- **First-run calm:** "No log loaded yet" empty state; calmer search placeholder; suppressed empty
  "Last run"; trimmed Welcome; auto-hide empty ETW columns; filters default to a left rail;
  **new-user preview mode** (throwaway profile). — *UC1, UC6*
- **Terminology & IA:** one noun "Source"; Diagnostics/Tools/Help menus; Settings → labeled gear; Configure
  holds Sources/Rules/Connections + workspace ops; dev-only affordances gated; **Ctrl+K command palette**
  (was a "LATER" item, pulled forward). — *all*
- **Filtering (UC6):** level chips *are* the filter, now **multi-select**; active-filter badge; pick-from-
  values (multi-select, cross-filtered); source-aware column defaults.
- **The core loop, partial (UC7/UC9):** Rules hub opens config-first + plainer framing; **"Add a starter
  rule"**; **live "would this match?" preview** in the reformat editor; **per-rule match counts + timing**
  on the Active-rules page; Processor-Output previews deferred diagrams before generating.
- **Time (UC5):** **time-density strip** in the viewer — level-colored, click-to-zoom, hide/restore
  (closed the "no visual timeline" gap from v1.1).
- **Viewer polish:** full-text hover tooltip on Message; **rows top-aligned + single-line** (first line
  shows, not a middle line); **level row tint readable** (subtle, not full-fill) + live theme recolor;
  Settings opens on "All settings".
- **Reuse (UC12):** window title shows the active **workspace (N sources, M rules)**; cache-reuse prompt
  reworded to outcomes; Recent items are checkboxes with added-state.
- **Reliability / diagnostics:** System-check remediation links; symbol-resolution log reachable on
  *successful* WPP decode; online-source "Set up X…" (no dead-end); empty states for Logs / Sources / Rules.

---

## 1 · NOW — the linchpin, then the two safe wins

### 1.1 The authoring → diagram loop (UC7 → UC9) — **the #1 bet**  · **L (laddered)**
- **What.** Close the gap between "I can see the problem" and "the tool reconstructs it for me." Today the
  RuleDSL → sequence-diagram value exists but requires hand-authoring a rules file + schema knowledge — so
  it *doesn't remove the tedium* (P1's exact complaint). Deliver it as a ladder, cheapest-first:
  - **(a) Diagram-from-selection / "diagram this activity"** — from the viewer, select rows (or an
    ActivityId) → generate a sequence/causality diagram directly, no hand-written rule. *Manual, no AI —
    removes most of the tedium immediately.* · **M**
  - **(b) Sequence-rule templates** — one-click starter UML rules (participants + common match patterns)
    the user tweaks, instead of authoring from a blank file. · **S–M**
  - **(c) AI-assisted authoring (the linchpin proper)** — an agent reads the log/selection, **proposes a
    RuleDSL rule + a sequence UML**, and the human **reviews/edits/accepts**. Uses the MCP surface (UC10).
    · **L**
- **Serves.** **P1 ✅ (removes the tedium), P5 ☐ (value with zero DSL), P6 ☐ (this *is* their workflow);**
  P3 secondary. — the 3-persona unlock.
- **Hard requirement (the killing signal).** AI-authored rules must be **trustworthy and reviewable** —
  show the matched rows behind every diagram element, make edits easy, never a black box. If users can't
  trust it, they revert to hand-work and the whole bet fails.
- **Why now.** It's the only item that fixes the confirmed user's #1 gap; (a) alone is high-value and
  AI-free, so we get most of the win before taking on the trust problem in (c).

### 1.2 During-run progress (the open half of the old "run feedback")  · **M**
- **What.** Rows-scanned / current-file / elapsed during a run, not a bare spinner. (Pre-run summary
  already shipped.) One shared component across Run Search + QuickLogWithRules.
- **Serves.** UC1, UC2 · P1 (big-file loads), P2. — "feels hung" is the top mid-flow anxiety on the large
  files these personas open.

### 1.3 Accessibility sweep  · **M**
- **What.** `AutomationProperties.Name` on every filter input + icon-only chrome button (a shared
  IconButton style that *requires* a name); `AccessKey` mnemonics on the top menus.
- **Serves.** all personas; the one finding category still at **zero coverage** — and it stabilizes our own
  UIA-based UI tests (the "saw -1" class of flakiness).

---

## 2 · NEXT

### 2.1 MCP: "what can an agent do?" + it's the linchpin's surface  · **M**
- Add a tool-catalog / copyable connect-snippet surface; relabel finished. **Doubles as groundwork for
  1.1(c)** — the AI-authoring loop runs over MCP, so making the agent surface legible pays off twice. —
  *UC10 · P3, P6 ☐.*

### 2.2 Auto-add condition validation + pickers  · **M**
- Replace magic-string source-kind boxes with a multi-select of known kinds; validate/normalize on save
  (warn on typos like "Eventlog"); click-to-insert examples. — *UC7 · P3, P2.*

### 2.3 Graduate Inspect Binary from "experimental"  · **S (validation)**
- Validate WEVT_TEMPLATE + TraceLogging extraction against a corpus of real binaries, then drop the label.
  — *UC11 · P1.*

### 2.4 Polish sweep  · **S each**
- Hidden status-bar restore handle; one shared page-title style + sentence case; emoji → Fluent icon
  vocabulary. — *all.*

### 2.5 Clarify / unify Sources vs Log Finder (beyond subtitles)  · **M**
- Fold the two "add data" surfaces toward one consistent action; share one provider component. — *UC4 · P2.*

---

## 3 · LATER / bigger bets

### 3.1 Cross-source time normalization  · **L**
- Audit + fix time-zone/format alignment so events from different source kinds truly interleave on one
  timeline. **Note:** matters for P2's multi-source bundles and for *cross-source* sequence reconstruction;
  within a single trace (P1's common case) it's less pressing. Correctness, not chrome. — *UC5 · P2.*

### 3.2 Left-rail activity navigation (toolbar D4)  · **L**
- The command palette covered the keyboard-power case; a left activity rail with Settings pinned at the
  foot remains the long-term structure if first-run findability needs more. — *all.*

---

## Summary table

| # | Item | Serves | Effort | Tier |
|---|------|--------|:------:|:----:|
| 1.1 | **Authoring → diagram loop (UC7→UC9), AI-assisted** | **P1✅ / P5☐ / P6☐** | L (ladder) | **NOW #1** |
| 1.2 | During-run progress | UC1/UC2 · P1/P2 | M | NOW |
| 1.3 | Accessibility sweep | all | M | NOW |
| 2.1 | MCP catalog (+ linchpin surface) | UC10 · P3/P6 | M | NEXT |
| 2.2 | Auto-add validation/pickers | UC7 · P3/P2 | M | NEXT |
| 2.3 | Graduate Inspect Binary | UC11 · P1 | S | NEXT |
| 2.4 | Polish sweep | all | S | NEXT |
| 2.5 | Unify Sources + Log Finder | UC4 · P2 | M | NEXT |
| 3.1 | Cross-source time normalization | UC5 · P2 | L | LATER |
| 3.2 | Left activity rail (D4) | all | L | LATER |

**Recommended next build:** **1.1(a) diagram-from-selection** — the biggest chunk of the #1 bet, AI-free,
and it directly attacks P1's "doesn't make my job easier yet." Then 1.1(b) templates, then 1.2/1.3 as the
safe wins, and 2.1 to tee up the AI-assisted 1.1(c).
