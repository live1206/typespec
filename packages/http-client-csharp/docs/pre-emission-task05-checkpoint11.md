# Task05 checkpoint 11: direct qualified source Number cast

Status: this bounded profile is accepted after focused and fresh broad
validation and complete independent review. Builds on
[checkpoint 10](./pre-emission-task05-checkpoint10.md).

## Exact native counterpart

The direct `object` return loses only the native outer pair in
`((global::Fixtures.Number)value)`. The arrow and exact direct return statement
are separate native slots. The fully qualified target, cast type delimiters,
operand and one native write stay unchanged. The receiver shape retains its
pair. The former direct primitive profile stays independent.

This proof does not shorten the type or change the operator or any enclosing
scope. It does not complete the Task04-dependent `Number` unary-operand case,
which also requires independently chosen type spelling and representation of the
original operand delimiters.

## Source and conversion authority

The profile uses the actual unique public source struct in the captured compiler
input and checks the global namespace and generated owner boundary. It requires
a canonical owned `int` formal in the exact cast child and an actual selected
public static `op_Explicit(int)` owned by that struct, returning that same type.
The actual source value-to-`object` conversion must be boxing. Unsupported
checked contexts/operators, implicit conversions and unsupported contracts
retain. The operator need not be pure; its semantics and number of executions
are preserved.

The actual physical return/operand occurrence, native-written names and source
receipt, type/method revisions and captured input currentness remain required.
One written owner keeps the profile closed. Native output and same-pass
qualified fallback are not reconstructed after emission. False final compiler
authority requires discard even after short selection. This is not a general
source type or conversion binder, new preparation host, or writer change.

## Evidence and limits

Accepted checkpoint 10 had **two specific missing-omission failures and two
passing controls**. Actual bound source operator/type and boxing, native spelling
and qualified output, one native cast and operand visit, compiled boundary
runtime, boxed return type and operator invocation count were all checked before
the intended failures. All four initial candidate cases pass.

All 18 new cases pass: **1,405 focused passes and two unchanged Windows-only
skips**, with all 1,389 prior outcomes preserved exactly. Guards cover unsupported
implicit and checked conversions, restored target identity, cold second
dependencies and real compiler input expiry before and after selection. The
compiler cases use physical `Number.cs` and compare final output with actual
native final output at the same stage.

A paired review regression verified that a missing cast target used to reach
the native writer's `ArgumentNullException`. The new profile initially threw
earlier during preparation; it now declines without altering the inherited
writer failure. This does not accept malformed output or change valid cases.

Pinned checks and unfiltered local test-library generation passed with no
generated diffs after the correction. The full generator solution passed
**8,522 tests**, with 965 skips (963 default-stubbed Spector cases and two Windows
controls). All 1,407 focused outcomes match the full run. Emitter tests:
**422 passed, two skipped**. Three changed C# source checksums matched actual
portable PDBs and four test-host Core assembly copies matched. Complete
independent review found no outstanding findings.
WSL is the agreed validation domain; no CI, other-platform, SDK, performance
or default activation claim. Explicit caller-expression cast roles, checked
wrappers, raw trivia and Task04/Task07/Task12 composition remain open.
