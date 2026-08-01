---
date: 2026-08-01
area: research against the community mod corpus
symptoms:
  - 'gh search code returns an empty array for a repository that does contain the term'
  - 'HTTP 403: API rate limit exceeded for user ID'
tags: [github, gh-cli, rate-limit, corpus, research, verification]
---

# Verifying a claim across many GitHub repositories dies on the code-search quota

## Problem

Checking one fact against every repository in the mod corpus — does each project pin the same version
of a dependency — is a per-repository query, and the obvious tool for it gives up a fifth of the way
through while looking like it succeeded.

## What didn't work

`gh search code "<term>" --repo <owner>/<name>` per repository. Code search has its own quota,
**10 requests per minute**, separate from and far below the 5000/hour core API limit that
`gh api rate_limit` shows first. The sweep ran 10 repositories and then failed.

The failure is the expensive part: piping through `2>/dev/null`, or reading only the parsed field, turns
an exhausted quota into an **empty result set**. Four repositories read as "does not reference this
dependency" when they had simply been throttled — a wrong answer that looks exactly like a right one,
in the middle of a run whose whole point was to establish unanimity.

## Root cause

`gh api rate_limit --jq .resources` reports three independent buckets. `code_search` is
`{"limit": 10}`; `search` is `{"limit": 30}`; `core` is `{"limit": 5000}`. Exhausting one says nothing
about the others, so a run can be dead in the water with 5000 core requests still available.

## Fix

Reach the same files through the core quota instead — the default branch, the recursive tree, then the
raw file:

```bash
br=$(gh api "repos/$r" --jq .default_branch)
paths=$(gh api "repos/$r/git/trees/$br?recursive=1" --jq '.tree[].path' | grep -i '\.csproj$')
curl -sL "https://raw.githubusercontent.com/$r/$br/$p"
```

Two core calls per repository plus raw fetches that count against nothing. Twenty repositories cost
about forty of five thousand requests.

## Prevention

Let a failed request be loud: no `2>/dev/null` around a quota-limited call, and treat an empty result
during a sweep as unproven rather than as a negative. A sweep establishing that something is _unanimous_
has to distinguish "checked, absent" from "never checked" — count the repositories actually answered and
compare that against the number asked for.
