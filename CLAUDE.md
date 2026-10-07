# CLAUDE.md — Dungeon-Keeper

## What this project is

Dungeon-Keeper is a single-player dungeon-management game (Unity 6000.6.4f1,
Universal 2D / URP, New Input System). The current milestone is the "First Dig"
vertical slice, spec'd in `DESIGN.md` Appendix A. `DESIGN.md` is the design
source of truth — read the relevant section before implementing a story.

## How work is organized

- All work is tracked as GitHub issues in `evenflow58/Dungeon-Keeper`. Epics
  contain stories; implement one story at a time.
- One story = one branch = one PR. Branch off `main`, named
  `<issue-number>-<short-slug>` (e.g. `17-board-dimensions`).
- A story is only ready to start when its issue carries the `in-progress`
  label. If it doesn't, stop and say so.
- NEVER commit or push directly to `main`. NEVER merge a PR. Evan reviews and
  merges every PR.
- Commit to the story branch early (a clean baseline before large changes),
  push, and open the PR against `main`. Fill in the PR template, including
  `Closes #<issue-number>` on the Closes line so the story auto-links and
  auto-closes on merge.
- After pushing, report the commit hash and confirm the commit is visible on
  GitHub.

## Code standards (see CONTRIBUTING.md for the full version)

- New Input System only (`UnityEngine.InputSystem`). Never use the legacy
  `Input` class.
- Null handling: use `?.`, `??`, and `??=` instead of verbose null-check
  blocks. Guard clauses (`if (x == null) return;`) are fine and stay. Some
  code deliberately falls back on a *default value* (e.g. zero input) rather
  than on null — preserve that semantics exactly; don't "simplify" it into a
  null check.
- Tunable values (sizes, speeds, counts) are `[SerializeField] private`
  fields, never buried `const`s or magic numbers. Player-facing bindings must
  stay rebindable.
- Pure logic (board state, math, pathfinding, economy, AI decisions) ships
  with EditMode tests in `Assets/Tests/EditMode`. Feel/juice values don't
  need tests.
- Keep simulation/data separate from rendering, following the existing
  DungeonBoard / BoardRenderer split.

## Verification — read this twice

- After editing a file, re-read it from disk and confirm the change is
  actually present before reporting progress. (A previous assistant reported
  a fix that was never written to disk.)
- Run the EditMode test suite after code changes and report the actual result
  counts. If you can't run tests (editor/MCP unavailable), say so explicitly —
  never imply they passed.
- Smoke-test changed behavior in Play mode when possible, and say exactly
  what you tested.
- Never hand-edit `.unity` scene files, `.meta` files, or anything under
  `Library/` or `Temp/`. If a serialized reference needs wiring in the
  Inspector, either do it through MCP for Unity (if connected) or tell Evan
  precisely which object and field to set.
- When you create new C# files outside the editor, Unity generates matching
  `.meta` files on import — make sure they are committed with the scripts.
