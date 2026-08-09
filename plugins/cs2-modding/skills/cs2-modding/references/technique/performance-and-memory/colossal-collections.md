# The game's own container library

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

`Colossal.Collections` ships with the game, so it costs a mod no dependency.
It is what vanilla jobs are written against, which means a fork of one meets these types immediately.

Most of them exist so a job body can accumulate, queue or sort without touching a shared container.
The ones a fork meets first:

| Type | What it is |
| --- | --- |
| `NativeValue<T>` | A one-element `NativeArray<T>`, so a job can write a scalar. |
| `NativeAccumulator<T>` | A per-thread striped accumulator; its `ParallelWriter` is atomic-write-only and indexes by thread, so parallel accumulation needs no atomics and the reduction happens on read. `T : IAccumulable<T>`. |
| `NativeParallelQueue<T>` | A block-pooled parallel queue with its own block pool. |
| `NativeQuadTree<TItem, TBounds>` | The spatial index. See [`performance-and-memory.md`](performance-and-memory.md) for its protocol and its unconditional throws. |
| `NativeHeapAllocator` / `UnsafeHeapAllocator` | A sub-allocator handing out block ranges inside one buffer — the rendering systems' answer to churning GPU-visible buffers. |
| `UnsafeLinearAllocator` | A bump allocator owning `Allocator.Persistent` buffers, so it needs disposing. Pathfinding gives each of its worker jobs one. |
| `NativeMinHeap` / `UnsafeMinHeap` | Priority queues. The flow solver allocates two per call from `Allocator.Temp` inside the job body. |
| `StackList<T>` | A stack-allocated list for a small fixed-bound collection inside a job body. No allocation at all. |
| `NativeCurve` | Burst-compatible curve evaluation over real keyframes, which is what the prefab data uses. It delegates to the static `CurveSampling`, which is where the interpolation lives. |
| `AnimationCurve1`–`4` | A lossy fixed-step resample held inline, so their numbers do not match `NativeCurve`'s. |

**Exactly two of this library's types carry an asynchronous `Dispose(JobHandle)`** — the accumulator and the parallel queue.
Every other one that owns memory has to be completed before it is disposed: complete every outstanding reader and writer handle, then call `Dispose()`.
The stack-allocated and inline types own none and declare no `Dispose` at all, so calling one is a compile error rather than a leak.
That is the inverse of stock Unity, where the container types carry the overload, subject to the custom-allocator carve-out [`performance-and-memory.md`](performance-and-memory.md) states with the allocators — so the library you are in decides which discipline applies.

`Colossal.Collections.Generic` is a different thing and not job-facing: bidirectional dictionaries, ordered dictionaries and keyed collections, all ordinary managed types with nothing native in them.

(VOLATILE: what each named type does — each named type's own declaration; which two carry the asynchronous dispose — the `Colossal.Collections` namespace.)
