# Task05 checkpoint 12: caller-expression casts inside native lambdas

Status: this bounded profile is accepted after focused and fresh broad
validation and complete independent review. Builds on
[checkpoint 11](./pre-emission-task05-checkpoint11.md).

## Exact native roles

The supported instance arrow caller passes a zero-parameter block lambda with
one native `return ((int)value);` to one of two exact callables:

- the actually selected private static generated `Capture(Func<int> f, ...)`;
- the actually selected public constructor of the loaded source `Sample.Holder`.

Both have the actual canonical optional `[CallerArgumentExpression("f")] string
text = null` observer. The explicit text case removes only the cast's native
outer pair inside the return. The omitted text case retains it: the actual
nested source text changes when that pair is removed. Native lambda braces,
call delimiters, target spelling, cast type and operand do not change.

## Callable, capture and source authority

The exact original argument list, zero-parameter lambda and one direct return
slot, actual cached `int` formal binding, and actual selected call/applicability
are separate prerequisites. The loaded constructor must be the unique authored
source target; the generated static method must be the exact authored emitted
member in a closed one-type inventory. Only the dedicated generated group admits
that `Func<int>` formal and optional `null` default. The legacy generated call,
import and ordinary explicit frame rules are unchanged. An intended signature
or a supplied string alone is not an absence proof: the actual selected map
and source edges must establish that the cast's lambda is not captured.

The exact physical nodes, source and list currentness, immutable header,
actual native written names and receipt, qualified companion and final compiler
input result remain required. The cast and its operand each write once. A stale
choice retains same-pass native spelling. A false compiler result requires
isolated output discard even after short selection. No general binder, new
writer preparation host, source text discovery or distinct activation.

## Evidence and scope limits

On accepted checkpoint 11 the two explicit cases failed only missing-omission
assertions and the omitted method and constructor cases passed. Actual selected
callables and `Func<int>` formals, caller-expression attributes, typed compiled
constants and runtime, names/source/qualified output and one native visit each
were checked first. A separately compiled unsafe edit changes actual nested
omitted text, while explicit `manual` remains unchanged. Both generated formal
carriers were already available before capture. All four foundational cases
pass after the bounded implementation. The loaded constructor fixture records
the selected text so the compiler-observed behavior is measurable.

All 21 new cases pass: **1,426 focused passes and two unchanged Windows-only
skips**, with exact preservation of all 1,407 prior outcomes. Additional cases
cover competing callables and wrong delegate types, restored formal authority,
real selected and physical final compiler files/expiry, and distinct cold
dependencies.

A paired review regression verified an introduced preparation failure for a
cached delegate formal with no type. The new predicate now declines without
changing the inherited native writer failure; malformed output is not accepted.

Pinned checks and official unfiltered local test-library generation passed
with no generated diffs after the correction. The full generator solution
passed **8,543 tests**, with 965 skips (963 default-stubbed Spector cases and two
Windows controls). All 1,428 focused outcomes match the full run. Emitter tests:
**422 passed, two skipped**. Four changed C# source checksums matched actual
portable PDBs and four test-host Core copies matched. Complete independent
review found no verified production findings. WSL is the agreed
domain; no CI, Windows runtime, SDK, performance or activation claim. Wider
closures and observers, typed generic defaults, raw text/wrappers, checked
syntax and Task04/Task07/Task12 composition remain open.
