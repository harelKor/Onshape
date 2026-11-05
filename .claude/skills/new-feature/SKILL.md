---
name: new-feature
description: Write or change a custom FeatureScript feature in featurescript/ and push it to an Onshape Feature Studio, then fix compile errors. Use when asked to create, edit or publish a FeatureScript feature.
---

# New / changed FeatureScript feature

1. Start from `featurescript/template_feature.fs` (new) or the existing file (change).
   Follow the FeatureScript conventions in `CLAUDE.md`.
2. Ask where it lives: a Feature Studio URL in a `claude/` workspace, or the team's feature
   library document (that document must be listed in `guard.unguardedDocuments`).
   If the user has no Feature Studio yet, ask them to create an empty one and paste its URL.
3. `push_feature_studio` with the URL and file path (user approves). It matches the
   `FeatureScript NNNN;` header and std import versions to the target automatically.
4. Read the returned notices. Fix errors locally and push again — at most 3 rounds, then show
   the user the remaining errors.
5. Test it: in a part studio in a `claude/` workspace, add it with `add_feature` (or
   `add_lighten_feature` for Lighten Plate) and check the feature status. `render` once if
   the geometry needs a visual check.
6. For cross-document use, the user creates a version of the library document; update
   `featureLibrary.url` in `onshape.config.json` to that version URL (`/v/...`).
7. Commit the `.fs` file with a message describing the feature change.
