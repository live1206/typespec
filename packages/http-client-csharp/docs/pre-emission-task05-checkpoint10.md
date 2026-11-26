# Task05 checkpoint 10: bounded metadata cast returns

Status: this bounded profile is accepted after focused and fresh broad
validation and complete independent review. Builds on
[checkpoint 9](./pre-emission-task05-checkpoint9.md).

## Exact supported native forms

The new direct arrow-return profile removes only the native outer pair around:

- `(global::System.Exception)null`;
- `(global::System.DateTimeOffset)default`;
- `(global::System.Collections.Generic.IList<string>)value` from an owned `object` formal;
- `(global::System.Exception[])value` from an owned `object` formal.

All return `object`. Their native type spelling, cast type delimiters, operand
and one native write remain unchanged. The proof does not authorize a receiver,
unboxing, an alternative array or generic element, or a broader conversion from
other operands. This is not Task04 type shortening.

## Actual type, source and output authority

The finite certificate requires exact known framework runtime shape and actual
compiler input symbols. The global path and target must be unambiguous, belong
to the canonical core library with a global reference, and not be shadowed by
an emitted owner. `IList<>` plus exact `string` is separate from the exact SZ
`Exception[]` target. An `object` operand needs an actual current native formal
binding. The compiler classifications establish the known explicit reference
and implicit return conversions, including boxing for the default value type;
null and default operands must be the exact native keyword nodes.

The current profile is limited to one written owner. Actual source shapes,
written names, header/inventory/receipt, qualified same-pass fallback and
compiler-input result authority remain required. Restored changes do not revive
permission; a false final result requires isolated output discard. No general
type-origin registry, binder, new frame/host, writer edit, or text discovery.

## Evidence and remaining scope

Before implementation the accepted checkpoint-9 baseline produced **four
specific missing-omission failures and three passing controls**. Actual bound
symbols and operations, boxing and native/selected/qualified names and contents,
normal runtime and invalid-cast behavior, native visits and receipts were
checked before the expected failures. All seven foundational cases pass after
implementation. All 22 new cases pass: **1,387 focused passes and two unchanged
Windows-only skips**, with exact preservation of all 1,367 prior outcomes. Guards
check actual source and generated-owner competitors, unsupported type shapes,
target identity restoration, real selected/final compiler input expiry, and
distinct cold dependencies. The final compiler oracle compares actual native
final output at the same stage.

Pinned checks and unfiltered local test-library generation passed with no
generated diffs. The full generator solution passed **8,504 tests**, with 965
skips (963 default-stubbed Spector cases and two Windows controls). All 1,389
focused outcomes match the full run. Emitter tests: **422 passed, two skipped**.
Three changed C# source checksums matched actual portable PDBs and four test-host
Core assembly copies matched. Complete independent review found no verified findings.

Raw delimiters/trivia, checked wrappers, other cast roles, and Task04/Task07/Task12
composition remain open. WSL is the agreed validation domain; no CI, Windows,
SDK, performance or default activation claim.
