---
name: scan-mcp-failures
description: Mines this machine's agent transcripts for failed gameface, unity and cs2-dev MCP calls, and turns them into roadmap hits and items.
disable-model-invocation: true
---

# Scanning transcripts for MCP failures

Every failed `gameface`, `unity` or `cs2-dev` call an agent made is evidence of a gap: grammar `eval` lacks, a message that sent the agent the wrong way, a mode a tool is missing. This skill counts the failures no earlier run counted, and lands them through `/roadmap` as hits on the item each one bears on, or as new items.

[`scan.ts`](scan.ts) does the mining; its header states the ledger contract. Its state lives in `.scratch/mcp-failures/` and is machine-local, like the transcripts it reads.

## 1. Scan

Run `bun .agents/skills/scan-mcp-failures/scan.ts`. It prints the totals and one line per error signature, and writes every failure to `.scratch/mcp-failures/failures.jsonl` with its input, cleaned error and the agent's next four moves.

With no failures, go to "Record". A run with no ledger (a first run on this machine, or a wiped `.scratch/`) recounts failures the hits already hold: count only those dated after an item's last hit, in its body or its comments.
When failures run into the hundreds, hand "Group by root cause" to a subagent with this file as its brief; it runs that step only, returns its classes, and writes nothing.

## 2. Group by root cause

Read every failure in `failures.jsonl` and sort each into a **class**: one root cause, one fix.
A signature is a grouping hint and nothing more — `method … not found`, `no … accepts these … argument(s)`, and its older form `no overload … accepts` each front several causes, so classify on the method called and its declared signature, reading the plugin source where the message leaves it open.
The `next` moves are the richest field: the workaround the agent reached for says what it wanted, and an abandoned call marks a gap with no workaround.

Each class takes one kind:

- **Product gap**: a tool or grammar limit, or a message that misled.
- **Agent mistake the product could absorb**: a wrong name, a reflex syntax, a trap a hint or a suggestion would catch.
- **Environment**: the game not running, a port closed, a user-side crash. Counted in the report, never roadmapped.
- **Agent logic**: an in-game exception the agent's own code raised. Counted, never roadmapped.

Done when every failure sits in exactly one class.

## 3. Park each class

Date-check each gap or absorbable mistake: `git log --since=<earliest failure date> -- plugins/<plugin>`, then the current code. A failure that predates a commit fixing it is no hit; a class the code has since fixed is dropped from the report with the commit that fixed it.

`park` each remaining class through `/roadmap`, its body the count, the date range and the verbatim input and error that evidence it.

## 4. Drain

Run a bare `/roadmap`: it settles each class as a hit on the item whose fix would have prevented it, matched by subject rather than by message text, its evidence line carrying the count and date range, or proposes it as a new item. Report the environment and agent-logic counts beside it.

## 5. Record

Run `bun .agents/skills/scan-mcp-failures/scan.ts --record`, which promotes this run's watermarks to the ledger. Record only once the drain has settled every candidate, including a run whose proposals were all dropped; a run abandoned before then stays unrecorded, and the next scan finds its failures again.
