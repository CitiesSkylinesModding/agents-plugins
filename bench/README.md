# The skill-efficacy benchmark

Measures whether an agent holding the `cs2-modding` skill answers a hard CS2 modding question better, faster or cheaper than the same model holding only the game's own sources.

Two arms answer the same question in isolated headless workspaces, several times each, and a blind judge scores every answer against a maintainer-verified reference answer:

- **control** — an empty workspace. The prompt pastes the question's source roots and forbids skills and any reading beyond them.
- **treatment** — the same, plus the shipped skill folder copied to `.claude/skills/cs2-modding/` as plain files. The prompt names `SKILL.md` as the entry point. Nothing loads it as a harness skill; the arm reads it with `Read`.

Both arms get the identical root set and the identical flag set, so the delta is attributable to the skill alone.

The layout is two files: `core.ts` holds every decision (question parsing, prompt assembly, result parsing, aggregation, rendering) and is what the tests drive; `run.ts` is a deliberately untested shell around it (workspaces, spawning, retries, file writes).

## Running it

```bash
mise bench:test                     # the core's unit tests
mise bench:validate                 # the question files parse and their roots resolve; spends nothing
mise bench:run                      # the real thing: Opus, real spend
mise bench:run --smoke --runs 1 --model haiku --judge-model haiku --timeout-minutes 5
```

Flags, all optional:

| Flag                    | Default           | What it does                                                                       |
| ----------------------- | ----------------- | ---------------------------------------------------------------------------------- |
| `--smoke`               | off               | Runs the smoke question in `tests/fixtures/` instead of iterating `questions/`.    |
| `--runs <n>`            | `4`               | Runs per arm per question.                                                         |
| `--concurrency <n>`     | `2`               | Runs in flight at once. Also the throttle on spend rate.                           |
| `--model <id>`          | `opus`            | The model both arms run.                                                           |
| `--judge-model <id>`    | `opus`            | The model that grades.                                                             |
| `--effort <level>`      | `medium`          | Effort level for every invocation, arms and judge.                                 |
| `--timeout-minutes <n>` | `20`              | Per invocation, after which the run counts as an infrastructure failure.           |
| `--validate-only`       | off               | Loads and validates the questions, resolves the roots, then stops before spending. |
| `--questions <dir>`     | `bench/questions` | Where the question files live.                                                     |
| `--results <dir>`       | `bench/results`   | Where output lands.                                                                |

At four questions and four runs an invocation is 32 Opus runs plus 32 judge calls. That is real money; `--concurrency` is the throttle.

**Source roots are resolved before the first invocation.** The decompile and the reformatted UI bundle copy come from the setup record at `~/.cs2-modding/setup.md`, the install from `CSII_INSTALLATIONPATH` with the record's `Game install` line as the fallback. A root that resolves to nothing, or to `(none)`, aborts the whole invocation rather than running an uneven comparison.

Results land in `bench/results/<timestamp>/`, which is gitignored: `summary.json`, `summary.md`, and a `raw/` directory holding each run's assembled prompt, its CLI JSON, and its judge prompt and JSON. Promote a summary worth keeping into the repository by hand. A run that exhausts its retries keeps its workspace on disk and the path is in the fault; a run that succeeds deletes it.

## The question-file format

One Markdown file per question in `questions/`, the filename's stem as its slug. Four `##` sections, all required and all non-empty:

```markdown
# A short title

## Prompt

What the arms are asked, verbatim. Nobody answers a follow-up, so it must stand alone.

## Verified answer

What the maintainer verified against the owning first-party source, with file-and-line evidence.
Never derived from the skill's own references: that would measure agreement, not truth.

## Rubric

- 6: The first key point.
- 4: The second key point.

## Roots

- decompile
- ui-bundle
```

The validation `core.ts` enforces, each failure named by its own fault code:

- **Rubric**: two to four points, each line `- <weight>: <key point>`, weights summing to 10 — the /10 scale the judge scores on is the rubric's own total.
- **Roots**: `decompile`, `install`, `ui-bundle`, no repeats, and `decompile` always present. A question brings in the roots its answer touches and no more, since every extra root is context both arms pay for.

`mise bench:validate` is that check on its own: it loads every question file and resolves its roots — everything a scored invocation does before it can spend anything — then stops.

The smoke question lives in `tests/fixtures/smoke.md`, deliberately outside `questions/`: a real invocation iterates that directory and must never pick it up.

**A question may rest on prose the benchmark is measuring.** Where its answer touches a claim the shipped reference gets wrong, the treatment arm loses points for reading the skill faithfully — the measurement working, not a flaw in it. What keeps that fair is the rubric scoring what the prompt actually asks. `update-interval-power-of-two` is the live case: it sits on two reference claims the decompile contradicts, and its three rubric points are the throw, the failure's reach and the ignored interval, none of which the reference gets wrong.

## The invocation contract

Every invocation, both arms and the judge:

```
claude -p --output-format json --model <model> --effort <level> --safe-mode --disable-slash-commands
```

Arms add `--tools Read,Glob,Grep --allowed-tools Read,Glob,Grep --add-dir <root>...`; the judge adds `--tools ''` and no directory grant, so it can only read its own prompt. The prompt travels on stdin rather than in argv, which keeps Windows quoting and command-line length out of it.

`--tools` is what denies subagents: `Task` is simply not in the set. `--safe-mode` is the isolation flag — it disables every customization on the machine (CLAUDE.md, skills, plugins, hooks, MCP servers, commands, agents) while leaving auth and permissions working, which is why the treatment arm reads the skill as plain files and loses nothing. `--bare` is ruled out: it keeps skills resolving and restricts auth to an API key this machine does not configure.

### What the smoke run confirmed

Against Claude Code 2.1.226, with a probe invocation carrying the arm flags and reporting on its own environment:

- **Isolation holds.** The probe reported no CLAUDE.md or project instructions in context, and no skills, plugins or MCP servers available.
- **Tool denial holds.** Available tools were exactly `Read`, `Glob`, `Grep`; no shell tool was reachable, despite the environment note mentioning one.
- **Permissions hold under those flags.** Both arms read the decompile from a workspace in the temp directory with `permission_denials: []` in the result JSON, so `--add-dir` plus the bare `Read,Glob,Grep` grants pre-approve the reads with nothing left to prompt for.
- **Usage and duration fields are all present.** The result JSON carries `usage` (input, cache creation, cache read, output), `total_cost_usd`, `num_turns`, `duration_ms` and `duration_api_ms`. The summary reports the API duration and flags wall times as concurrency-distorted only where a run reports none.
- **Both arms and the judge complete end to end**, and the treatment arm cited the copied `SKILL.md` in its answer, so the copy-in and the entry-point path work as intended.

What the smoke run cannot prove is what Opus does with `--effort medium`; that is the preflight the benchmark run owes before the budget commits.

**The `Read` grant is unscoped.** `--allowed-tools Read` pre-approves reads anywhere, not only inside the granted roots; the workspaces sit outside any repository and the prompt forbids reading beyond the roots, so nothing on the machine is in reach by accident, but the flag set is not what enforces that.
