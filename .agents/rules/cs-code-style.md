---
paths:
  - "**/*.cs"
version: 1.1.0
---

# C# Code Style

- Projects build with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- Strict null-check is enabled (`<Nullable>true</Nullable>`).
- Use the very latest C# 14 language features.
- Use the init and required modifiers when appropriate rather than ctor-based params.
- Use the compiler-synthesized `field` for backing fields rather than manually declaring them.
- Declare extension methods/properties in C# 14 `extension(Receiver) { ... }` blocks grouped by receiver type, rather than classic `this`-parameter extension methods.
- Choose appropriate data structure types: is it a class, a struct, a readonly struct, a record class, a record struct, a readonly record struct, a ref struct, a readonly ref struct...?
- Name exception variables `ex` by default (catch clauses, `Assert.Throws` results, etc.), not `e`.
