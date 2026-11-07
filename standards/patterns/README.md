# Design pattern library

`design-rules.md` says what is **not allowed**. These files say what the team **likes to do**:
the solutions Claude should suggest, when each one fits, and real examples from our CAD.

| File | Covers |
|---|---|
| `lightening.md` | Pocket shapes and when to use each |
| `connections.md` | Joints: tube–plate, gussets, standoffs, shafts, bearings, polycarb |
| `hardware.md` | Fasteners, bearings and spacers we actually stock |
| `lessons.md` | What broke or annoyed us, and what we do instead now |

## How a pattern is written

```markdown
### CN-3 Tube to plate — rivets
- **Use when:** ...
- **Avoid when:** ...
- **Numbers:** spacing, edge distance, count per joint
- **Hardware:** (from hardware.md)
- **Why we like it:** one line
- **Examples:** [Intake side plate 2026](https://orbit1690.onshape.com/documents/...)  ← a real part studio
- **Source:** team decision / learned from <doc> / lesson LL-4
```

- Each pattern has an ID (`LT-`, `CN-`, `LL-`) so suggestions can cite it.
- Anything marked **draft** is a generic FRC default Claude wrote. Students confirm it or edit it,
  then delete the word "draft".
- **Examples** links are what make suggestions look like *our* robot. Add one whenever a design is
  one we'd copy again.
- When students reject a suggestion, write the reason into the pattern (or `lessons.md`)
  so it isn't suggested again.

## Sources Claude can learn from

| Source | Onshape API calls | Good for |
|---|---|---|
| STEP files + `scripts/step_analyze.py` | **none** | Real numbers: pocket shapes/webs/margins, hole sizes, fasteners per joint — ours and other teams' published CAD |
| Screenshots / photos pasted in chat | none | What a mechanism does, broken parts (→ `lessons.md`) |
| Web: FRCDesign.org, team binders, Chief Delphi / Open Alliance | none | Generic patterns and the reasoning behind them (summarised, linked, marked *generic*) |
| Interview: Claude asks, students answer | none | Team habits and the `_?_` blanks |
| Onshape links (`plate_report`, `part_contacts`) | ~3 per part studio | Live work on this season's robot |

## Local STEP analysis

Export in Onshape: right-click a part studio or assembly tab → **Export** → STEP, and save it to
`exports/` (ignored by git). Exporting from the Onshape UI doesn't use API calls.

One-time setup — CadQuery lives in its own venv **outside OneDrive** (it's ~1 GB):

```
python -m venv %LOCALAPPDATA%\frc1690-cad\venv
%LOCALAPPDATA%\frc1690-cad\venv\Scripts\python.exe -m pip install cadquery
```

Run:

```
%LOCALAPPDATA%\frc1690-cad\venv\Scripts\python.exe scripts\step_analyze.py exports\
```

It prints a summary across all files and writes per-file details to `.cache/learn/`.
STEP has geometry and part names only — no feature history and no materials. Pockets are only
seen when they cut through the plate.
