---
name: gameface-update
description: 'Sweeps the coherent-gameface plugin after the reference target''s Cohtml moved. Use when the target''s embedded engine updated, when the decompile''s `cohtml.Net` assembly version is newer than the gameface skills'' baseline lines, or when `mise skills:check-changelog` reports a stale ceiling.'
---

# Sweeping the gameface plugin for an engine update

This skill re-probes every version-dependent claim against the running game, reads the rest off Coherent's feature changelog, and ends with every `Verified against Cohtml` line on the new version.

It edits `plugins/coherent-gameface/`, `docs/ROADMAP.md` and the [probe catalogue](probes.md). The research files' observed-Cohtml lines, `docs/SOURCES.md` and all `cs2-modding` prose are `cs2-game-update`'s.

**A stale ceiling alone is a shorter run.** It applies where `mise skills:check-changelog` is red and the two versions below are equal, and comparing them needs no game. Fetch the changelog, work the timeline-and-ceiling step, run the checks, then take the close's review pause before committing it as `fix(coherent-gameface):`. The refusal, the plan and the rest of the close belong to a moved engine.

## 1. Reach the game, or refuse

Outside the ceiling-only run above, `game_status` is the first act. Unreachable ends the run: say the sweep needs the reference target running with its CDP endpoint answering, and stop there. Every claim this sweep writes is a probed one, so the run ends here rather than requesting a launch or continuing from the docs.

Anything a later step needs done in the game — a save loaded, a panel opened, a setting changed — takes the root `AGENTS.md` ask-before-acting rule: say what and why, last in the message, and end the turn.

## 2. Fix the two versions

- **New**: `AssemblyVersion` in `src/cohtml.Net/Properties/AssemblyInfo.cs` at HEAD of the decompile root `~/.cs2-modding/setup.md` records.
- **Old**: the version every baseline line states — `grep -rho 'Verified against Cohtml [0-9.]*[0-9]' plugins/coherent-gameface/skills | sort -u` returns one line, and a second one means a previous sweep never closed: resume it before starting this one.

Equal versions mean nothing to sweep: work the ceiling-only run above where the ceiling is stale, and otherwise say so and stop.

The `Browser` string `game_status` reported must carry the new version. Where it carries another, the decompile and the running game disagree: report both and stop.

## 3. Fetch the changelog range

`curl -s https://docs.coherent-labs.com/cpp-gameface/changelog/feature/` into `.scratch/gameface-update-<new>/`, and parse the saved page there: a summarising fetch tool returns the range abridged. Inside `<main>`, a release opens at each `Version X.Y.Z` heading, the pattern `scripts/check-skill-changelog.ts` matches.

Write to `changelog.md` in that folder every release above old up to new, and every release above the `<!-- timeline-ceiling: … -->` marker in `plugins/coherent-gameface/skills/gameface/references/version-gating.md`. Nothing fetched is tracked ([ADR-11](../../../docs/adr/0011-coherent-labs-documentation-corpus-stays-out-of-the-gameface-skill.md)).

## 4. Build or resume the plan

The plan lives at `.scratch/gameface-update-<new>/plan.md`, one checkbox per step, so a compaction loses nothing: invoked over an existing plan, resume at the first unchecked box.

1. **Marker clusters.** `grep -rn 'VOLATILE:' plugins/coherent-gameface/skills`, grouped by subject — the flex gates, the whitespace-node regime, the missing globals — one step per cluster, each carrying every marker on its subject across files plus the changelog entries that name it. A marker on the selector whitelist belongs to the whitelist-triple step, never to a cluster. A changelog entry in the range whose subject no marker covers joins the cluster whose subject it touches, as an inline probe, and one touching none is a step of its own.
2. **The whitelist triple.**
3. **The catalogue.**
4. **The timeline and ceiling.**
5. **The user-facing version claims.**
6. **The close.**

A marker whose subject neither a probe nor the changelog can settle stays as written.

## 5. Work a step

For one marker cluster:

1. Open the changelog entries on the cluster's subject.
2. Run the [probe catalogue](probes.md) entries settling its claims, one at a time through the tool each names, plus an inline probe for whatever those changelog entries call for and the catalogue does not cover. Record every answer under the step in the plan.
3. Re-derive each marked claim from the probe answer and the changelog. A gate at or below the new version reads as satisfied or goes; a workaround the new version retires is rewritten to the supported feature, the workaround gone.
4. Edit in place. Every agent-facing line the step touches names "the reference target" where it named the game, except the one line in each skill's opening that says which game the reference target is.
5. Check the edits against the range and the recorded answers, then check the box.

The other steps, whose `mcp/` and `WHY.md` paths are relative to `plugins/coherent-gameface/`:

- **The whitelist triple.** Re-probe every selector entry of the catalogue, the wrongly-answered ones by the answer each gives rather than by acceptance ([why acceptance is not membership](../../../docs/solutions/a-support-set-built-from-what-did-not-throw.md)). Then edit together `mcp/src/selectors.ts` (`SUPPORTED_PSEUDOS`, `ANSWERED_NOT_REJECTED` and `ARGUMENTLESS_ANSWERED`, the summary, the `:nth-child()` argument pattern, every `Verified live against` line), the `verbatim from Cohtml` version in `mcp/tests/selectors.test.ts`, and in both skills the whitelist sentence, the `:nth-child()` argument sentence, the wrongly-answered sentence and the `:host`-scope sentence. Then the one carrier outside the triple, which no test reaches: the Shadow DOM entry of `skills/gameface/references/tooling-workflow.md`. The `version-gating.md` timeline answers when a feature arrived, never how well the JS APIs answer it, so that fact stays off its rows. `mise test:gameface` binds the whitelist and `:nth-child()` copies and is green before the box is checked; every other sentence is re-read against the probe answers by hand. `mise build:gameface` follows any `mcp/src/` edit.
- **The catalogue.** Run every entry no earlier step reached. Rewrite the expected answer of every entry whose recorded answer changed, whichever step ran it. Rewrite the snippet of every entry that would have answered the same on an engine with the feature and on one without. Once every entry has run, the file's as-of line moves to the new version and states that the answers come off that run. An inline probe joins the catalogue only when it settles a claim the plugin states.
- **The timeline and ceiling.** Append to the version-gating timeline the notable web-platform entries of every release above the ceiling marker, add their breaking changes to "Breaking changes worth knowing", strike from "Never existed" whatever one of them introduced, and move the ceiling to the newest: the marker, the "current through" sentence under it, and the V8 section's "no later bump through", re-read for a bump.
- **The user-facing version claims.** In `mcp/README.md` and `WHY.md`, a statement of the version the reference target runs moves to the new version, while a floor claim ("field-verified down to") keeps the lowest version ever verified. A statement of the version a finding was established on — in `WHY.md`, the plugin's `AGENTS.md` verification-context line and `docs/ROADMAP.md` — is re-read against the new version. `mcp/README.md` and `WHY.md` keep the game's name where it states what the plugin was developed against.

## 6. Close

In this order, once every other box is checked:

1. Dispatch one verifier on opus with the whole working diff, `changelog.md` and the plan's recorded answers. The close continues when it finds no claim that neither the range nor an answer carries.
2. `mise check:agents` and `mise test:gameface` are green.
3. Every line `grep -rn 'Verified against Cohtml' plugins/coherent-gameface/skills` lists moves to the new version, last, so the line promises the file above it was swept.
4. Hand the working diff to the user for review, uncommitted. The message states what the version moved, every marker the sweep could not match, and every catalogue entry whose expected answer changed. **The ask ends the turn**: put it last, in bold at the top level, and stop there — the sweep rewrites shipped claims across a whole plugin, and the only reader who can weigh that against the game in front of them is the user.
5. Commit what the review leaves standing, in two: `fix(coherent-gameface): <what the version moved>` carrying every edit under the plugin, the `mcp/` unit and the baseline bump included — `feat` where the whitelist set grew — and an unscoped `docs:` commit for `docs/ROADMAP.md` and the catalogue, which a commit under the plugin would release. A plan step is a line of the first commit's body, never a commit of its own: the release notes read as one engine move, and a reader after a single claim reaches the plan folder rather than a commit. The sweep is the commit instruction.

Leave the plan folder for the user to delete.
