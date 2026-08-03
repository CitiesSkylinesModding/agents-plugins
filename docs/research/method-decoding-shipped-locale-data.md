# Method: decoding the game's shipped locale data

**Baseline.** Decompiled game 1.6.0f1; the recipe was run against an installed game at `1.6.0f1 (419.d6c6) [6216.19404]`, the same build, on 2026-08-03.

**This is a method file, not a topic file.** It carries no topic from the approved reference structure and no authoring agent is pointed at it. It exists because `localization.md`'s namespace table rests on this decode, and a table whose derivation lives only in a gitignored scratch folder is a table the next pass has to rediscover. The maintainer ruled the recipe out of the shipped reference and onto the roadmap as a script (ticket 15, 2026-08-03; `docs/ROADMAP.md`, "Extracting the shipped localization dictionaries").

## Why this source outranks the others

The game's own compiled locale data is first-party and version-known, which no other source of a vanilla key list is.
The decompiled C# names only the namespaces that happen to appear as string literals — 21 of the 75 that exist — because 72% of the game's UI strings are reached from the frontend and never from C#.
The wiki's table is hand-maintained, names 46 and collapses or omits the rest.
The only complete list otherwise reachable is a copy of the compiled UI bundle that a corpus mod author vendored into their repository, carrying no version stamp.

The decode replaces all three for anything it can answer, and it answers from the reader's own install rather than from anything this project ships.

## Where the data lives

Twelve locales are packed into `Cities2_Data/Content/Game/Locale.cok`: `de-DE`, `en-US`, `es-ES`, `fr-FR`, `it-IT`, `ja-JP`, `ko-KR`, `pl-PL`, `pt-BR`, `ru-RU`, `zh-HANS`, `zh-HANT`.
The thirteenth, `uk-UA`, sits loose as `Cities2_Data/StreamingAssets/uk-UA.loc` and is the only `.loc` outside the package.
The sixteen DLC and radio-pack directories under `Cities2_Data/Content/` ship none, so a content pack's strings live in the base locale files rather than in a locale file of its own.

`en-US` is the hard-coded fallback locale (`src/Game/Game.SceneFlow/GameManager.cs:2356-2361`), so its key set is the one that defines what exists, and any count meant to describe the game is an `en-US` count.

## Step 1 — the package is a plain zip

A `.cok` is the asset-database package extension (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/PackageAsset.cs:7-9`).
`ZipPackageWriter` adds each asset with `CompressionMethod.Stored` and a sibling `<name>.cid` entry holding the asset's GUID as UTF-8 text (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/ZipPackageWriter.cs:51-74`).
So any zip reader extracts the payload and there is no decompression step.

## Step 2 — the payload is a flat `BinaryWriter` stream

`LocaleAsset.Load` (`src/Colossal.IO.AssetDatabase/Colossal.IO.AssetDatabase/LocaleAsset.cs:109-134`) and `LocalizationCompiler.WriteLocale` (`src/Colossal.Localization/Colossal.Localization/LocalizationCompiler.cs:206-227`) are mirror images of each other, and the reader is the whole specification:

```
ushort formatVersion               // LocaleAsset.kFormatVersion == 1 (LocaleAsset.cs:11-13)
string systemLanguage              // parsed into UnityEngine.SystemLanguage, ex. "English"
string localeId                    // "en-US"
string localizedName               // "English" - the literal the language dropdown shows
int    entryCount
entryCount      x (string key, string value)
int    indexCountCount
indexCountCount x (string key, int count)
```

Every `string` is `BinaryWriter`'s own encoding — a 7-bit-encoded byte length followed by UTF-8 — so `BinaryReader.ReadString` decodes one and no hand-rolled string reader is needed.
There is no compression, no checksum, no table of contents and no offset table, which is why `ReadHeader` can take the locale id and display name by reading the first four fields and stopping (`LocaleAsset.cs:83-92`).

A `BinaryReader` over the extracted bytes, reading those fields in order, is the entire decoder; the working implementation was about forty lines of PowerShell.

## Step 3 — two traps

**Trailing bytes on two files.** `zh-HANS.loc` carries 2,506 bytes past the end of its structure and `zh-HANT.loc` 8,183; the other eleven carry none.
`LocalizationCompiler.WriteLocale` opens its output `FileMode.OpenOrCreate, FileAccess.Write` and never truncates (`LocalizationCompiler.cs:208`), so a build emitting a shorter file over a longer previous one leaves the old tail in place.
Nothing reads it — both counts in the format are explicit, so `LocaleAsset.Load` stops at the declared end — but a parser treating end-of-file as end-of-data trips on those two, and any tool writing a `.loc` through the compiler inherits the same defect.

**The installed version is not in the files you would reach for.** `Cities2_Data/app.info` holds only the publisher and product name, and `Cities2_Data/boot.config` only a Unity build GUID and graphics flags.
The game itself answers: `GameManager` logs `Version.current.fullVersion` at startup and writes the same string into a `version` file in its persistent data folder (`src/Game/Game.SceneFlow/GameManager.cs:2295`, `:2336`, the string composed at `src/Colossal.Core/Colossal/Version.cs:58-62`).
Reading that is what lets a decode claim a version at all, and a decode that cannot state its version is worth little more than the unversioned bundle it replaces.

## What the decode is good for

Group and id counts, the exact group set, the identifier-shape distribution, and the indexed-variant table — none of which any other source carries at a known version.
Running the four identifier regexes from `LocalizationValidation.cs:22-25` over the decoded keys classifies every one of the 22,120 `en-US` entries with nothing left over, which is how the one-dot key grammar was confirmed against 22,120 worked examples rather than against the compiler's intent.

What it does **not** carry is the per-id `Single`/`Hashed`/`Indexed`/`HashedIndexed` typing and the argument names.
Those exist only in the generated TypeScript dictionary, so they stay bundle-sourced and have to be re-derived from the key text.

## The copyright boundary

Key identifiers and group names are mechanism and may be recorded and shipped.
The translated strings are the publisher's copyrighted text: they stay out of every tracked file, and a decode keeps them only in gitignored working material.
Nothing in this project distributes any part of the game, and this recipe reads a game the user already owns.

Rots: the locale set, the per-locale entry counts and the trailing-byte observation — re-derive by decoding `Cities2_Data/Content/Game/Locale.cok` and `Cities2_Data/StreamingAssets/` in a current install. The format version itself is `LocaleAsset.kFormatVersion`, and a bump there is what would break the decoder rather than move a number.
