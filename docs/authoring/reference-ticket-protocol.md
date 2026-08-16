# The reference-ticket protocol

The standing instructions every `cs2-modding` reference ticket runs under — authoring a new reference or re-sweeping a shipped one.
Read it with the ticket: the ticket carries its topic, its questions, its sources and the criteria only it can state, and everything an orchestrator needs every time is here instead — the standing criteria below included.
A ticket's body takes [the ticket template](ticket-template.md); this protocol is what a reference ticket adds on top.

## Standing criteria

Every reference ticket is judged on these as well as on its own, and each carries one line saying so.
They live here rather than in each ticket because they are the machine's obligations rather than a topic's: stamped into every ticket they drift the moment one is patched and the rest are not, which is exactly what happened to the verification loop when the source list changed.
Tick them in the ticket, against this list.

- [ ] The two subagents ran one at a time, and no other reference ticket was in flight while either was.
- [ ] Each subagent wrote its own output at the path its prompt named, rather than returning it for the orchestrator to transcribe. What an agent returns is a report that dies with it; only the file survives.
- [ ] Each subagent re-read its own finished file on disk before handing over, and fixed what that pass caught. A long file written across a long context drifts in ways its author cannot see from memory, and the closing message says what the re-read caught or that it caught nothing.
- [ ] The research file satisfies `docs/research/README.md` — a citation on every claim, the baseline, a verdict wherever the sources disagreed, `Rots:` on what rots, `## Bridge`, `## Dead ends` — with mod names and file-and-line references intact, and it reads as a source rather than as a summary of one.
- [ ] Every question the ticket asks is answered in the research file, or recorded there as one the sources could not answer.
- [ ] Disagreements the pass could not settle were appended to `docs/research/conflicts.md` rather than decided there.
- [ ] A separate authoring subagent produced the reference — from the research file, plus the decompile where the topic is a mechanics one, and from no mod source either way. `writing-for-agents` was loaded and `plugins/cs2-modding/AGENTS.md` read in full before writing, and the output holds to both; the agent also read its topic's family shape doc — `docs/authoring/mechanics-reference-shape.md` or `docs/authoring/technique-reference-shape.md` — and held to the form it fixes.
- [ ] The shipped reference answers the ticket's questions and holds to its boundary. The research-file criterion binds what discovery found; this one binds what shipped, which is the half the fan-out had left to the research file alone.
- [ ] The reference opens with its title, the baseline line under it, and the three-line decompile warning one blank line below that, and carries no front matter — only a `SKILL.md` carries that. It carries `Verified against game version <version>.` exactly once, and every claim that rots carries the `VOLATILE:` token while every claim the pass could not confirm carries `UNVERIFIED:`, the only spellings the lint accepts in any form. Every marker of either kind the authoring agent added is quoted in its closing message, since the maintainer owns which claims count and no lint checks that.
- [ ] The reference sits in a folder of its own, entry file named for the topic, per the `**Outputs.**` path. Anything it disclosed sits beside it with its own title and baseline line, linked by bare filename, and nothing a reader must have to be right went behind a pointer.
- [ ] The orchestrator appended the reference's line — link plus a coarse one-line description — to the going-deeper list in `plugins/cs2-modding/skills/cs2-modding/SKILL.md`. The trunk is a shared file outside the authoring agent's folder, so it is the orchestrator's write rather than the agent's.
- [ ] The reference names no mod. It names no library except Harmony, which patching prose names freely while its package id and pinned version stay in `cs2-mod-project`.
- [ ] The reference names its bridges as backticked slugs rather than markdown links, since a bridge target may not exist yet and the trunk-coherence pass converts every slug once the last reference lands.
- [ ] The setup skill's mod catalog gained what the discovery pass learned: a repository whose source proved to demonstrate a pattern or advanced technique the catalog does not yet name has that added to its **Demonstrates** entry. Each gap was recorded as an inline finding in the research file rather than only in a closing message, and a sweep that came back empty was recorded there as a dead end. The catalog's own rules still bind — it is the one file that names mods, and its entries stay short enough to scan.
- [ ] `docs/SOURCES.md` gained what the pass learned about the sources themselves: an artifact it does not name, or an entry whose path, format or scope turned out wrong. Each was recorded as an inline finding in the research file, and a pass that found nothing to add recorded that too. The list is only as current as the last pass that used it said so.
- [ ] The orchestrator read both finished documents itself, end to end, rather than trusting the agents' closing reports. Where the research file and the shipped reference disagreed, or where either contradicted a reference already shipped, it re-derived the claim from whichever source `docs/SOURCES.md` makes authoritative for it and fixed every file carrying the wrong version — editing another ticket's output is intended here, and the correction belongs to this ticket rather than to a follow-up. What it could not settle went to `docs/research/conflicts.md`.
- [ ] `/simplify` ran on both finished documents, and `/review-gate` ran after it rather than before.
- [ ] `mise check:skill-content` passes.

A ticket still states what these cannot: which claims in its topic rot, its own bridge slugs by name, and whatever its subject makes non-obvious.
The two paths its agents write to are the one thing that belongs above the criteria rather than in them — every ticket carries them on an `**Outputs.**` line, which is what the wrote-its-own-output criterion is ticked against.

## The shape: two agents, one at a time

**Discovery**, then — where a conflict entry binds the topic — **a maintainer ruling**, then **authoring**.

A discovery subagent owns the topic across every source that bears on it and lands a cited research file.
A separate authoring subagent reads that one file — beside the decompile where the topic is a mechanics one, and never the mod corpus either way — and writes the shipped reference.
The split is the **strip gate**: the agent that spent its context reading mod source is the wrong one to trust with never naming a mod.

**Launch them one at a time, and run no second reference ticket while either is in flight.**
A discovery pass costs a large fraction of a session. An orchestrator that fans out loses every agent in flight when it hits a session limit; one that runs a sequence resumes from the last file written.

Where the ticket says a conflict entry binds the topic, that is **three beats rather than two**.
The ruling lands _between_ the stages: it is a ruling on the evidence discovery produced, so it cannot be put honestly before discovery, and the authoring agent reads the research file and never `conflicts.md`, so it must be written in before authoring starts.

**One ruling serves every topic the entry names, and the entry moves once.**
`conflicts.md` moves an entry to `## Ruled` the moment it is ruled — so the first ticket an entry binds consumes it, and every later one finds it already ruled. The pre-launch balance entry bound seven mechanics topics and moved exactly this way.
Consuming the entry does not discharge the middle beat for the topics behind it: the beat is discharged per research file.
Read the entry wherever it now sits, ruled or open. Where it is already ruled, the middle beat is to copy that ruling into _this_ topic's research file at the finding it governs, restating the decision and what the reference owes because of it. Where it is still open, put the question and wait.
`conflicts.md` says the ruler writes the outcome into every topic it touches, and most of those research files do not exist when the ruling is made, so the copying cannot have happened in advance.

**A ruling invalidates what the research file derived, not only the finding it names.**
A discovery pass that ran before the ruling wrote its whole file against the model the ruling overturns, and the finding the ruling lands on is the one place that gets corrected by landing it.
The settings-and-input pass proposed three catalog sentences and two of them asserted the mechanism its ruling had just overturned. The ruling was written in correctly, the reference was authored correctly from it, and the wrong model would still have shipped — because the catalog is the orchestrator's own write and the authoring agent never reads it.
So after writing a ruling in, re-read the file for every claim resting on the overturned model, the `## Bridge` section and the catalog-gap findings included.
A finding that reaches a shipped file without passing through the authoring agent is one nothing else checks.

## The roots

`docs/SOURCES.md` is the list, and the discovery prompt points the agent at it rather than restating it.
The roots no environment variable names are recorded in `~/.cs2-modding/setup.md`, which is the route that works on both supported harnesses.
Read it yourself before launching, and paste the roots the topic needs into the prompt: a subagent starts with none of this.

## Stage 1 — the discovery prompt

Hand the agent the topic, its boundary, its questions, its sources and its bridge obligation from the ticket, plus:

- **Read `docs/SOURCES.md` and `docs/research/README.md` in full before starting.** The first is every source you may read and what each settles; the second is the conventions your file has to satisfy, down to the citation shapes.
- **Read the entries in `docs/research/conflicts.md` that name your topic**, ruled and open alike, and follow that file's entry shape for anything you append.
- **Read the seed surveys your topic touches**, and treat what they say as **leads rather than facts**. They predate the per-claim conventions and carry no verdicts, and repositories have joined and left the checkout since they were written — so a survey lead citing a repository the checkout no longer holds is evidence gone, not a search to widen.
- **The verification loop:** take the claim, check it against the first-party source that owns it — the decompile for C#, the install for the frontend and for anything shipping as data — observe what the corpus does, record the verdict. First-party wins by default; a verdict going the other way carries the evidence that overturned it.
- **Ask the user when the live game would settle it.** The two MCP servers are ordinary tools when connected; when a call fails because the game is not running or the server has not started, ask for it rather than recording the claim as unanswerable.
- **Write the file yourself**, at the path the ticket names. Do not return it for transcription: what you return is a report that dies with you, and only the file survives.
- **Write it as a source, not as a summary of one.** The authoring agent never reads the corpus, and for a technique topic it reads your file and nothing else; for a mechanics topic it reads your file beside the decompile, so what it needs from you is what you checked, the traps, the verdicts and the citations rather than prose narrating the code. The two ways this goes wrong are condensing what you found, and recording only the claims you already guessed the reference would want; either sends the authoring agent back to the corpus and makes the strip gate structural in name only.
- **Record catalog gaps inline**, as a finding of its own naming the catalogued mod, the sentence to add to its **Demonstrates** entry, and the source lines behind it. A gap mentioned only in a closing message is lost with the agent.
- **Record source-list gaps the same way.** `docs/SOURCES.md` is a list somebody wrote on a Monday, and every artifact on it was unlisted until a pass went looking. Where you reach an artifact it does not name, or find an entry wrong or stale — its path moved, its format is not what the entry claims, its "authoritative for" is narrower than the entry says — that is a finding in your file naming the entry and the correction, exactly as a catalog gap is. The list only grows by a pass that used it saying so.
- **Re-read your finished file on disk before handing over**, and fix what that pass catches. A long file written across a long context drifts in ways its author cannot see from memory — a claim restated with a different verdict, a citation that moved, a question the plan answered and the file did not. Say in your closing message what the re-read caught, or that it caught nothing.
- **Append what you cannot settle** to `docs/research/conflicts.md` and move on. A judgement about what ships, a claim no source can settle, a question whose answer changes the product — those are the maintainer's, and the agent holding one topic's context is the reader least able to see what the decision costs elsewhere.

## Stage 2 — the authoring prompt

The boundary and the questions travel to both stages, not just discovery.
An authoring agent given a 460-line research file and no boundary picks its own scope: it writes into a neighbour's territory, or drops a question the research file buried, and every acceptance criterion still passes because the lint checks names, baseline, marker spelling and links.
Its ticket carries a criterion binding the shipped reference to those questions, so hand it what the criterion judges.

Hand the agent the shipped path, **the topic's boundary and its questions** from the ticket, plus:

- **Load the `writing-for-agents` skill before writing**, and hold the output to it.
- **Read `plugins/cs2-modding/AGENTS.md` in full.** Its "shipped-prose contract" section is the contract, and it is the file the lint enforces.
- **Read your topic's family shape doc first and hold to the form it fixes** — `docs/authoring/mechanics-reference-shape.md` for a mechanics topic, `docs/authoring/technique-reference-shape.md` for a technique one: the sections or their absence, the trap format, the listing format where the family has one, and the prose-line budget. The separate-authoring-subagent criterion is ticked against that form, so an agent that never opens the doc cannot satisfy it.
- **Your inputs are the research file the ticket names and, for a mechanics topic, the decompiled game beside it.** A technique reference works from the research file alone; where it is thin, say so in your closing message rather than going to a source, and a technique trap's `Source:` line names the file the research file's own citation names. A mechanics one does not, because an agent writing pointers into code it cannot open compresses a file that already compressed that code, and the branch dropped between the two is what a reader acts on — it also cannot write an openable `Source:` line or diff a listing against a method it was told not to open. Either way, **do not read the mod corpus**: an agent that has read mod source is the wrong one to trust with never naming a mod, which is why this half of the pipeline exists, and a mechanics topic takes nothing from it.
- **The file skeleton — title, baseline line, warning block, then the body — is fixed by `plugins/cs2-modding/AGENTS.md` and your family's shape doc; take it from them rather than from this prompt.** No front matter — only a `SKILL.md` carries that. A file that omits the baseline or the warning block fails the lint the green-check criterion requires.
- **Your reference is a folder, and your entry file repeats the topic name** — the ticket's `**Outputs.**` line gives the exact path. The folder exists even if you write one file into it.
  **Disclose into it where your topic earns it.** Material only some of your readers need — a lookup table, a census, an enumeration, a second procedure serving a different reader — becomes a sibling file in your folder, linked by bare filename, and keeps its own title and its own baseline line.
  What stays in the entry file is everything a reader who follows no pointer needs to be **right**. A pointer carries depth; a rule whose absence makes a reader wrong rather than shallower may not go behind one, and that is the failure this convention has already produced once.
  A topic with nothing to disclose writes one file and is finished; the folder costs it nothing and saves the next pass a move.
- **The baseline line**, exactly once: `Verified against game version <version>.`
- **`VOLATILE:` is the only spelling of that word the lint accepts**, in any form and any case. Reach for _rots_, _moves between versions_ or _version-dependent_ when you want the English adjective — the token greps into the next version's maintenance checklist, and a second spelling would put an unchecked claim on it. Report every marker you add, quoted, in your closing message: the maintainer owns which claims count.
- **State every technique on its own authority, crediting no repository.** That is the rule; the name list below is the tripwire. The lint matches catalogued names whole-word and case-sensitively, at two strengths. A **failure** names a mod your prose must not name: remove the credit and state the technique on its own authority. A **`WARNING:`** names a display name the lint's ordinary-word list has declared ordinary English, and it is a question rather than a stop — answer it in your closing message: a credit is removed exactly as a failure is, and where the word is your subject, say so and leave the sentence alone. **Where the check fails on a word your topic genuinely owns, the fix is the maintainer adding it to the lint's ordinary-word list in `scripts/check-skill-content.ts`, not your prose** — `plugins/cs2-modding/AGENTS.md` rules exactly that, and bending a sentence around a word the subject needs is the outcome that rule exists to prevent. Report the collision and stop; do not reword.
- **Balance values do not ship, in any form** — not baked, and not linked either. Name the component and the field instead, with the access shape where that is not a plain singleton read, and a ratio derived over such values goes with them, an adverb carrying it included. A mechanism table is the opposite case and gets baked, because an agent cannot write the code without it. The test is balance against mechanism, not table against prose. `docs/adr/0004-a-mechanics-reference-names-the-component-not-the-balance-value.md` carries the reasoning.
- **Re-read your finished reference on disk before handing over**, and fix what that pass catches. A long file written across a long context drifts in ways its author cannot see from memory — a question the research file answered and the reference dropped, a claim that rots and carries no marker, a sentence that wandered past the boundary. Say in your closing message what the re-read caught, or that it caught nothing.
- **Bridges are backticked slugs, never markdown links.** The reference tickets do not block each other, so your bridge targets may not exist yet, and the lint asserts that every markdown link in the shipped tree resolves on disk. The trunk-coherence pass converts every slug into a resolving pointer once the last reference lands.

### The name list to hand the authoring agent

**Harvest it fresh from `plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md` each time**, rather than pasting a snapshot from here.
That file is the lint's only input for names, so a list taken from it cannot disagree with the check; a list copied into this protocol can, and will — the catalog gains entries from the catalog-feedback criterion above.
Two spellings per entry, both matched: the `###` heading's display name, and the `owner/repo` slug in its `Source:` line.

Which names warn rather than fail is the script's own ordinary-word list, not anything the catalog declares.
A topic whose own subject collides with a catalogued name reports the failure to the maintainer, who owns that list, rather than rewording.
All of that is orientation for the orchestrator, not the list to hand over.

## The orchestrator's own pass, after both agents

Read both finished documents yourself, end to end. An agent's closing report is its own account of its work, and the two failures that matter are invisible from it: a claim the research file establishes and the reference states differently, and a claim either of them establishes that contradicts a reference already shipped.

The second is the one the machine produces by design. Each topic owns a slice, the slices overlap at the edges, and a later discovery pass reads the same source more closely than an earlier one did. The prefabs-and-assets pass found `mod-lifecycle-and-ordering` shipping `PrefabUpdate` as gated on pending prefab updates when the phase is driven unconditionally every frame — a fact the tracer pass had no reason to look at twice and the later pass could not avoid.

**Fix it, do not report it.** Re-derive the claim from whichever source `docs/SOURCES.md` makes authoritative for it — an agent's finding about a third file is a lead, not a verdict — then correct every file carrying the wrong version, the research file included.
For a frontend or shipped-data claim that is the install and not the decompile, and a grep of `src/` coming back empty is not a re-derivation. Editing another ticket's shipped output is intended: a reference nobody may touch after its ticket resolves is a reference that only gets more wrong, and the correction belongs to the ticket that found it rather than to a follow-up that may never be written.

Where the disagreement is a judgement rather than a fact, it goes to `docs/research/conflicts.md` like any other.

**The mechanical pass is yours or a dispatched finder's, never the author's.** The family shape docs state it — every `Source:` line opened and its claim confirmed, and on a mechanics topic every listing diffed against its method — and authoring cannot audit its own claims, so it runs here, before any prose review.

**Then `/simplify`, and only then `/review-gate`** — both on the finished pair, and after this pass rather than instead of it. This pass settles what is true, `/simplify` cuts what no reader acts on, the gate re-derives what survives; `plugins/cs2-modding/AGENTS.md` owns why the order is fixed.

**Check every bridge slug against the approved topic set before resolving** — the reference folders on disk, plus the structure document the run's tickets are scoped by for topics not yet authored. A discovery agent asked for a `## Bridge` section will name a topic that reads right and does not exist — the placement-definitions pass asserted `terrain-and-water`, whose material belongs to `environment-and-pollution`, and the same phantom had been sitting in the custom-tools research file undetected. Nothing catches this on its own: the lint asserts that markdown _links_ resolve, and a bridge is deliberately a backticked slug until the trunk-coherence pass converts it, so an invented slug passes every check until that conversion fails on it. Grep the finished reference for its slugs and diff them against the approved set; it costs one command.

## Corrections worth their own heading

**Phrase headline material as the question the topic must answer, not as the answer.**
The tracer ticket's own criterion stated "a lifecycle hook which throws silently disables the system" as a fact. Discovery found four wrapped hooks with three different behaviours.
The criterion was rounded rather than wrong, and an agent reading it as a fact to confirm would have shipped the rounded version and passed every check.
Each ticket's questions are written that way. Keep them that way when you pass them on.

**Coverage is the ticket's job, not the conventions'.**
`docs/research/README.md` binds a research file claim by claim and says nothing about how much ground it covers, so a thin file satisfies every written rule and the gap surfaces only when the shipped reference reads thin.
The ticket's question list is what closes that, which is why a question the sources could not answer is recorded in the research file rather than dropped.

**A registration block is not a unit of ownership.**
Two mechanics topics reading the same contiguous block in `SystemOrder.cs` do not split it where the systems are registered: ownership follows the role of the thing each system drives, and the seam the approved decomposition already drew is what settles it.
Where a mechanism then runs across the seam, the topic owning the object keeps the object and bridges to the topic owning the loop rather than drawing a boundary through the middle of a method.
Ruled in `docs/research/conflicts.md` over the vehicle `*AISystem`s.
