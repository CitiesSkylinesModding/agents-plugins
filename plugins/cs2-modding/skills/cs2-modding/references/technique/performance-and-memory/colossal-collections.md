# The game's own container library

Verified against game version 1.6.0f1.

`Colossal.Collections` ships with the game, so it costs a mod no dependency.
It is what vanilla jobs are written against, which means a fork of one meets these types immediately.

Most of them exist so a job body can accumulate, queue or sort without touching a shared container.

| Type                                          | What it is                                                                                                                                                                                             | `Dispose(JobHandle)` |
| --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | -------------------- |
| `NativeValue<T>`                              | A one-element `NativeArray<T>`, so a job can write a scalar.                                                                                                                                           | **no**               |
| `NativeAccumulator<T>`                        | A per-thread striped accumulator; its `ParallelWriter` is atomic-write-only and indexes by thread, so parallel accumulation needs no atomics and the reduction happens on read. `T : IAccumulable<T>`. | yes                  |
| `NativeParallelQueue<T>`                      | A block-pooled parallel queue with its own block pool.                                                                                                                                                 | yes                  |
| `NativeQuadTree<TItem, TBounds>`              | The spatial index. See [`performance-and-memory.md`](performance-and-memory.md) for its protocol and its unconditional throws.                                                                         | **no**               |
| `NativeHeapAllocator` / `UnsafeHeapAllocator` | A sub-allocator handing out block ranges inside one buffer — the rendering systems' answer to churning GPU-visible buffers.                                                                            | **no**               |
| `UnsafeLinearAllocator`                       | A bump allocator owning `Allocator.Persistent` buffers, so it needs disposing. Pathfinding gives each of its worker jobs one.                                                                          | **no**               |
| `NativeMinHeap` / `UnsafeMinHeap`             | Priority queues. The flow solver allocates two per call from `Allocator.Temp` inside the job body.                                                                                                     | **no**               |
| `StackList<T>`                                | A stack-allocated list for a small fixed-bound collection inside a job body. No allocation at all.                                                                                                     | n/a                  |
| `NativeCurve`                                 | Burst-compatible curve evaluation over real keyframes, which is what the prefab data uses. It delegates to the static `CurveSampling`, which is where the interpolation lives.                         | **no**               |
| `AnimationCurve1`–`4`                         | A lossy fixed-step resample held inline, so their numbers do not match `NativeCurve`'s.                                                                                                                | n/a                  |

**Every type marked no in that column has to be completed before it is disposed**: complete every outstanding reader and writer handle, then call `Dispose()`.
Every stock Unity container — arrays, lists, hash maps and sets, parallel hash maps and sets, queues, streams, references, bit arrays, text, key-value arrays — has the overload, subject to the custom-allocator carve-out [`performance-and-memory.md`](performance-and-memory.md) states with the allocators.

`Colossal.Collections.Generic` is a different thing and not job-facing: bidirectional dictionaries, ordered dictionaries and keyed collections, all ordinary managed types with nothing native in them.

(VOLATILE: the type list and which members carry an asynchronous dispose — the `Colossal.Collections` namespace.)
