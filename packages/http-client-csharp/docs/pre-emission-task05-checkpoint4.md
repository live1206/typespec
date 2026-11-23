# Task05 checkpoint 4: native `if`-condition equality

Status: accepted for this bounded profile after focused and fresh broader
validation and complete stable independent review. It builds on the accepted
[third checkpoint](./pre-emission-task05-checkpoint3.md), not a full Task05
implementation or completed Task04 composition.

## Bounded grammar and original anchor

Prepare omission of the redundant binary-expression pair in the original M2
`StructuralParenthesesNonReassociatingCasesKeepOperations(condition)` counterpart:

```csharp
// Native writer
public bool M(int x, int y) { if ((x == y)) return true; return false; }
// Prepared selected form
public bool M(int x, int y) { if (x == y) return true; return false; }
```

The native `if` writer's **required condition delimiters remain**. Inline and
braced/body-layout variants do not change that requirement. The shared binary
`AddParentheses` flag is never mutated.

Admission requires an exact native method with a canonical `bool` result and two
actual emitted `int` formals, under the established signature/header constraints.
Unsupported base contracts, parameter modifiers/default metadata, ref/out/in/
params, defaults, attributes and unavailable native formal bindings refuse.
Matching variable names or advertised expression types are not binding proof.

The exact native body is a known two-statement `MethodBodyStatements` collection:
an `IfStatement` containing one literal-`true` return, followed by a literal-
`false` return. Its condition is an exact grouped built-in integer equality of
owned variable references. Other bodies, containers, literals, operators and
roles are not granted permission by this profile. Unknown shapes retain.

## Preparation, writing and freshness

The existing grouping-admission layer recognizes the new role. The structured
snapshots and parameter frame already support these native statement/literal
shapes, so their capture and callback schedules are unchanged. The native `if`
and binary writers are unchanged; the condition is written once through the
existing occurrence hook and qualified companion buffer.

Preparation records only the four additional physical context occurrences in the
closed body: body container, `if`, and the two Boolean literals. Those producer
kinds are not globally whitelisted. Old whole-file admission with an empty new-
context set still refuses this body, preserving legacy compatibility and check
schedules. Existing binary and direct-cast positives remain unchanged.

The new context reuses actual native-written file inventory, current relevant
AST/formal/type/source/receipt facts and compiler-input/final-result authority.
Unrelated live path/serialization getters are not replayed for new permission.
Relevant mutation, restored-but-expired snapshots, missing cached facts, unsafe
actual names or source-input expiry still refuse. Late selection uses the same-
pass native companion without another traversal or rewriting the expression.

The shared compiler-input check verifies generator identity immediately after
its existing truthful callback, including during legacy validation. A provider
callback can also change the generator before that check begins. The native
writer therefore compares the captured source configuration with the immutable
configuration it actually uses for the header. A mismatch vetoes native-written
grouping without vetoing separate legacy choices. Neither check adds a host call,
replays a header or path getter, changes the emitted header, or repairs broader
hidden-source policies.

## Validation status

Before production changes on the accepted checkpoint-3 base, the two equality
counterparts failed only missing plan/use/raw shortening assertions; four native
retention controls passed. Native and selected names, compilation, operations,
compiled IL execution, runtime results, receipts, single binary visits, unchanged
flags and required `if` delimiters were valid before the intended failures.

All 91 new cases and every prior 1,156 focused outcome pass: **1,245 passes and
the same two Windows-only controls not executed**. Cases include:

- Actual formal ownership versus hints/names, unsupported signature metadata,
  nullable/ref/long/extra formals and unknown or changed body/collection shapes.
- Required delimiters, inline/braced layouts, source/condition/body/layout/formal
  mutation and restore, host windows, single visits and qualified fallback.
  Body/layout mutation is compared to the native spelling actually written.
- Forced delimiter/operator corruption oracles; actual captured lambda constants
  and runtime text, with an explicit manual-text comparison.
- Independent file-local versus shared expiry and genuinely cold second
  path/serialization dependencies with and without a separate legacy positive.
- Real compiler-session source/Corelib hashes, actual selected filenames and
  contents, final physical output, and late physical output links. Input expiry
  before selection forces qualified fallback and a false result; expiry after
  short selection still returns false and requires the trusted caller to discard
  isolated output.

Complete review identified two host-callback generator-swap windows, each with
three selection-stage retention failures and a passing stable control. A separate
provider-path case reproduced a swap before the trailing host check. The accepted
checkpoint-3 baseline passed all nine paired cases without an `if` plan. All nine
pass after correction, with all 1,246 prior focused outcomes unchanged. For the
provider-path case, the separate legacy direct-equality choice remains eligible.
The guard preserves the native inner pair at prepared selection. The existing
downstream stage may still remove it, as it did on both the accepted baseline
and the corrected candidate, including in the hidden-source case. Actual file
authority, source visibility, operations and runtime were checked; no incorrect
Boolean runtime result was demonstrated.

Fresh pinned checks and unfiltered local test-library generation passed with no
generated diffs. The full generator solution passed **8,362 tests** (963
default-stubbed Spector skips and two Windows-only skips). Emitter tests passed
**422 with 2 skipped**. All 1,247 focused outcomes match the full run. Eight changed
C# sources matched actual portable PDB checksums, and four test-host Core assembly
copies matched. Complete stable independent review found no remaining findings.
Current WSL is the agreed validation domain; other-platform runtime verification
is not an acceptance requirement. No CI, SDK, performance improvement or default/public rollout is
claimed.

## Remaining Task05 work

Local initializers, ordinary arguments and shared sibling-list grammar facts,
right-associated coalescing, additional cast roles, source/trivia contracts and
Task04/Task07 child-spelling composition remain open. Explicit caller-expression
override positives are not completed by retaining an otherwise unsupported call.
Conditional preprocessing/configuration correspondence belongs to Task12; C#
conditional-expression parent roles are not silently excluded from Task05.

This checkpoint does not replace the original accepted inventory with invented
bridges, complete arbitrary nested raw wrappers, or introduce a general binder,
precedence/type-origin framework, expression model, preparation host/lifecycle,
emitted-text discovery, shared flag mutation or broad hidden-source repair.
