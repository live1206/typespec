# Task05 checkpoint 9: assignment and bound argument casts

Status: this bounded profile is accepted after focused and fresh broad
validation and complete independent review. Builds on
[checkpoint 8](./pre-emission-task05-checkpoint8.md).

## Exact native counterparts

```csharp
long result; result = ((long)value); return result; // → result = (long)value;
Echo(((int)value))                                // → Echo((int)value)
```

The separate assignment profile requires the exact ordinary three-statement
block, canonical `long` local and return, actual owned `int` operand, and the
same local declaration in the assignment and return. The declaration and
assignment syntax stay unchanged. This is distinct from checkpoint 8's
initializer role; an enclosing pair, different local, or unsupported form is
not authorized by this slot.

The argument profile requires one ordinary instance arrow caller with an
actual owned `int` formal and one exact identity cast argument, with the
actual unique authored private `Echo(int)`-shaped callee returning its formal.
The existing actual generated selection, scoped caller visibility/no formal
shadowing, metadata-origin boundary, original argument-list scope and observer
proof still apply. The existing cast applicability check receives only the
canonical non-error compiler `System.Int32` resolver on this exact route. No
other metadata or cast applicability rule is broadened. Call delimiters and
cast type delimiters remain; competing/optional callees and narrowing retain.

Both profiles remove just the cast's outer pair. They reuse the existing
source-only frame, one native cast and operand write, exact receipt and native
inventory, same-pass qualified form, and final compiler-input authority. No
writer/explicit-frame/default activation change. A stale state rejects the
choice; false final authority requires the caller to discard isolated output.

## Evidence and scope limits

After correcting the unused-local test's expected reused formal visit count,
accepted checkpoint 8 had **two specific missing-omission failures and five
passing controls**. Actual selected/native/qualified names and source, binding
and diagnostics, required source punctuation, compiled operations/runtime,
and source receipts were checked first. The seven foundational candidate cases
pass. The two deliberately corrupted casts compile: the target corruption
changes the operation even when the result stays equal; the operand corruption
also changes runtime.

All 23 new cases pass, with **1,365 focused passes and two unchanged Windows-only
skips** and exact preservation of all 1,344 prior outcomes. The suite covers local
alias/shadowing and restoration, argument-list restoration, optional and narrowing
refusals, real compiler source expiry and discard, and cold second dependencies
with and without a separate legacy use. Physical final output is compared with
actual policy-disabled native final output, not with globally normalized casts.

Pinned checks and official unfiltered local test-library generation passed with
no generated diffs. The full generator solution passed **8,482 tests**, with 965
skips (963 default-stubbed Spector cases and two Windows controls). All 1,367
focused outcomes match the full run. Emitter tests: **422 passed, two skipped**.
Four changed C# source checksums matched actual portable PDBs and four test-host
Core assembly copies matched. Complete independent review found no verified findings.

Raw trivia/wrappers, checked delimiters, broader casts/arguments/captures and
Task04/Task07/Task12 composition remain open. WSL is the agreed validation
domain; no CI, Windows runtime, SDK, performance or default activation claim.
