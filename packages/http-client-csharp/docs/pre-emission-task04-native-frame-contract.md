# Task04 native frame and POC selection contract

**Status: LOCAL DESIGN CONTRACT DONE.** This closes retained-output representation,
coordinate projection, selection and reservation rules. It does not create Task04 spelling
authority, implement native capture, close Foo's emitter/epoch proof or enable shortening.
[C0](./pre-emission-task04-c0-contract.md) remains closed independently.

Evidence: isolated frozen-source delegate `d2887170591bd92e008abb515d5616ca1`, 12 turns /
11 tool calls, followed by parent source assessment. Its five live sources matched the
frozen snapshot at integration. HEAD `2afe54d5de261897d49a182e5eda2c6d3411ba95`, tracked
diff `49c38f91188c59343543eda88fb2ad74fe4c26ad56c77537866311890f0f7d8f`.
No code/test changes, builds or experiments were performed to close this contract.

Read with the [approved resource contract](./pre-emission-task04-bounded-overhead.md) and
[Foo blueprint](./pre-emission-task04-foo-blueprint.md). Names below are conceptual internal
records; final class layout and numerical tariffs are implementation refinements.

## 1. Existing source boundary

- [CodeFile.cs:19-49](../generator/Microsoft.TypeSpec.Generator/src/Primitives/CodeFile.cs#L19)
  owns content, qualified content, ranges, Name and the receipt reference.
- [CodeFile.cs:59-108](../generator/Microsoft.TypeSpec.Generator/src/Primitives/CodeFile.cs#L59)
  has an all-allowed metadata bypass and all-qualified malformed-range fallback. The new
  POC adapter must not inherit either as its POC validation/isolation policy.
- [PreparedNameAlternative.cs:13-24](../generator/Microsoft.TypeSpec.Generator/src/Primitives/PreparedNameAlternative.cs#L13)
  establishes owned spelling plus numeric offsets, not pooled-buffer spans.
- [CodeWriter.cs:1058-1138](../generator/Microsoft.TypeSpec.Generator/src/Writers/CodeWriter.cs#L1058)
  drains buffers, constructs the actual common header once and transfers payload offsets.
- [CSharpGen.cs:193-282](../generator/Microsoft.TypeSpec.Generator/src/CSharpGen.cs#L193)
  owns staging, final legacy decisions and workspace submission. It is not a transactional
  disk-publication boundary.

## 2. Owned records and handoff

| Record | Contents / invariant |
| --- | --- |
| NativeFrame | Original POC-free primary and optional qualified strings by reference; legacy field/body metadata; exact Name/path, SourceConsumption reference and provenance. Structurally immutable. |
| PocUse | Opaque occurrence-owned choice/slot ID, completed actual-read evidence, primary and qualified ranges, native order, owned prepared short spelling. Separate from legacy ranges. |
| BaseResult | Correct legacy field/body result with no applied POC shortening, associated with its originating frame and exact legacy decisions. |
| PocView | Terminal rendered bytes plus exact frame/base/legacy/POC decision vector. Not a new preparation lifetime or permission cache. |
| ViewState | Generation-owned metadata and at most one tentative view plus one differing final view; bounded, prepaid storage. |

Receipt identity is preserved; its internals are not thereby immutable or authoritative.
Authority stays with its existing owner. The NativeFrame must not be built from an already
POC-shortened preview and call it native. Legacy synthetic inline-POC fixtures retain their
old API behavior; they are not automatically valid inputs to this new POC-free adapter.

The capture handoff supplies payload ranges in both coordinate systems, actual-read evidence
and order. Finalization adds the **actual native header length** once to produce file ranges;
never reformat a header or search text to recover coordinates. If no qualified companion
and no legacy differences exist, primary coordinates are canonical. Ranges own no spans
into writer pools. Repeated drains transfer/copy owned immutable metadata; later writer
reuse/disposal must not mutate an earlier frame.

## 3. Establish BaseResult without POC reconstruction

At the ordinary final legacy selection point, `CSharpGen` computes and retains BaseResult
using its unchanged shared/field/body gates, then requests the optional POC view. The
adapter receives that already selected result and its origin/decision context; it does
not repeatedly call the legacy renderer to obtain fallback. A raw tentative request uses
the native-primary legacy decisions and original frame as its base.

Only genuinely unchanged legacy selection counts as baseline work. Additional wrapper
creation, certification scans, copies or repeated selection calls introduced for POC are
paid. A final legacy decision is not silently recomputed inside each POC cache query.
Wrong frame/owner/decision context yields no POC result; the caller retains its independently
valid legacy base, not an arbitrary supplied result disguised as authorized output.

No-metadata field-only compatibility remains. If two legacy buffers differ without the
metadata needed to project between them, retain ordinary BaseResult and deny POC. Do not
infer a difference by parsing/searching output.

## 4. Validate before a POC-enabled fast path

Check enabled **and disabled** entries, with checked/subtraction-based arithmetic:

- Correct frame/generation/file/choice ownership; completed, unique, correctly ordered uses.
- Nonnegative starts, positive lengths and end within the corresponding immutable string.
- Nonempty owned prepared short spelling, strictly shorter than the native payload.
- Sorted disjoint intervals in both coordinate systems; no POC/legacy overlap, containment
  or nesting initially, including a disabled interval. Nested choices require a later design.
- Legacy metadata is valid whenever POC projection relies on it. Unknown/malformed channels
  are not accepted via the old all-true bypass.

Use IDs are not validated by an unbudgeted hash set. A concrete bounded option is the
prepared file slot table of capacity `K`, direct bounded index/reference checks, and a
prepaid `K`-entry seen bitmap for duplicate detection. Actual native-order/range ordering
is checked in one forward scan. Unused planned choices need not have emitted uses; required
missing-use semantics come from the separate consumption contract. Bitmap initialization,
validation and cleanup are paid, not constant ingress. An equally explicit immutable
ordinal scheme may replace the bitmap if it proves uniqueness without assuming it.

Validated immutable frame metadata may be reused by its exact frame identity. Do not cache
source/input freshness or final authority under that metadata certificate.

## 5. Numerical projection and rendering

Qualified content is the canonical coordinate system when present. For each selected
legacy interval `i`, define:

```text
delta_i = shortLength_i - qualifiedLength_i
baseStart(q) = q + sum(delta_i for selected legacy intervals ending at or before q)
```

With no overlap, the POC payload length is unchanged. Check supplied primary coordinates
against the projection where all native-primary legacy choices are applied. Merge sorted
legacy and POC arrays with forward-only cursors; no sorting, hashing, prefix rescans or
text search is needed. The merge also computes the checked resulting length.

For allowed POC uses, copy unchanged BaseResult segments and prepared replacements in one
forward traversal into a fixed/pre-sized destination. Do not parse/rebind output, discover
new choices or reconstruct qualified fallback. Shortening never increases result length.

An exact-vector cached view may reuse **bytes only**. Every final request independently
needs applicable sealed authority tied to the frame/owner/epoch and uses; the renderer
cannot obtain it from cache equality. Reference equality of immutable decision vectors
is sufficient for byte reuse; otherwise pay a bounded element comparison. Equal hashes
or POC booleans alone are insufficient when legacy decisions/base identity differ.

At most one tentative and one differing final POC rendering is retained. Further distinct
render requests decline the optional POC result and leave the supplied, independently
valid BaseResult untouched. Their queries/comparisons are paid if not fixed ingress.

### Concurrent requests, retry and disposal

Each tentative/final phase has a prepaid claim slot. One CAS attempt claims variable-work
ownership; contention returns Unavailable with no waiting or retry loop. Writable scratch
is phase-owned (or separately reserved), never concurrently reused. Fund each attempt's
variable work; immutable metadata validation may be reused only by its exact frame.

Underfunding before materialization may release/reset that exact claim with one CAS so
an independently funded later request can succeed. It does not poison the other phase or
consume a rendered-view allowance. Already materialized views remain bound to their exact
vectors; do not reset them to permit unlimited distinct copies. Only separately initiated
requests may retry; no POC-created retry loop counts as external ingress.

Publish a ready immutable view by CAS from the exact owned claim. Claim identity must be
unique per attempt, or use a completed nonwrapping/saturating epoch: reusing an indistinguishable
marker must not permit ABA publication after a released claim. Any unique ticket allocation
is paid before the claim, never ingress allocation. No publication occurs after release.
Disposal atomically installs a preinitialized terminal tombstone; a delayed publisher cannot
replace it or resurrect state. Do not check disposed and then perform an unguarded cache
store. Failed publication drops local references and performs only reserved cleanup. Slot,
epoch and budget ownership must be audited in implementation; an ordinary non-thread-safe
ledger is not assumed safe for contenders.

## 6. Failure behavior

| Condition | Result |
| --- | --- |
| Invalid POC range/evidence/order/ID, unsupported projection, missing required use, POC overlap or unavailable POC resources | Deny this file's POC view; retain BaseResult and independent legacy choices. |
| Shared authority failure | Apply existing shared denial to dependent legacy channels too; POC cannot override it. |
| Malformed legacy metadata needed for reconstruction/projection | Legacy/shared invalidity: qualified fallback and POC denied, following the adapter contract. |
| Qualified fallback unavailable for malformed legacy reconstruction | Do not invent bytes; decline the optional adapter and preserve existing native handling. |
| Native exception | Propagate with existing semantics; do not replay work. |
| Catastrophic allocation failure | Preserve existing exception policy; do not relabel it ordinary budget exhaustion. |

POC exhaustion returns the retained base reference with no restoration scan/copy/allocation.
Any newly needed qualified-fallback wrapper is prepaid before validation, or retained from
frame setup; discovering invalid legacy metadata must not require an unfunded allocation.
Legacy-only APIs keep their established compatibility behavior, including the deliberate
all-allowed bypass tested today. Strong POC-adapter validation is a distinct entry contract.

## 7. Reservation owner and explicit work bound

One generation output-reservation owner supplies existing generation/child-ledger capacity.
Files/views do not create fresh budgets or borrow independent sibling funds.

Let `L`, `Q`, `A`, `U`, `K` be actual primary/qualified lengths, legacy range count,
completed POC-use count and prepared file-slot capacity. All loops use immutable lengths
or prepaid fixed capacities; a live collection's earlier size is not a later work bound.

1. **Before installation/capture:** reserve frame/view-state objects, fixed slot/metadata
   capacities and cleanup. No unbounded growth or history-dependent fallback dictionary.
2. **Before offset transfer/metadata publication:** reserve the added `U` transformations,
   immutable-array storage and copies; use the actual native header scalar length.
3. **Before validation/projection:** reserve bounded passes over `A + U`, plus `K` bitmap
   initialization/cleanup if used, and any decision-vector comparison. Deny unsorted input
   rather than allocating/sorting it.
4. **Before a view render:** after the funded merge computes length, reserve destination
   allocation, copied segments/replacements, result wrappers and final-string materialization.
   If runtime construction initializes or copies another full buffer, charge that pass too.
   No hidden StringBuilder growth. Size is at most BaseResult length, bounded by `max(L,Q)`.
5. Each tentative/final render has a separate reservation; exact-vector reuse avoids the
   second rendering, not its required authority. Never start partial work and hope fallback
   can be funded later.

Added work is a fixed number of linear metadata/character passes, including `K` where the
selected uniqueness algorithm uses it: `O(A + U + K + rendered characters)` per admitted
view, at most two views. Storage and cleanup remain prepaid. Numerical tariffs and runtime
copy details are audited during implementation; ownership/pass/capacity/denial rules are
settled here. Nothing becomes free merely by fusing it with native finalization.

## 8. Implementation locations and future validation

Minimal locations: CodeFile (frame/base/adapter/view state), PreparedNameAlternative (separate
POC use/coordinate/vector records), CodeWriter finalization (owned metadata plus actual
header), CSharpGen staging (retain legacy base then optional view), and existing alternative
tests. No public Content mutation or native carrier substitution is needed.

Preserve existing tests and add adapter-specific controls for:

- Mixed field/body/POC bytes and receipt/Name identity; POC-only failure isolation.
- Invalid enabled/disabled/all-allowed metadata; legacy-only compatibility unchanged.
- Multiple selected/unselected legacy deltas, no-companion case, overlap/nesting and overflow.
- Duplicate/out-of-order IDs, slot ownership, missing required uses and unused planned slots.
- Exact/one-short validation/storage/render/final-copy reservations; same-reference denial.
- Exact-vector reuse, changed legacy base/decisions, two-view limit, fresh authority despite
  cached bytes, independent frames after repeated drains, and exception behavior.

These are future checks, not tests run here. This local contract is settled within its
input/ownership boundary. Capture implementation and Foo target/export/alias/epoch authority
remain separate tasks; their absence does not reopen this representation/selection decision.
