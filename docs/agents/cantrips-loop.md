# The cantrips loop in this repo

What the storage verbs translate to here, which knowledge stores are enabled, and where the roadmap lives.
Storage-touching skills read this doc instead of the plugin defaults.
`/setup-cantrips-loop` wrote it; re-run that skill to change it.

## Storage backend

Local markdown under `.scratch/<feature>/`, a kebab-case feature slug per folder. `.scratch/` is already listed in `.gitignore`, so a write there needs no setup.

A spec and its tickets are working material, not a record: they live in `.scratch/` only while the feature is in flight, and the durable outcome lands in code, git history, and the knowledge stores below. Each feature is copied one way to GitHub issues by `/storage`'s script, the local files ruling.
After publish-spec, and outside Claude Code after any write verb, sync the feature through `/storage` without asking, since its script publishes only in a clone the user marked; a spec opens with a one-line title, a level-one heading on its first line.
A spec or a ticket delivering roadmap items names them on a `Roadmap items:` line, and publishing a spec marks its items in flight through `/roadmap`.
Closing a finished feature is the human's act: close it through `/storage`, with `--done` or `--not-planned` where its tickets are not all resolved, and through `/roadmap`, then delete its folder.

- Mirror: `CitiesSkylinesModding/agents-plugins`
- Features: `.scratch/`

- **Publish the spec** (`publish-spec`) — write the spec to `.scratch/<feature>/spec.md`.
- **Fetch the spec** (`fetch-spec`) — read `.scratch/<feature>/spec.md`.
- **Annotate the spec** (`annotate-spec`) — append the note under a `## Comments` heading at the end of the spec file, each entry prefixed with its date; the body above that heading stays frozen.
- **Publish the tickets** (`publish-tickets`) — write one file per ticket to `.scratch/<feature>/NN-<slug>.md`, numbered from `01` in dependency order, blocking edges as the ticket body's "Blocked by" prose; the shared folder is what ties a ticket to its parent spec. A ticket's body takes the shape `docs/authoring/ticket-template.md` fixes.
- **Fetch the ticket** (`fetch-ticket`) — read the ticket file.
- **Annotate the ticket** (`annotate-ticket`) — append the note, undated, under a `## Comments` heading at the end of the ticket file.
- **Resolve the ticket** (`resolve-ticket`) — add or flip a `Status: resolved` line directly under the ticket's title; the verified acceptance-criterion checkboxes remain as evidence.

Three invariants hold whatever the backend: an annotation appends without touching what is already there and stays time-ordered; an annotation recording a revised decision governs over the body it revises, so later readers judge the spec as amended; and a published ticket is traceable to its parent spec.

## Knowledge stores

- `docs/adr/` — **enabled**. Durable decisions with supersession chains: an outdated record names the record that replaced it. The root `AGENTS.md` section "Where knowledge goes" holds the boundary against the other stores.
- `docs/solutions/` — **enabled**. One file per hard-won problem, reached through a pointer placed where the problem bites.

## Roadmap

Read and write this repo's roadmap through `/roadmap`.

- Backend: `GitHub issues`
- Tracker: `CitiesSkylinesModding/agents-plugins`
- Labels: `gameface`, `unity-devtools`, `cs2-modding`

## Deputy/Arbiter instructions

Always brief the deputy for this:
- Take roadmap entries critically, they are often raw reports from other agents. So you can reject and retire roadmap entries, just ask the user first.
- Prose written for agents is not code: agents are good at reading intention, so short sentences can beat longer ones unless a specific problem is encountered.
- In the same vein, always keep skills as small as possible. Every line is a context cost and an actual cost for the user.
