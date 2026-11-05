---
name: weight-check
description: Total the mass of robot systems from their Onshape design assemblies and compare against weight budgets in onshape.config.json. Use when asked about robot or system weight, budgets, or what is heavy.
---

# Weight check

1. Make sure `onshape.config.json` has `kind: "assembly"` entries for the systems in scope and
   budgets set. If budgets are null, report masses without pass/fail and suggest setting them.
2. One system: `weight_check` with `system`. Whole robot: run
   `python scripts/onshape_cli.py weight` (throttled; may take a few minutes for many systems).
3. Any system with `problems` (no mass) or suspiciously low mass: run `part_studio_summary` on
   its part studios to find parts without material — those parts count as 0 kg.
4. Report a table: system, mass, budget, over/under, and data gaps. Then list the 5 heaviest
   parts in over-budget systems (from `part_studio_summary`) and which are lightening candidates.
5. Totals are only as good as materials and COTS mass overrides — say how many parts lacked mass.
