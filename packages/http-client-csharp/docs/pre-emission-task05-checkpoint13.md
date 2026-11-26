# Task05 checkpoint 13: native barriers and checked-binary cast grouping

Status: this bounded profile is accepted after focused and fresh broad
validation and complete independent review. Builds on
[checkpoint 12](./pre-emission-task05-checkpoint12.md).

## Which pair is removable

The native checked keyword writes `checked` and its child, without independent
operand delimiters. A direct cast supplies the required pair in
`checked ((int)value)`; that pair must stay. This is a native retention case,
not a representation of the original extra cast wrapper in `checked(((int)value))`.

The new, separate native bridge is:

```csharp
checked (((int)value) + other) // → checked ((int)value + other)
```

Here the grouped addition owns the required checked delimiters. Only the
nested cast's outer pair is removed. The profile requires the exact arrow
`checked` keyword, grouped native `+` and physical cast-left edge, actual cached
`long` formal for checked narrowing to `int`, actual cached `int` right formal,
and `int` return. Right-side casts, subtraction and unchecked forms retain.
The native writer, required addition pair, cast type and operand do not change.
Both checked conversion and arithmetic overflow remain observable.

## Source and retention boundaries

The source-only profile adds only those exact parent identities and scoped
signature/physical occurrence IDs. The direct checked cast, explicit frame
and earlier legacy grammar stay unchanged. No general checked syntax or
precedence rule, emitter change, or type or text discovery. The existing
native actual name, header, source/receipt/currentness and final compiler-input
authority still apply, with same-pass qualified fallback. A false compiler
result requires isolated output discard.

Native tests cover the exact relational sibling, generic-ambiguity and cast
argument barriers, bound indexer, optional-default cast and DateTimeOffset
receiver, plus the earlier sibling positive. These are checked at actual
prepared selection, not inferred from legacy final text. The checked and
floating arithmetic reassociation controls compare actual operations and
runtime with compilable forced edits.

## Evidence and limits

Accepted checkpoint 12 produced **one specific missing-omission failure and
eight passing controls**. Actual checked operations and compiled overflow,
selected and qualified native source, required tokens, native visits and receipt
were checked before the negative result. All nine initial candidate cases pass.
All 22 new cases pass: **1,448 focused passes and two unchanged Windows-only
skips**, with exact preservation of all 1,428 prior outcomes. Further cases
cover other positions/operators/unchecked refusals, restored operand state,
real selected and final compiler-input expiry and discard, and genuinely cold
second dependencies.

Pinned checks and official unfiltered local test-library generation passed with
no generated diffs. The full generator solution passed **8,565 tests**, with 965
skips (963 default-stubbed Spector cases and two Windows controls). All 1,450
focused outcomes match the full run. Emitter tests: **422 passed, two skipped**.
Three changed C# source checksums matched actual portable PDBs and four test-host
Core assembly copies matched. Complete independent review found no verified findings.

The original direct checked positive, exact typed `default(T?)`, arbitrary
raw wraps and delimiter trivia, and Task04/Task07/Task11/Task12 composition remain
open. No missing representation is silently replaced with a different bridge.
WSL is the agreed validation domain; no CI, Windows runtime, SDK, performance,
or default activation claim.
