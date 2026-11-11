---
name: cs2-game-update
description: 'Re-verifies this repo''s Cities: Skylines II traps after a game update. Use when the game has updated, or when the decompile holds a game version newer than the trap-check baseline.'
---

# Re-verifying the traps after a game update

The `cs2-modding` skills hold traps, each marked with the game type whose code carries it, and `scripts/trap-check/baseline` holds the decompile commit they were last verified at.
An update costs one trap-check run and one read per trap it names.

## 1. Refresh the decompile

Read the installed version off the install, since `~/.cs2-modding/setup.md` holds only what was last seen: `strings -n6 "<Game install>/Cities2_Data/Managed/Game.dll" | grep -E '\) \[[0-9]+\.[0-9]+\]'` prints its `VersionInternal` string with no launch.
Where it is newer than the decompile's newest game-version commit, run `cs2-modding-setup`'s refresh branch, which commits the new version.
Where the baseline already is the newest game-version commit, say there is nothing to re-verify and stop.

Where the diff touches `src/cohtml.Net/Properties/AssemblyInfo.cs`, the embedded engine moved: name `gameface-update` as the sweep for the coherent-gameface plugin.

## 2. Step over re-decompiles

A game-version commit carries the version as its message; any other commit is a re-decompile that rewrites thousands of files and changes no game fact, so no diff spans one.
For each re-decompile between the baseline and HEAD, oldest first:

1. Where game-version commits sit between the baseline and it, run the check with `--target <the last of them>`, settle what it names as "Settle each named claim" says, and write that commit's hash into `scripts/trap-check/baseline`.
2. Run `mise skills:trap-check <decompile root> --base <it> --target <it>`, which diffs nothing, so only its `missing` claims matter; once it reports none, write its hash into the baseline.

A `missing` claim at a re-decompile is a type the new decompiler names differently: repair its marker as "Settle each named claim" says for a `missing` claim before moving on.

## 3. Run the check

`mise skills:trap-check <decompile root>` diffs the baseline against HEAD and lists every marked claim, in the skills and in the `cs2-dev` server's log-line comments, as `changed`, `missing`, `unchecked` or `clean`.
A `clean` claim's type file did not change, so it needs no read.

## 4. Settle each named claim

- `changed`: read the type's diff (`git -C <root> diff <baseline> <target> -- <file>`, the target being HEAD unless a step-over named a commit) against the claim, then keep it, reword it, or retire it where the game no longer misleads. A changed server marker is a parser to check against the line the game now prints.
- `missing`: the type was renamed, moved or removed. Find where the behaviour went, then point the marker there and re-verify the claim, or retire the trap.
- `unchecked`: rests on no type, so no diff covers it; list these in the closing message for the maintainer to read.

Done when every `changed` and `missing` claim is kept, repaired or retired, and a rerun names none of them `missing`.

## 5. Close

1. Write the decompile HEAD hash into `scripts/trap-check/baseline`.
2. `mise check:agents` and `mise test:cs2-dev` are green.
3. Commit the skill and server edits as `fix(cs2-modding): <what the version changed>`, and the baseline move on its own as `chore: move the trap-check baseline to <version>`, since a commit under the plugin releases it.

Running this skill is the commit instruction.

The closing message lists each changed or missing claim with what became of it, then the unchecked ones.
