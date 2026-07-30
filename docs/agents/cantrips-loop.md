# The cantrips loop in this repo

What the six storage verbs translate to here, and which knowledge stores are enabled.
Storage-touching skills read this doc instead of the plugin defaults.
`/setup-cantrips-loop` wrote it; re-run that skill to change it.

## Storage backend

Local markdown under `.scratch/<feature>/`, a kebab-case feature slug per folder. `.scratch/` is already listed in `.gitignore`, so a write there needs no setup.

A spec and its tickets are working material, not a record: they live in `.scratch/` only while the feature is in flight, and the durable outcome lands in code, git history, and the knowledge stores below. Closing a finished feature is the human's act — they delete `.scratch/<feature>/` once it no longer serves.

- **Publish the spec** (`publish-spec`) — write the spec to `.scratch/<feature>/spec.md`.
- **Fetch the spec** (`fetch-spec`) — read `.scratch/<feature>/spec.md`.
- **Annotate the spec** (`annotate-spec`) — append the note under a `## Comments` heading at the end of the spec file, each entry prefixed with its date; the body above that heading stays frozen.
- **Publish the tickets** (`publish-tickets`) — write one file per ticket to `.scratch/<feature>/NN-<slug>.md`, numbered from `01` in dependency order, blocking edges as the ticket body's "Blocked by" prose; the shared folder is what ties a ticket to its parent spec.
- **Fetch the ticket** (`fetch-ticket`) — read the ticket file.
- **Resolve the ticket** (`resolve-ticket`) — add or flip a `Status: resolved` line directly under the ticket's title; the verified acceptance-criterion checkboxes remain as evidence.

Three invariants hold whatever the backend: an annotation appends without touching what is already there and stays time-ordered; an annotation recording a revised decision governs over the body it revises, so later readers judge the spec as amended; and a published ticket is traceable to its parent spec.

## Knowledge stores

- `docs/adr/` — **enabled**. Durable decisions with supersession chains: an outdated record names the record that replaced it. The root `AGENTS.md` section "Where knowledge goes" holds the boundary against the other stores.
- `docs/solutions/` — **enabled**. One file per hard-won problem, reached through a pointer placed where the problem bites.
