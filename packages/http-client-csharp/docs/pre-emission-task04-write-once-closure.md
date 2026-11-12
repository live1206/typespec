# Task04 write-once: bounded closure findings and decision

**Status: design investigation complete within its budget; not implementation approval.**
Read with the [write-once design](./pre-emission-task04-write-once-design.md). No production
source, existing tests or regression expectations were changed. No builds, experiments,
regeneration, commits or pushes were run.

## 1. Scope and evidence

The pass inspected 20 distinct named implementation files: six emission/auxiliary files,
eight mutation/output-frame files and six resource files. Both delegates completed within
their limits; the resource pass was performed by the lead. Detailed line references,
source boundaries and alternatives are in `.pi/issue-12454/design-closure-20261112/`:
`emission-contract.md`, `mutation-contract.md`, `resource-contract.md`.

Source identity: HEAD `2afe54d5de261897d49a182e5eda2c6d3411ba95` plus the unchanged existing
tracked diff, SHA-256 `ca87a66568dae6dbd6e92ef6eadf1167d856127a1a0f96ce156b1a33120afe43`.
Prior green checkpoints do not validate this dirty implementation or the proposed changes.

## 2. First correction: do not expand the admission problem

The authoritative Task04 contract separates **consumer-input/admission assessment and
broader activation** from the rule rewrite. The implementation guide preserves existing
admission and forbids substituting a different fixture/source authority.

Therefore the first milestone is the unchanged original Foo prepared/raw/actual-pipeline
anchor **within its existing admitted inputs**, not proof of unrestricted normal-CLI,
plugin, scaffolding or filesystem behavior. The previous draft overstated normal-entry
auxiliary support as an unconditional prerequisite. This is corrected, not a decision to
exclude any original accepted anchor or to require a new owned-Csc host.

Map the actual original anchor to its existing host/entry mode before claiming pipeline
acceptance. If that entry skips additional files, auxiliary-effect closure for that run
is straightforward. If it invokes them, the exact effect contract below is still required.
Do not replace an accepted normal-path fixture with a hosted one simply to avoid the issue.

## 3. Concrete local designs selected by this pass

These are implementable design choices, not claims that current code implements them.

| Area | Source-grounded finding | Selected contract / algorithm |
| --- | --- | --- |
| Actual type use | Expected CSharpType/symbol/revision is not proof of what the formatter emitted. | Admit the initial simple, non-generic/non-nested/non-array type slot. Observe actual native formatter values/branches and exact payload range; match the prepared descriptor. Never reread a getter or format twice. |
| Native output frame | CodeFile can retain immutable strings/metadata with internal changes; its current mixed fast path is insufficient. | Keep an untouched legacy field/body NativeFrame and separate POC uses. Validate POC metadata before any fast path. POC denial returns legacy BaseResult, not all-qualified mixed output. |
| Optional rendering | Lengths are available after native emission; eager POC buffers needlessly complicate exhaustion. | Prepay fixed metadata; reserve validation and complete render costs after actual lengths are known. At most one tentative and one different final POC view. If underfunded, return retained native/legacy result without restoration copying. |
| Shared exact lists | SameSharedReferencesForPoc reserves then invokes an unbounded legacy copy. | Use the existing exact-list fixed-bound primitive, with observation-local Match/Mismatch/Unavailable. No construction/legacy fallback after mutation. Preserve exact/short/mismatch/retry controls. |
| Recursive currentness | SourceCurrentnessChecks.Type prepays then invokes shared IsCurrent. | Pass the POC observation context through every recursive operation. Bound the actual collection read/copy and propagate its result. Do not alter native/shared entry semantics. The private snapshot call graph remains an implementation audit item. |
| Versioned metadata | Some TypeProvider/ParameterProvider revisions advance before assignment. | Use actual consumed values, or a genuine completed begin/write/end, overlap/saturation-aware witness. Plain version equality does not prove a completed mutation; raw aliases do not enter these setters. |
| Participating seal | An atomic flag does not cover live operations or delayed failure notification. | Couple admission/active ownership/terminal rejection, attempt seal once, deny on active work/contention/saturation, publish failure before release. No waiting/retry loop or observer resurrection. Raw aliases still need actual-use evidence or an explicit validity contract. |

The participating seal is a protocol requirement, not proof of arbitrary concurrent
mutation. Reuse the admitted complete physical-input/source-proof contract, not merely
SourceInputModel identity. Its immutable compilation does not alone freeze replaceable
input pointers, physical files or configuration. If Task04 needs a stronger validity
interval than the existing contract, identify that exact difference and request it.

## 4. Removal policy: prefer actual output agreement, not a new ownership system

Current code has an expected selected provider set and native stream observations.
Use those facts directly:

1. Prepare namespace/scope facts against the explicit expected provider/export set before
   writing. Keep the existing native write-policy calls and their timing/counts.
2. During the original native pass, record the actual provider stream and actual exported
   names/namespaces/arities/global-import effects under finite lexical/export contracts.
3. At seal require complete agreement. A policy mutation that changes written competitors
   rejects POC. Stream identity **alone** remains insufficient.
4. Do not additionally enumerate the mutable removal HashSet merely to establish equality
   with a final live model when that equality does not determine the captured artifact.

This selects **conditional artifact authority**, not a cached live-set freshness claim.
The prefiltered expected subset can supply the generated-provider side of the first
conservative proof, provided complete actual stream/export agreement and all independent
source/reference, scope, import, alias and currentness obligations hold. It does not
establish final live-set equality or waive sticky invalidation/original retentions. No
new optional-provider/binder framework is required merely for that conservative guard.
Cheap bounded activity/count observations may retain denials, but cannot be advertised
as alias/ABA witnesses or grant permission on their own.

Preserve the original removed-competitor positive, final-proof mutation, root/stream,
currentness and shared controls unchanged. Before removing/replacing any check, compare
base/candidate against those anchors. If a verified control requires final-live-set
invalidation even when the certified artifact is unchanged, stop with that specific
semantic difference; do not weaken the test or declare the check redundant.

An analyzer-owned immutable publication/mutation protocol is the alternative only if
that requirement invalidates the simpler design. An empty-only veto loses required
positives. A private-runtime HashSet-layout walker is not selected.

## 5. Emission closure: exact missing component, not universal purity

The outer TypeProviderWriter skeleton is mapped. Its attributes, constraints, methods,
properties, wrappers and raw/directive leaves still need compositional contracts. The
first profile must specify:

- exact emitter/template and admitted hole/child types;
- lexical state and balanced boundary at entry/exit;
- declarations, imports, aliases and directive effects it can export;
- actual consumed values and mutation/epoch evidence;
- precomputed lookup/source-policy guard and bounded observation costs.

Noncandidate files require export/escape closure, not every expression's semantic proof.
The 39-provider stream, 11 raw interpolations, four directives and eight Foo wrappers are
an inventory, not this proof. Existing safe literal/name facts can be reused within their
boundaries. Unknown composition denies POC; it does not authorize a general language parser.
The exact actual Foo child/template manifest was outside the six-file outer-writer pass
and is not marked closed. Implementing a named certificate without that table would be
another partial implementation, not progress toward authority.

For entries that invoke auxiliary work, there is a real ordering barrier: provider output
is selected/written before WriteAdditionalFiles/scaffolding. Schema generation traverses
live settings/models and ToString paths; scaffolding changes project inputs. Exact outer
method identity or JSON filenames are insufficient. Either prove the actual receiver,
inputs and nested effects remain within a finite no-relevant-effect contract through
completion, approve a narrowly stated trust/lifecycle change, or retain native output
for that entry. A late check cannot retroactively retract published files.

## 6. Resource accounting — Alternative B approved

The user approved B on 2026-11-12, requiring well-documented bounded overhead.
The [bounded-overhead contract](./pre-emission-task04-bounded-overhead.md) is normative:

`W_added <= sum(c_i * N_i) + W_paid`

Each qualifying native/external entry site needs a fixed, transitive, audited ingress
bound. No loop, allocation, reflection, metadata realization, hash/history lookup,
monitor or retry may hide in that constant. All variable work and retained storage,
including cleanup and rendering, remain funded under the existing caps. Entry argument
preparation and dispatch are included; POC-invented loops/recursion do not create entries.
Existing regressions and narrow runtime exceptions remain unchanged.

This is a bounded-overhead guarantee, not an absolute fixed total-work or wall-clock
promise. The previously considered strict-total model was not shown impossible for
bounded Foo; it simply was not selected. Do not reopen that policy decision without a
concrete invalidating finding, changed assumption or explicit new requirement.

The current registry's monitor/bucket scans and unbounded global-history lookup are
still invalid. A paid generation-owned append log is a replacement direction, not a
claim that the implementation is compliant. Each hook must complete the contract's
site/tariff/lifetime/paid-work/failure/cleanup manifest before admission. User approval
settles the model; it does not certify any hook or finish the Foo proof.

## 7. Gate disposition and stop point

- **Selected as feasible local design:** native type-use recording, isolated native frame,
  funded optional rendering, tri-state fixed-list comparison, recursive-context API.
- **Selected conditional strategy:** expected/actual native export agreement instead of
  live removal enumeration; unchanged regression controls remain mandatory.
- **F1 still needs a finite actual Foo component/entry manifest.** Broad activation is not
  an added prerequisite; no accepted fixture is silently reassigned.
- **F2 still needs exact per-dependency actual-use/completed-mutation/host-epoch mapping.**
  Participating leases do not supply alias ownership.
- **F3's accounting model is approved.** Actual site tariffs, variable-work bounds,
  storage and lifetime ownership remain unclosed; no existing hook is certified by approval.
- **F4 is design review only**, not a replacement for implementation validation/review.

The resource alternative is settled. Any resumed source work must now produce the one
finite Foo entry/component/epoch/tariff manifest under that contract, within a stated
budget—not another broad investigation or a reopening of approved accounting. No experiment was needed in this pass because its remaining
questions concern contracts and algorithms, not measured timing or throughput. Approved
local strategies should not be reopened without a concrete counterexample or changed
assumption. Full Task04 role/retention coverage, required validation and exhaustive stable
implementation review remain mandatory; no production checkpoint is authorized by this report.

Document-only integration review `df053480c6601259f813513d373d55a85` completed against this
report, the main design and the authoritative task documents. It confirmed the scope/
resource distinction and required four consistency corrections, assessed and applied in
one document-only batch. It did not independently verify sources, close the actual Foo
manifest, or sign off the current implementation. Alternative B was subsequently
approved by the user; its documentation obligations are defined in the linked contract.
