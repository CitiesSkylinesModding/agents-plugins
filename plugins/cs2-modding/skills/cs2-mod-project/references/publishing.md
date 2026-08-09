# Publishing a mod

Verified against game version 1.6.0f1.

Three modes, one per publish profile in `Properties/PublishProfiles/`, run with the IDE's publish command.

| Profile | Does | Builds | Requires |
| --- | --- | --- | --- |
| `PublishNewMod` | creates a new mod entry and uploads content and metadata | yes | `ModId` empty or `0` |
| `PublishNewVersion` | uploads a new version of an existing mod | yes | `ModId` and `ChangeLog` |
| `UpdatePublishedConfiguration` | replaces metadata, thumbnail and screenshots, and nothing else | no | `ModId` and `ChangeLog` |

Reach for `UpdatePublishedConfiguration` to fix a description, a tag or a screenshot without shipping a version nobody needed.

The other two upload the local install — the folder the build's [deploy stage](build-pipeline.md) emptied and refilled — rather than a separate publish output, so publishing ships exactly the bits that were last built and played.

## The mod id is the guard against a duplicate

A first publish prints `Mod published with Id=<number>` and writes that number nowhere.
Copy it into `PublishConfiguration.xml` yourself:

```xml
<ModId Value="1234" />
```

Write it as the `Value` attribute exactly as above.
`PublishNewMod` reads this one field from the attribute alone, so an id written as element text reads as absent — and absent is the state that publishes a second, unrelated mod entry for the same project, which players then see twice.
With a non-zero id in the attribute, `PublishNewMod` refuses, which is what makes the field a guard rather than only a record; the `0` a fresh project carries reads as absent by design.

## The metadata file

`Properties/PublishConfiguration.xml`, validated up front: a missing field is reported as an error before anything is uploaded.

Enforced for every mode: `DisplayName`, `ShortDescription`, `LongDescription`, `Thumbnail`, `ModVersion` and `GameVersion` — a missing one is named in an error and nothing uploads.
Enforced by the two modes that touch an existing mod: `ModId` and `ChangeLog`.
Repeatable and optional: `Screenshot`, `Tag`, `ExternalLink`, `Dependency` and `RequiredDLC`. `ForumLink` is optional but single — a second one silently replaces the first.
(VOLATILE: the accepted `ExternalLink` types and `RequiredDLC` names — both grow with the game, and the template's own comments carry the current sets.)

`AccessLevel` is the field to write carefully, because it is the one that fails quietly: it accepts `Public`, `Private` or `Unlisted`, case-sensitively, and anything else — a lowercase `private`, a typo, a missing element — is read as `Public` with no error.
A mod meant to stay unlisted goes public on a lowercase letter, so read it back after editing.

Almost every field is read from a `Value` attribute, which is why the template writes them that way and why the attribute is the safe default for a field this file does not name.
`Dependency` and `ExternalLink` are the exceptions: `Dependency` carries `Id` — plus an optional `DisplayName` and `Version` — and `ExternalLink` carries `Type` and `Url`. A `Value` on either is read by nothing and dropped without an error.

**The multi-line trap.**
`LongDescription` and `ChangeLog` are also read from the element's own text when the attribute is absent, and so is `ModId` — except under `PublishNewMod`, which reads it from the attribute alone.
That fallback is what splits into two failures.

Text placed inside a field that only reads its attribute is invisible, and the field is reported missing — an indented `<ShortDescription>` body yields `ShortDescription must be set in configuration`.
Text placed inside a field that does read it is taken **verbatim**, indentation included, and the platform renders a markdown subset in which leading whitespace means a code block.
So a multi-line description indented to sit tidily inside the surrounding XML publishes as one grey box:

```xml
<LongDescription>
	Line one.
	Line two.
</LongDescription>
```

Write it at column zero instead, however wrong that looks next to the rest of the file:

```xml
<LongDescription>
Line one.
Line two.
</LongDescription>
```

## Signing in

The publisher signs in automatically as the platform account the game last used, reading it from the cache `CSII_PDXCACHEPATH` points at, so credentials never live in the project.
An empty cache is not a prompt: it fails with `Could not auto log in: You were not logged in before. Launch the game and log in or pass email and password as args`, and launching the game once is the fix.
Passing an email and password instead takes a `--noAutoLogin` alongside them — without it the automatic path runs first and the credentials are never read — and the arguments the build hands the publisher carry none of the three.
