# 05 — Type catalog and `find_types` search

**What to build:** an agent that knows a concept but not a namespace asks the running game what
types exist, instead of leaving the session to harvest candidate names from a decompiler. A new
`search` parameter on `find_types` takes a regular expression and returns matching types from the
live process, each with its assembly and kind. The existing `fullName` parameter is untouched:
exact, cheap, and now pointing at `search` when it finds nothing.

The type list is harvested once per session into a session-scoped catalog and searched client-side
afterwards, so iterating on a pattern is free. Harvest is one invoke per assembly, joining that
assembly's type names into a single string. Every search first spends one invoke on the assembly
name list and harvests only assemblies not already held, so a mod loaded mid-session becomes
findable without anyone remembering to ask for a refresh. The catalog dies with the session.

Measured live on the reference target, so the sizing is known rather than guessed: 165 assemblies,
38191 types, roughly 1.5–2 MB in total, and a full harvest on the order of a second or two. Because
harvest runs as invokes on the game's main thread, the first search of a session is the longest
single freeze this plugin introduces, and the skill should say so.

Two parameters with two very different costs sit on one tool deliberately: an agent discovers
`search` where it already looks, while a typo'd exact name cannot silently trigger the harvest.

**Blocked by:** None — can start immediately, independent of the ECS chain.

- [ ] A session-scoped type catalog harvests assembly type names one invoke per assembly and caches
      them client-side.
- [ ] Each search spends one invoke diffing the live assembly-name list and harvests only assemblies
      it has not seen.
- [ ] The catalog is discarded with the session, so a reattach re-harvests.
- [ ] `search` matches an unanchored .NET regex against full type names, so a plain fragment behaves
      as a substring search.
- [ ] Matching is case-insensitive by default, consistent with the exact resolve on the same tool.
- [ ] Results whose short name matches rank above results that matched only in the namespace.
- [ ] An invalid pattern fails with the runtime's own regex message quoted verbatim.
- [ ] A pathological pattern fails on a match timeout rather than hanging the call.
- [ ] Results report an exact match count, a listing capped by a limit defaulting to 50, and the
      number omitted.
- [ ] Each hit reuses the existing type-description shape — full name, assembly, kind — with no new
      result type introduced.
- [ ] Requesting members alongside a search is refused above a small match count, with an error
      naming the exact-name parameter or a tighter pattern as the way forward.
- [ ] The "type not found" error on the exact parameter names `search` as the escape.
- [ ] The parameter descriptions state that `search` is for authored patterns and the exact
      parameter for pasted names, since type names carry regex metacharacters of their own — nested
      types render with `+`, generics with a backtick arity and bracketed arguments.
- [ ] Integration tests drive the catalog against the Mono debuggee, covering matching, the
      case-insensitive default, short-name ranking, count against limit and omitted, the invalid
      pattern message, the match timeout, and incremental refresh after an assembly is loaded into
      the debuggee mid-session.
- [ ] The driving skill loses the passage telling agents to harvest names offline, gains the search
      workflow, and notes the first-search freeze.
