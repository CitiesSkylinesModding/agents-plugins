# The skill-efficacy benchmark

Measures whether an agent with the four `cs2-*` skills installed writes better Cities: Skylines II mod code than the same model with only the game's own sources, and what it costs.

Each task is a request a modder would type: a feature request, or a bug report on seeded code. Two arms get the same prompt in isolated headless workspaces, several times each, and write files. A judge that holds the task's checklist then rules on each item: did the code fall into the trap, avoid it, or never meet it.

- control: a fresh workspace, seeded from the task where it has a seed.
- treatment: the same, plus the four `cs2-*` skill folders copied to `.claude/skills/`, where the CLI loads them as real skills. The prompt never mentions them, so a skill that fails to trigger shows up as a failure.

The prompt is byte-identical across arms. It's the request, then where the task's source roots are on this machine, then two closing lines: write the code without asking questions, and don't build, test or run anything.

The layout is two files. `core.ts` holds every decision: task parsing, the arm and judge plans, stream reading, file collection, contamination, the retry plan, verdict parsing, resume planning, calibration, aggregation, the report and the results file. It's what the tests drive. `run.ts` is a deliberately untested shell around it: workspaces, spawning, file system and clock.

## Running it

```bash
mise bench:test                     # the core's unit tests
mise bench:validate                 # the task folders parse and their roots resolve; spends nothing
mise bench:calibrate                # judge each task's two samples; spends judge calls only
mise bench:run                      # the real thing: Opus, real spend
mise bench:run --task-budget 3      # the same, stopping after three whole tasks
mise bench:run --resume bench/results/<timestamp> --task-budget 3   # the next slice
mise bench:promote bench/results/<timestamp>                         # write a finished run into results.md
mise bench:run --smoke --runs 1 --model sonnet --judge-model sonnet --effort low --timeout-minutes 10
```

Flags, all optional:

| Flag                    | Default         | What it does                                                                         |
| ----------------------- | --------------- | ------------------------------------------------------------------------------------ |
| `--smoke`               | off             | Takes the fixture tasks in `tests/fixtures/` instead of `tasks/`.                    |
| `--runs <n>`            | `4`             | Runs per arm per task.                                                               |
| `--concurrency <n>`     | `2`             | Jobs in flight at once. Also the throttle on spend rate.                             |
| `--model <id>`          | `opus`          | The model both arms run.                                                             |
| `--judge-model <id>`    | `opus`          | The model that judges.                                                               |
| `--effort <level>`      | `medium`        | Effort level for every invocation, arms and judge.                                   |
| `--timeout-minutes <n>` | `20`            | Per invocation, after which the attempt counts as an infrastructure failure.        |
| `--task-budget <n>`     | none            | Stops after this many whole tasks, each in both arms for every run.                  |
| `--resume <dir>`        | none            | Resumes a run directory instead of starting one.                                     |
| `--validate-only`       | off             | Loads and validates the tasks and the fixture, resolves the roots, then stops.       |
| `--calibrate`           | off             | Judges each task's two calibration samples instead of running the arms.              |
| `--promote <dir>`       | none            | Writes a finished run directory into `results.md`.                                   |
| `--tasks <dir>`         | `bench/tasks`   | Where the task folders live.                                                         |
| `--results <dir>`       | `bench/results` | Where output lands.                                                                  |

An invocation is tasks × 2 arms × runs agent sessions, plus one judge call per job that wrote code. That's real money; `--concurrency` is the throttle.

**Source roots are resolved before the first invocation.** The decompile and the reformatted UI bundle copy come from the setup record at `~/.cs2-modding/setup.md`, the install from `CSII_INSTALLATIONPATH` with the record's `Game install` line as the fallback. A root that resolves to nothing, or to `(none)`, aborts the whole invocation rather than running an uneven comparison.

Results land in `bench/results/<timestamp>/`, which is gitignored: `run.json`, `summary.json`, `summary.md`, and a `raw/<task>-<arm>-<run>/` directory per job. It holds the prompt, each attempt's streamed output and stderr, the files the judge read under `files/`, the judge prompt, each judge reply and its stderr, and `record.json`, the job's whole record. A workspace is deleted once its files are copied out. An attempt that failed keeps its workspace, and its fault names the path.

### Resuming and slicing a run

A run directory is its own ledger. `run.json` fixes the run's model, effort, judge model, runs per arm and task list when the directory is created. `--resume` keeps them, so it refuses `--smoke`, `--tasks`, `--runs`, `--model`, `--judge-model` and `--effort`. `--concurrency`, `--timeout-minutes` and `--task-budget` can change from one slice to the next. A resume also refuses a task whose folder changed since the run began. Its jobs would otherwise run and be judged against two versions of the task.

A resume skips every job whose `record.json` says `judged` or `no-code`: both are results. Any other job is rerun whole from a fresh job directory. That covers a job an interruption cut off, which has no record yet, and a `failed` one, since an infrastructure failure measures nothing about the arm. A record is renamed into place once it's complete, so an interruption never leaves half of one. Ctrl-C kills the CLI sessions in flight along with the driver.

`--task-budget <n>` stops an invocation after `n` whole tasks, each run in both arms for every run of the task, so a slice always ends on a task boundary. A finished task doesn't count against the budget. A task an earlier slice started counts as one, however few of its jobs are left.

The summary is rebuilt from the records on disk after every job, so an interrupted run still has a readable `summary.md`. A task with a job not yet final is listed under "Unfinished tasks" and kept out of every score, so a partial report compares the arms on the same tasks. Its jobs still show up in the runs table.

There's no skill pin. A resume against changed skills goes ahead, and each record says which skills it ran against.

### Calibration

`mise bench:calibrate` judges each task's `falls/` and `avoids/` samples, with the same judge prompt and invocation a run uses. A task is calibrated when every item fails on `falls/` and passes on `avoids/`. A not-applicable ruling on either sample fails it, since both samples meet every item by construction. The command prints each ruling with its quoted line, then the misses, and exits nonzero when any task fails. Judge logs land in `bench/results/calibration-<timestamp>/`.

A task enters `tasks/` only once it calibrates. Calibrate again whenever a checklist or the judge prompt changes.

### Promotion

`mise bench:promote <run directory>` writes a finished run into `bench/results.md`, the one tracked results file. Each promotion replaces the last, and git history holds the series. The file gives the run's date, model, effort, runs and judge, then every skills commit its jobs ran against, with a line saying so if any job ran on uncommitted skill changes. Then come the summary tables: per arm, per task kind, per task, per item, and the references treatment runs read. The per-run rows and contamination findings stay in the run directory, since they name paths on this machine.

A run with an unfinished task is refused: resume it first.

## The task format

One folder per task in `tasks/`, the folder name as its slug:

```
tasks/<slug>/
  task.md     the request, the checklist, the roots
  falls/      a solution that falls into every trap on the checklist
  avoids/     a solution that avoids them all
  seed/       optional: files copied into the workspace before the run
```

A bug report seeds from `falls/`, which is why it takes no `seed/` folder. A feature request may have one, for surrounding mod code the request builds on.

`task.md` has a title and four `##` sections, all required and non-empty:

```markdown
# A short title

## Kind

feature request

## Prompt

The request, in a modder's voice. Nothing in it names the trap, skills, documentation, the web or a judge.

## Checklist

### The update interval is a power of two

- Falls in: what the code looks like when it falls into the trap.
- Avoids: what it looks like when it avoids it.
- Reference: cs2-modding/references/technique/mod-lifecycle-and-ordering/mod-lifecycle-and-ordering.md
- Source: src/Game/Game/UpdateSystem.cs:288

## Roots

- decompile
```

The validation `core.ts` enforces, each failure named by its own fault code:

- Kind: `feature request` or `bug report`.
- Checklist: three to six `###` items with distinct names, each carrying exactly the four fields. `Reference` is a path under `plugins/cs2-modding/skills/` and must exist there; `Source` cites the decompile line that verifies the item.
- Roots: `decompile`, `install`, `ui-bundle`, no repeats, and `decompile` always present. A task brings in the roots its request touches and no more.
- Folders: `falls/` and `avoids/` exist and aren't empty, and a bug report has no `seed/`.

`mise bench:validate` is that check on its own. It loads every task folder and the fixture and resolves their roots, which is everything a scored invocation does before it can spend anything, then stops.

The fixture task lives in `tests/fixtures/`, outside `tasks/`, so a real invocation never picks it up. `--smoke` runs or calibrates it in place of `tasks/`.

## What a job records

A job is one run of one task in one arm. Its record holds:

- Every tool call, read off the CLI's `stream-json` output: name, input, error, whether a deny rule refused it, the URLs a web search returned, and whether a skill ran forked. The session transcript is never read, since its format is internal.
- The workspace's files, from snapshots taken before and after the run. The ones it created or changed are the files the record lists, and they say whether it wrote code. The judge gets all of them, so a seeded bug the run never touched is still in front of it. Build output (`bin/`, `obj/`, `node_modules/`) and the workspace's `.claude/` are left out.
- The final message, and the cost, turns and time of every attempt, discarded ones included.
- The skills it ran against: the last commit touching `plugins/cs2-modding/skills/`, and whether the working tree changed them.
- Skill use: the `Skill` calls naming a `cs2-*` skill, and the skill files read through `Read` or named in shell text.
- Contamination, defined below.
- The judge's verdicts: pass, fail or not-applicable per item, each with the line of code it quoted.

A run that wrote no code is asked again, whole, with the same prompt. That retry shares the cap of three attempts with infrastructure failures, and the last attempt decides what a job at the cap is: a no-code run or a failed one. Both are listed in the report and left out of the pass rate.

The judge gets the request without its roots, the checklist without its references, the collected files and the final message. It has no tools and is never told the arm. An unreadable reply is retried under the same cap.

A run is contaminated when a call read outside its workspace and roots, named a denied location in shell text, reached this repository on the web, or ran a skill as a forked subagent. A call a deny rule refused never ran, so it doesn't count. Reading back a large tool output the CLI saved under `~/.claude/projects/<project>/<session>/tool-results/` doesn't count either: that's the arm reading its own output. A contaminated run is listed with its findings and kept out of every score.

The report gives, per arm, the pass rate over applicable items, the scored runs, the share of finished runs that loaded a `cs2-*` skill, and the contaminated and no-code counts with mean cost, turns and duration. Then the pass rates per task kind and per task, the pass/fail/not-applicable counts per item, a row per run, the unfinished tasks, the skill files treatment runs read, and the runs left out of the scores.

## The invocation contract

Every invocation, both arms and the judge, runs in a fresh temp directory of its own, with the prompt on stdin:

```
claude -p --output-format stream-json --verbose --model <model> --effort <level> \
  --setting-sources project --strict-mcp-config
```

Each directory holds a `.claude/settings.json`:

```json
{
  "autoMemoryEnabled": false,
  "permissions": {
    "deny": [
      "Read(/<this repo>/**)",
      "Read(/<home>/.claude/**)",
      "Read(/<home>/.cs2-modding/**)",
      "Skill(code-review)"
    ]
  }
}
```

The control arm's deny list adds `Read(/<tmp>/cs2-bench-*/.claude/skills/**)`, which covers the skill copies in any treatment workspace running at the same time. `cs2-bench-` is the prefix the harness gives every workspace directory, so the rule and the harness change together.
`<this repo>`, `<home>` and `<tmp>` are absolute paths, so every rule opens with `//`, which is how a permission rule spells an absolute path.

The arms add:

```
--permission-mode default --permission-prompts none \
--tools Read,Glob,Grep,Write,Edit,Bash,WebSearch,WebFetch,Skill \
--allowed-tools 'Read,Glob,Grep,Bash,WebSearch,WebFetch,Skill,Edit(/<workspace>/**),Write(/<workspace>/**)' \
--add-dir <root>...
```

- **treatment**: the four `cs2-*` skill folders are copied into `<workspace>/.claude/skills/`.
- **control**: no skills folder.

The judge adds `--disable-slash-commands --tools ''` and gets no directory grant.

What each flag is for:

- `--setting-sources project` is the isolation flag. It skips the user's settings, so their CLAUDE.md, rules, skills, plugins, hooks and output style never load. It still loads the workspace's own settings, which brings in the skills and the deny rules.
- `--strict-mcp-config` keeps out the account's claude.ai connectors. Without it they show up, and in one run they arrived only after the `init` event.
- `autoMemoryEnabled: false` keeps the auto-memory section out of the context.
- `--permission-prompts none` denies anything not pre-approved. That's what makes the scoped `Edit`/`Write` grant hold. `--permission-mode default` pins the mode so it never comes from a settings file.
- `--tools` keeps the agent tool out of the set. The `Skill(code-review)` deny rule blocks the one bundled skill seen running as a subagent.
- `stream-json` is where the tool-call record comes from. In print mode the CLI refuses it without `--verbose`.

`--safe-mode`, the previous isolation flag, is ruled out. It blocks workspace skills, and skills from `--plugin-dir` too, and it still applied the user's `defaultMode` (the run reported `permissionMode: "auto"`).
`--restricted` is ruled out as well: workspace skills don't load under it, and it confines the file tools but not the shell.
`--bare` fails with `Not logged in`.

### What the probe confirmed

These were run against Claude Code 2.1.289, on Sonnet at low effort. Each probe reported on its own environment, then made a fixed series of reads, writes, shell commands and web calls.

- **The cs2 skills are the only skills the arms don't share.** Both arms' `init` events list the same 19 skills the CLI bundles (`dataviz`, `claude-api`, `run` and others). The treatment's adds `cs2-mod-project`, `cs2-modding`, `cs2-modding-setup` and `cs2-modding-ui`, and nothing else. Either arm can call a bundled skill, so the record of skill loads counts only `Skill` calls naming a `cs2-*` skill.
  Two treatment runs got a modder's request to log blackouts once per in-game day, with no mention of skills. Both opened with `Skill {"skill":"cs2-modding"}` and then read references.
- **None of the machine's customizations load.** Both arms and the judge reported no CLAUDE.md, rules, memory or output style. Their `init` events list no MCP server, and no plugin beyond the CLI's three builtins (`cc-plugin-agents-md`, `cc-plugin-telemetry`, `cc-plugin-plugin-authoring`). The judge's lists no tools and no skills. With the user's settings loaded, a short prompt emitted 14 hook events. Under the contract it emitted none.
- **The logged-in account works.** Runs report `"apiKeySource":"none"` with no API key in the environment.
- **Workspace deny rules are honoured.** `Read`, `Grep` and `Glob` on a denied path all fail with a permission error. The shell is denied for `cat`, `head`, `ls`, `find` and `cd <denied> && head`. But `python3 -c "print(open('<denied file>').read())"` went through.
- **Shell and web tools run unprompted.** `Bash`, `WebSearch` and `WebFetch` all returned results with no prompt and no denial.
- **The agent tool is gone, but a bundled skill can still fork.** The arms' `init` tool lists hold no agent tool. In a control probe, `Skill` on the bundled `code-review` returned `"status":"forked"` with an agent id, and three events carried a parent tool call. With `Skill(code-review)` denied, the same call fails with `Skill execution blocked by permission rules`. `Skill` calls on `deep-research` and `batch` fail with `disable-model-invocation`.
- **The write grant is scoped.** `Write` and `Edit` inside the workspace succeed. `Write` or `Edit` on a file in `/tmp` outside it is denied, and so is `Write` into the granted decompile root. With an unscoped `Write` grant, that same `/tmp` write succeeded. `echo outside > /tmp/...` through the shell went through under the scoped grant as well.
- **The stream carries every tool's input.** Each `tool_use` event has its input: the `Bash` command, the `file_path` of `Read`, `Write` and `Edit`, the `path` of `Grep` and `Glob`, the `WebSearch` query, the `WebFetch` URL and the `Skill` name. A `WebSearch` result's `tool_use_result` lists the result URLs. Every denied call appears in the result's `permission_denials`.
- **The fields the bench records are all there.** The closing `result` event carries `total_cost_usd`, `num_turns`, `duration_ms`, `duration_api_ms` and `usage` (input, cache creation, cache read, output).

An arm doesn't always reach files through `Read` and `Write`. One treatment run wrote its answer with a shell heredoc and read references with `cat`. That's why file collection diffs the workspace, and why the reference-read record has to parse shell command text as well.

### Which protections are enforced

| Protection                                                                       | Status                                                                                                   |
| -------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| Skills kept from the control arm: loaded as skills                               | Enforced                                                                                                 |
| Skills kept from the control arm: read from a concurrent treatment workspace     | Enforced by the control's extra deny rule for `Read`, `Grep` and plain shell reads; detected-only past that |
| Reads of this repository, the Claude config directory and the cs2 setup directory | Enforced for `Read`, `Grep`, `Glob` and plain shell commands naming the path; detected-only through an interpreter or any other shell form the CLI's permission check misses |
| Reads anywhere else outside the workspace and roots | Detected for `Read`, `Grep` and `Glob`; through the shell, only reads naming a denied location or another workspace are detected. The grants are unscoped |
| Writes outside the workspace                                                     | Enforced for `Write` and `Edit`; through the shell, recorded but not classified                          |
| Subagents | Enforced for the agent tool and `code-review`; detected-only for any other bundled skill that forks, through its `"status":"forked"` result |
| This repository reached on the web                                               | Detected-only                                                                                            |

Detection scans the recorded tool calls, shell command text included, which makes it best-effort.
