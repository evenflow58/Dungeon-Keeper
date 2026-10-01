# Contributing

Solo project — these rules exist so future-me doesn’t have to guess.

## Every PR

Checklist (mirrored in the PR template — check the boxes for real):

1. **Tests for pure logic.** New board state, math, pathfinding, economy, or AI
   decision code gets EditMode tests in `Assets/Tests/EditMode`. Feel and juice
   (damping values, particles, screen shake) don’t need tests.
2. **Null idioms.** Use `?.`, `??`, `??=` instead of null-check blocks.
   `if (x == null) return;` guard clauses stay. If code intentionally falls back
   on a *default value* (not just null) — e.g. “use the device reading when the
   action reads zero” — keep the explicit check and say why in the PR.
3. **Play-mode verified.** Exercise the story’s acceptance criteria in Play
   mode before merge; say what you tested in the PR.
4. **Commit before large AI-generated changes**, so they can be reverted.

## Workflow

- One story = one branch = one PR. Branch: `<issue-number>-<short-name>`.
- Label the issue `in-progress` before starting.
- Every PR gets a review pass before merge. (GitHub won’t let authors approve
  their own PRs — a review comment is sufficient.)
- Keep PRs focused; unrelated cleanup goes in its own PR.

## Tests

- Unity Test Framework. Game code: `DungeonKeeper.asmdef`. Tests:
  `Assets/Tests/EditMode/EditModeTests.asmdef` (Editor-only).
- Name tests `Method_Scenario_ExpectedResult`.
- Keep pure logic callable without Play mode (a test `GameObject` +
  `AddComponent` is fine) so EditMode tests can reach it.
- Run the EditMode suite before pushing.

## C# notes

- New Input System only — never the legacy `Input` class.
- `cam ??= GetComponent<Camera>();` over `if (cam == null) ...`.
- `[SerializeField] private` tunables, not magic numbers.
