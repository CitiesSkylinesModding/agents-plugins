---
date: 2026-07-30
updated: 2026-10-17
area: plugins/unity-devtools/sdb
symptoms:
  - 'a null check on an invoke result never fires'
  - 'string.Join over a mirrored array returns nothing'
  - "'Retrieve the LoaderExceptions property for more information'"
  - 'System.Int32 reported as a class, or a null passed to a struct parameter'
  - 'an out argument of a nullable or a generic struct disconnects the VM'
  - 'an omitted `in` generic-struct default disconnects the VM'
  - 'a generic struct passed by `ref` or into an `in` parameter disconnects the VM'
  - 'a pointer argument arrives at a different address, or a null pointer write fails'
  - 'a frame local reads back its old value after a call in the same pause'
  - 'a static field reads as zero'
tags: [sdb, mono, unity, invoke, null-handling, reflection, pointers]
---

# What a Mono debuggee hands back is not what .NET would

## Problem

Code that reads a value or an exception back from the debuggee gets answers shaped by Unity's Mono
fork and by the SDB value model, not by the .NET a reader is picturing. Each divergence below cost
real time, and two of them were argued as bugs from the .NET reference source before being measured.

## Root cause

**A debuggee null is a MIRRORED null.** An invoke returning null answers
`PrimitiveValue { Value: null }`, not a C# null, so `is {}` and `is not null` both pass and the null
travels on into the next invoke, where the game throws on it. The predicate the evaluator uses is
`EvalInterpreter.IsNull` (`value is null or PrimitiveValue { Value: null }`), and it is private, so
every new call site rediscovers this.

**`string.Join(string, object[])` does not short-circuit on a leading null.** The .NET Framework
reference implementation opens with `if (values.Length == 0 || values[0] == null) return
String.Empty;`, so a sparse array whose FIRST slot is null renders as the empty string. Mono's does
not: it renders every null slot as an empty entry wherever it sits, first included. Joining a sparse
array debuggee-side is therefore safe here, and the gaps arrive as empty entries to drop
client-side.

**`ReflectionTypeLoadException.Message` already names the missing assemblies.** Where .NET Framework
gives only "Unable to load one or more of the requested types. Retrieve the LoaderExceptions
property for more information.", this fork appends the loader exceptions' own messages, so the
message reads "... Could not load file or assembly 'Some.Dependency, Version=1.0.0.0, ...'". Reading
the `LoaderExceptions` array over the wire buys nothing. It repeats one line per failed type, so a
badly broken assembly carries a long message.

**`TypeMirror.IsValueType` is the agent's encoding flag, not the CLR's answer.** It is true only for
a type the agent encodes as a plain value type: plain structs, enums, `decimal`. It is false for the
primitives (`int`, `bool`, `IntPtr`) and for every generic struct instance (`KeyValuePair<,>`,
`Nullable<>`). Whether a type IS a value type goes through `Invoker.IsValueType`, which reads the
base type. A client-built `StructMirror` of a nullable or a generic struct instance sent as an
out-argument placeholder, for an `int?` say, disconnects the VM and takes the game with it, so
`Invoker.DefaultMirrorFor` refuses those types; the same value sent by value, or as a field of a
struct the agent flags, decodes fine. The mirror reports every by-reference parameter's type by
value, `ref`, `out` and `in` alike (`IsByRef` false), and carries no flags, so the assembly metadata
says how a parameter is passed: the by-ref signature and the Param row's `Out` and `In` flags. A
client-built generic struct or nullable sent through any of them disconnects the VM too. A null
sent into an `in` nullable, or a plain struct holding a generic one, decodes there. The agent wraps
a bare value into a nullable slot for an invoke argument or a game field, but nothing wraps one
stored into a client-built struct's nullable field, so the evaluator builds that wrapper itself.

**A raw pointer argument arrives at another address.** A pointer passed as an invoke argument
(`int*`, `void*`) reaches the callee at a different address, null included, so the evaluator
refuses one. An `IntPtr`, or a pointer held in a struct's field, arrives intact, and a pointer
written to a field, an array element or a frame local is stored faithfully. A null mirror in any
pointer slot is rejected or, in an array, silently dropped, so the client sends a zero
`PointerValue` for a pointer null.

**A frame local the agent wrote reads back stale after an invoke.** A write to a local the JIT keeps
in a register lands in the agent's restore context and its cached frame; an invoke marks the frames
stale, and the next read re-walks the thread's own context, which still holds the old value. The
resumed code gets the write all the same. Parameters, which live in stack slots, and `this` stores
are unaffected. `FrameScope` remembers the locals a pause wrote and answers their reads itself, as `debug_pause_state` does; the plugin's `AGENTS.md` divergence list records what that journal misses. A
local the JIT folded from a constant ignores the write altogether once the code resumes.

**A static of a type whose static constructor has not run reads as zeros.** The agent hands back
the storage without running the constructor, so the read succeeds with a default value. Check
`TypeMirror.IsInitialized` first: it stays false while the constructor is still running, and the
client caches it only once it reads true.

## Prevention

Measure against a live debuggee before trusting reference-source behaviour: the .NET reference
implementation is evidence about .NET, not about this fork, and reasoning from it produced two
confident bug reports about code that was correct.

The Editor's MonoBleedingEdge is close enough to reproduce these, and a green integration suite is
still not the last word — the divergences above were confirmed against the reference game itself,
save the value-type, pointer, frame-local, and static paragraphs, measured on the Mono test fixture only.
