---
name: design-suggest
description: Suggest design improvements for an Onshape part studio — lightening shapes, connection/joint solutions, hardware — using the team's pattern library in standards/patterns/. Use when asked for design ideas, suggestions, how to connect or attach parts, or how to make something lighter or stronger.
---

# Design suggestions

Suggestions come from **the team's patterns first**, generic FRC knowledge second. Always say
which one a suggestion comes from.

## Suggest mode (default, read-only)

1. **Read the library:** `standards/patterns/*.md` (especially `lessons.md`, including the rejected
   suggestions table) and `standards/design-rules.md`. Read `hardware.md` before naming any part.
2. **Gather geometry — about 4 calls per part studio:**
   - `plate_report` → plates, thickness, holes, faceIds (1 call)
   - `run_fs_query` with `part_contacts` → which parts touch, and the holes at each joint (1 call).
     Holes listed on both sides of a contact are usually the fasteners for that joint.
   - `part_studio_summary` only if mass or weight savings matter (2 calls, cached)
   - `render` once (isometric) and open the PNG — needed to understand what the mechanism *does*.
3. **Open pattern examples only when it helps.** If a pattern's **Examples** link is close to this
   design, `plate_report` it to compare. At most 2 example studios per session (API budget).
4. **Work out the job of each part:** what it holds, where loads go (pivots, bearings, motor mounts,
   frame joints). Ask the user when it isn't clear from the render — don't guess loads.
5. **Match against the patterns:**
   - Plates → lightening shape (`lightening.md`), within PL rules.
   - Each contact → is it a known joint pattern (`connections.md`)? Too few fasteners, missing
     gusset, a pocket near the joint, hardware not in `hardware.md`?
   - Anything that repeats a `lessons.md` entry is the top priority.
6. **Report** — ranked, most important first. For each suggestion:
   part(s) · what to change · pattern / rule / lesson ID · why · rough effect (mass in kg, fastener
   count) · source: *team pattern* or *generic — not yet in our patterns*.
   Write it to `reviews/<system>-suggestions-<YYYY-MM-DD>.md` when there are more than a few.
7. **Ask what to keep.** Offer to record accepted generic ideas as new **draft** patterns and
   rejected ones in `lessons.md` → "Rejected suggestions" (with the student's reason).

## Do mode (only when the user asks to apply a suggestion)

- Lightening → follow the `lighten` skill's do mode.
- Other changes → add new features with `add_feature` in a `claude/` workspace, one at a time,
  each approved (CLAUDE.md hard rules 1–3). Never edit the student's existing sketches/features.
- Many joint changes are better done by a student by hand — say so instead of forcing them
  into API features.

## Limits to tell the user

No FEA: load reasoning is rules of thumb plus geometry. Treat suggestions as a design-review
partner, not a stress check.
