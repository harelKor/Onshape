---
name: learn-patterns
description: Study the team's existing Onshape designs (past robots, good mechanisms, things that failed) and turn what they do into entries in standards/patterns/ so future suggestions match the team's style. Use when the user shares design documents to learn from, or asks Claude to learn the team's design habits.
---

# Learn patterns from team designs

Claude does not remember documents between sessions. "Learning" means writing what the
designs show into `standards/patterns/`, with links back to the examples. Read-only in Onshape.

## 1. Pick sources — cheapest first

See the sources table in `standards/patterns/README.md`. Prefer, in order:
1. **STEP files** (no API calls) — ask the user to export the designs to `exports/` and run
   `scripts/step_analyze.py` on them (setup and command in that README). Works for other teams'
   published CAD too. Whole robots are fine here.
2. **Screenshots / photos** pasted in chat, and the team's own docs in the repo.
3. **Web** — FRCDesign.org, team technical binders, Chief Delphi / Open Alliance. Summarise in
   your own words, link the source, mark the pattern *generic, from <source>*.
4. **Interview** — ask senior students about the `_?_` blanks and last season's failures.
5. **Onshape links** — only when the above can't answer it.

For every design, ask what it is and why it's worth learning from:
- "we'd build this again" → patterns to copy
- "this broke / was a pain" → `lessons.md`
- the system it belongs to, the season, and whose team it is

## 2a. STEP files

Run the analyzer and read its summary; open `.cache/learn/<file>.json` for per-plate and
per-joint detail. A pattern's **Examples** link can point to the Onshape document the STEP came
from (ask for the URL) or name the published release.

## 2b. Onshape links — only if needed

Estimate API calls before starting (≈ 3–4 per part studio) and check usage
(`api_usage` / `scripts/onshape_cli.py usage`). Stop and ask if the total would pass the budget.
Gather each studio in about 3 calls:

- `plate_report` (thickness, size, holes, pockets) · `run_fs_query part_contacts` (joints and
  fastener holes) · one `render` (isometric).
- Skip `part_studio_summary` unless the user asks about weight.
- Save a short note per studio in `.cache/learn/<name>.md` so a second pass needs no new calls.

## 3. Extract what repeats

Look across the studios, not at one alone:
- **Lightening:** which pocket shapes, sizes, webs and margins they actually use, per material and
  thickness (`plate_report` gives these numbers).
- **Joints:** fastener sizes and counts per joint, spacing, gussets, standoff spacing, bearing
  margins (from `part_contacts` hole diameters plus the render).
- **Hardware:** every hole size that keeps showing up → likely stocked size.
- Something seen once is an example; something seen in 3+ designs is a team habit.

## 4. Write it down — show the diff first

- Update the matching pattern: real numbers, plus an **Examples** link (the part studio URL).
  Keep "draft" on anything a student hasn't confirmed.
- New habit with no pattern yet → a new draft entry with the next ID.
- Failures → `lessons.md`, worded the way the user described them.
- Where our designs break `design-rules.md` again and again, **don't** edit the rule — list it
  for the user: the rule may be wrong, or the habit may be bad.
- Show the user a summary of what changed and ask before committing.
