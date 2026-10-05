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
| Reads anywhere else outside the workspace and roots | Detected-only: the `Read`, `Grep`, `Glob` and `Bash` grants are unscoped |
| Writes outside the workspace                                                     | Enforced for `Write` and `Edit`; detected-only through the shell                                         |
| Subagents | Enforced for the agent tool and `code-review`; detected-only for any other bundled skill that forks, through its `"status":"forked"` result |
| This repository reached on the web                                               | Detected-only                                                                                            |

Detection scans the recorded tool calls, shell command text included, which makes it best-effort.
