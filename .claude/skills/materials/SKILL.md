---
name: materials
description: Find parts with no material in Onshape design documents and assign team materials from standards/materials.json so weight checks are accurate. Use when asked about materials, missing mass, or density.
---

# Materials

1. `part_studio_summary` for each part studio in scope. Collect parts where `material` is null
   or `hasMass` is false.
2. `plate_report` (1 call) for thickness/shape to help decide.
3. Propose a material key for each part from `standards/materials.json`:
   - `assignmentHints` name patterns first
   - otherwise: a flat plate whose thickness is in a material's `plateThicknessMm` → that material
     (aluminum when several match, unless the name or colour says otherwise)
   - COTS parts (motors, bearings, gears, electronics) → **don't assign a material**; flag them
     for a mass override instead (rule DA-2), because a density gives the wrong mass
   - unsure → ask, don't guess
   - materials marked `"verify": true` → mention that the density is an assumption
4. Show the proposal as a table (part, current, proposed, reason) and wait for approval.
5. Apply in a `claude/` workspace only, with `set_material` one part per call.
6. Re-run `part_studio_summary` and report the new total mass and anything still missing.
