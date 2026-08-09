# Which type the game scans a mod assembly for

## Prompt

When Cities: Skylines II loads a mod assembly, which type does it scan that assembly for? Give the type's fully qualified name.

## Verified answer

The game scans the assembly for a type implementing the `Game.Modding.IMod` interface.

## Rubric

- 6: Names the `IMod` interface.
- 4: Gives the `Game.Modding` namespace, so the fully qualified name is `Game.Modding.IMod`.

## Roots

- decompile
