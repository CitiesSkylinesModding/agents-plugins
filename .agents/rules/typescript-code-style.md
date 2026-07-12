---
paths:
  - "**/*.{js,jsx,ts,tsx}"
version: 1.0.0
---

# TypeScript Code Style

## TypeScript Strictness

- You are working with TypeScript's strictest settings.
- Never ever use `any`. Create types if necessary, derive from existing types, etc.
- Use `unknown` when the value is genuinely unknown.
- Use TypeScript built-in types when applicable.
- In interfaced, use property-style over method-style method signatures; property-style function declarations allow for stricter type checking when `strictFunctionTypes` is enabled. Use method shorthand in objects.
- Use the `type-fest` npm package to gain access to more advanced utility types.

## Style

- Use named function hoisting to place the more important functions at the top. The deeper a function is in the call stack, the deeper it is in a file.
- Also use hoisting for functions inside functions, putting helper functions at the very bottom.
- Use template literals for strings containing English sentences. This applies even when there are no interpolations. This makes it easier to use single and double quotes inside the sentence.

## Nullability

- Prefer `undefined` over `null` in general.
- Restrict `null` to serialization and interoperability boundaries.
- Use optional chaining (`?.`) very sparsely, when you are sure the value can be null/undefined.

## Readonly Data

- Prefer using immutable data structures whenever possible. When fields of a structure are mutable,
  add comments about it and explain why.
- Mark class and object properties as `readonly` whenever possible.
- Use `Readonly<T>` when all properties of a type are readonly.

## Type Safety and Guards

- NEVER use `===` unless strict equality (with null or undefined, for example) is specifically required. Instead, use `==`.
- AVOID the `!` non-null assertion. Prefer a runtime assertion that both narrows the type and fails loudly if the invariant is violated. You may use `!` in hot paths to avoid a function call, but you will need to silence the linter.
- When asserting that a variable is non-null, and it should be non-null if the program is sound, assert it explicitly rather than reaching for `!`. Prefer an assertion the project already provides; otherwise use a plain runtime check that throws when the invariant is violated. See "Assertions and Errors" below for how this differs between server/CLI and client code.
- Handle type narrowing (non-null, boolean, string, number, enum membership) the same way: assert the invariant with a helper the project provides, or with a plain runtime check that throws when it fails.
- For a code path that should be unreachable if the program is sound, assert it: ideally including the offending value (ex. in a switch statement's `default` case). A common pattern is to type the value as `never` and throw.
- For other cases not covered here, see "Assertions and Errors" below.

## Assertions and Errors

For server/CLI code only and for cases not already covered in "Type Safety and Guards":

- Use assertion-based error handling whenever possible.
- Use `assert()` for type guards when possible, ex. `assert(typeof value === 'string')`.
- Use `assert()` instead of throwing an exception for things that should not happen if the program
  is sound.
- Do NOT use assertions for operational errors.
- Use this import: `import assert from 'node:assert/strict'`.

For client code, you will throw standard errors.
