# Task05 checkpoint 5: right-associated native string coalescing

Status: accepted for this bounded profile after focused and fresh broad
validation and complete independent review. Builds on
[checkpoint 4](./pre-emission-task05-checkpoint4.md).

## Exact supported edge

The original M2 `coalesceRight` counterpart is a direct native expression body:

```csharp
public string M(string x, string y, string z) => x ?? (y ?? z);
```

Only the right child’s redundant pair is removed, yielding `x ?? y ?? z`.
The return-statement counterpart and explicitly parenthesized root are tested
separately. An existing root pair stays; a left child pair stays; an already bare
right child needs no plan. Nothing is reassociated. Both original binary nodes
are written once, and their shared `AddParentheses` flags remain unchanged.

## Boundaries

This proof accepts only two exact native `??` operators with three variable
leaves in a direct return or arrow body. It requires three actual native cached
formals and a return type of canonical nonnullable framework `string`; a hint,
name, or other reference type is not binding proof. Nullable, opaque, unsupported
signature metadata and extra tree shapes retain native spelling. Source-only
parameter capture admits just these two validated node identities after the
cached-formal preflight. The old explicit frame and whole-file legacy grammar
stay unchanged; no general precedence, source-capture, binder, or type model is
introduced.

The choice reuses actual source occurrence, emitted formal binding,
native-written names and inventory, immutable header and source currentness,
qualified companion and final compiler authority. Physical input expiry before
selection retains native spelling. A false final compiler result requires the
trusted caller to discard isolated output, even if the shortened form was already
selected. There is no new default or public activation.

## Evidence and limits

On the accepted checkpoint-4 baseline the four right-edge/outer-pair tests
failed only the missing omission assertions; five controls passed. Native and
selected names, source, operations, execution over strings and nulls, visits and
receipts were validated before the expected failures. The forced reassociation
oracle detects a change that the simple string runtime matrix need not detect.

All 30 new cases pass. Focused validation has **1,275 passes and two unchanged
Windows-only skips**; all 1,247 prior IDs, names and outcomes match. Cases cover unchanged native refusals, explicit frame grammar,
owned bindings despite misleading hints, actual caller-expression capture in
an excluded lambda, cold second path/serialization dependencies with and without
a separate legacy use, and real compiler-session selected/final physical files.
The caller-expression manual control remains out of profile; this is not the
original explicit-override positive in the argument-list work.

Pinned checks and unfiltered local test-library generation passed, with no
generated diffs. The full generator solution passed **8,392 tests**, with 965
skips (963 default-stubbed Spector cases and two Windows controls). All 1,277
focused outcomes match the full run. Emitter tests: **422 passed, two skipped**.
Four changed C# source checksums matched actual portable PDBs, and four test-host
Core assembly copies matched. Complete independent review found no verified findings.
WSL is the agreed validation domain; no CI, SDK or other-platform runtime result
is claimed.

## Remaining Task05 work

Used local initializers, ordinary and explicit caller-expression override
arguments, original sibling-list ambiguity and raw source/trivia representation
remain open. This checkpoint neither completes all original M2 cases nor supplies
Task04/Task07 child-spelling contracts.
