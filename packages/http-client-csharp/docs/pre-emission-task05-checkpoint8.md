# Task05 checkpoint 8: primitive cast parent roles

Status: this bounded profile is accepted after focused and fresh broad
validation and complete independent review. Builds on
[checkpoint 7](./pre-emission-task05-checkpoint7.md).

## Exact native counterparts

- In an exact used-local declaration, `long result = ((long)value)` becomes
  `long result = (long)value`; the second statement returns that same local.
- As the left operand of direct addition, `((long)value) + other` becomes
  `(long)value + other`. Both operands are bound `int` formals, the result is
  `long`, and any existing pair owned by the addition remains.
- As the condition of an ordinary C# conditional expression,
  `((bool)value) ? 1 : 2` becomes `(bool)value ? 1 : 2`, with the `bool` formal,
  literal arms and `int` result unchanged.

Only the cast's outer pair is eligible. The cast's type and operand are written
once through existing native code. The right addition operand, unsupported
conversions, unary parent and invocation receiver keep native spelling. The
previous direct-`int` case remains independent. The native typed local is a
counterpart, not a representation of original `var` or additional raw wrappers.

## Source, binding and selection

The source-only frame admits the exact additional parent nodes after cached
formal preflight. Other explicit and legacy frame grammars are unchanged.
The new finite profile checks canonical primitive types, each actual owned
formal binding, the physical parent and cast operand edges, and local declaration
identity, name and return in the block case. The conditional roles here are
ordinary `?:`, not Task12 preprocessor variants. Additional context/signature
admission is tied to these exact native occurrences, not a general cast
precedence rule, binder, text rewrite, or broader source observer policy.

The existing native-written names, header/currentness, receipt and compiler
input proof still authorize selection. A stale local revision or source fact
before selection retains the same-pass qualified form. False final compiler
authority requires the trusted caller to discard isolated output even when a
prepared form was selected. No writer, host, or activation change is needed.

## Evidence and limits

Accepted checkpoint 7 produced three specific missing-omission failures and
six passing controls before implementation. Actual native/selected/qualified
names and source, strict operations, overflow runtime, one cast/operand visit
and current receipts were checked first. The three deliberately wrong
association, target and operand edits compile but change the operations and
runtime results. All nine initial candidate controls pass.

All 24 new cases pass: **1,342 focused passes and two unchanged Windows-only
skips**, with all 1,320 prior outcomes preserved exactly. Tests cover local
identity and revision restoration, independence of a binary root pair,
unsupported operand/slot retention, real compiler source expiry before and
after selection, and genuinely cold second path/serialization dependencies with
and without a separate legacy use. Final physical compiler output is compared
with actual disabled native final output at the corresponding stage; legacy
rewriting is not part of the prepared-choice proof.

Pinned checks and unfiltered local test-library generation passed with no
generated diffs. The full generator solution passed **8,459 tests**, with 965
skips (963 default-stubbed Spector cases and two Windows controls). All 1,344
focused outcomes match the full run. Emitter tests: **422 passed, two skipped**.
Four changed C# source checksums matched actual portable PDBs and four test-host
Core assembly copies matched. Complete independent review found no verified findings.

Raw trivia and nested wrappers, checked delimiters, further assignment, argument,
reference/generic/default cast roles, and Task04/Task07/Task12 composition remain
open. WSL is the agreed validation domain; no CI, Windows runtime, SDK,
performance or default activation claim.
