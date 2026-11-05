---
name: lighten
description: Suggest or apply plate lightening (pockets) in an Onshape part studio using the team's Lighten Plate feature. Use when asked to lighten, pocket or reduce weight of plates.
---

# Plate lightening

## Suggest mode (default)

1. `plate_report` on the part studio. Candidates: `isPlate` true, aluminum/polycarbonate,
   face area large enough for at least a few pockets.
2. `part_studio_summary` for current masses (needs materials — if missing, say so and offer
   the `materials` skill first; weight savings are meaningless without density).
3. For each candidate propose parameters within `standards/design-rules.md`:
   - web width ≥ max(3 mm, 1.5 × thickness) (PL-2)
   - edge margin ≥ max(4 mm, 1.5 × thickness) (PL-3)
   - hole margin ≥ largest bolt hole diameter, ≥ 5 mm near bearing bores (PL-4, PL-5)
   - corner radius ≥ 1.5 mm (PL-6); pocket size roughly 3–6 × web width
4. Estimate the saving (rough: removed area fraction ≈ (s / (s + w))² for pocket flat size s and
   web w, times usable area inside margins × thickness × density). Present a table and stop.

## Do mode (only when the user asks to apply)

1. Confirm the URL is a `claude/` workspace (`list_workspaces`). If not, ask the user to branch
   from the latest version, name it `claude/lighten-<part>`, and paste the URL.
2. Check `featureLibrary.url` in `onshape.config.json` is set (Feature Studio with
   `featurescript/lighten_plate.fs`). If not, use the `new-feature` skill to publish it first.
3. Note the plate's mass, then call `add_lighten_feature` with its `faceId` and the agreed
   parameters — one plate per call, each approved by the user.
4. Check the result: feature status OK, `part_studio_summary` for new mass, one `render`.
   If the feature errors ("No pockets fit"), reduce pocket size or margins once, then ask.
5. Report per plate: before/after mass, pockets cut, parameters. Remind the user to review the
   branch in Onshape and merge it themselves.
