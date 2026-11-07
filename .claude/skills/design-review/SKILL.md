---
name: design-review
description: Review an Onshape part studio or assembly against the team's design rules (standards/design-rules.md) and write a report. Use when asked to review, check or critique a design, part or system.
---

# Design review (read-only)

1. **Resolve the target.** Get a URL from the user or `onshape.config.json` (`systems.<name>.design`).
   If it's a document URL without `/e/`, call `list_elements` and pick the part studios/assemblies.
2. **Gather data — keep it to ~3 calls per part studio:**
   - `plate_report` → thickness, size, holes, stock match, faceId per body (1 call)
   - `part_studio_summary` → materials and masses (2 calls, cached afterwards)
   - `list_features` only if feature errors or naming matter for this review
3. **Look at it.** `render` once (isometric) and open the PNG with Read. Render other views only
   when a specific question needs them.
4. **Check every rule** in `standards/design-rules.md`. For each finding record: rule ID,
   part name, what's wrong with numbers ("3.2 mm plate — not a stocked thickness; nearest 3 mm"),
   and a concrete fix. Don't report rules that pass, except a one-line summary.
   Base fixes on `standards/patterns/` where one fits (cite its ID) and check `lessons.md`.
5. **Spot lightening candidates** (PL-7): plates with `isPlate`, large face area and few pockets.
   List them with an estimated saving; hand off to the `lighten` skill only if the user asks.
6. **Write the report** to `reviews/<system>-<YYYY-MM-DD>.md`:
   - Summary (3–5 lines: biggest risks first)
   - Must-fix table · Suggestions table · Lightening candidates · Data issues (missing materials)
7. Tell the user the top findings in chat and link the report. Don't change anything in Onshape.
