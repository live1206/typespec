# Task04 approved bounded-overhead contract

**Decision: Alternative B approved by the user on 2026-11-12, subject to explicit
bounded-overhead documentation.** This document defines that approval's boundary.
It does not approve the current implementation, close its remaining evidence gaps,
change input admission, or authorize broader activation.

Read with the [write-once design](./pre-emission-task04-write-once-design.md) and
[closure report](./pre-emission-task04-write-once-closure.md). This is the normative
resource contract; their earlier Alternative A discussion records the decision history.

## 1. Guarantee and non-guarantees

For independently identified entry classes `i`:

```text
W_added <= sum(c_i * N_i) + W_paid
```

Equivalently, `W_added <= c * N_entry + W_paid`, where `c` bounds the admitted `c_i` values.

- `N_i` counts actual qualifying original-native events or externally initiated protocol
  entries at a listed site. It does not count new internal work created by Task04.
- `c_i` bounds the complete added straight-line entry/deny path, including Task04-specific
  argument preparation, dispatch, callees and fixed exit work—not just the recorder body.
- `W_paid` is additional non-ingress work funded by the existing ledgers. Existing ceilings
  remain: 1,048,576 generation, 262,144 recipe, 65,536 supplemental, 4,096 envelope, and
  the existing distinct limits. Linked children do not mint new parent capacity.
- Additional retained storage is generation-owned and bounded by prepaid capacities.
  The entry allowance is **not** an allocation or storage allowance per invocation.

This is an algorithmic work bound, not a deadline, CPU-cycle count, or wall-clock latency
promise. It permits total fixed overhead proportional to native/external entry count;
it does **not** claim all added work is bounded by an absolute input-independent constant.

Approval does not make an arbitrary helper call constant time. Every transitive operation
must satisfy its classification, including first use, rejection and disposal. No blanket
runtime/JIT/cache-initialization exception is added; existing narrow approvals remain narrow.

## 2. What is a qualifying entry?

A site must be identified before implementation and explain which independently existing
native operation or external protocol call would occur without its extra observation work.
The counted boundary includes added work before an enabled/failed check as well as after it.

| Candidate site class | Counted event | Boundary / exclusion |
| --- | --- | --- |
| Provider write begin/end | An original native provider writer invocation/completion. | Each boundary has its own tariff. Calling Begin/End repeatedly inside POC code cannot create more free entries. |
| Native occurrence/declaration/type use | A native consumption/formatter site, with actual native values already available. | A new POC traversal over children is paid, even if it calls the same observer per child. |
| Native namespace/interface result | A native registration or reflection result already computed by the original execution. | Replaying registration, reflection or enumeration for POC is not another original-native event. |
| External protocol call | An independently initiated start, query, failure or release/dispose request, including rejected/concurrent test calls. | Nested POC helpers, notification/help recursion, retries and cleanup loops are not independently initiated entries. |

These are **candidate categories, not certified hooks**. The actual Foo manifest must name
the concrete sites and their tariffs. No current hook is declared compliant merely by
appearing in a category. Observer-created loops/recursion cannot be laundered into `N_i`.

## 3. Constant ingress: allowed operations and exclusions

An admitted path is a finite branch tree with an audited fixed maximum and only known,
bounded leaf operations. Its bound cannot depend on input length, graph depth, stored
entries, collision prefixes, number of threads, cache warmth or process history.

| May be classified in `c_i` after source audit | Must not be classified in `c_i` |
| --- | --- |
| Reads/writes of a fixed number of known scalar/reference fields; reference identity comparisons. | String/content equality, hashing, scanning or normalization; arbitrary property/interface getters. |
| Length/count read from an already-held exact array/string or audited scalar field. | Asking an arbitrary collection for Count; indexers/enumerators with unknown implementations. |
| Finite flag/enum checks, primitive arithmetic and checked range tests. | Variable loops, recursion, retry/help loops, hidden loops in called helpers. |
| A bounded number of atomic/volatile primitives, including one CAS attempt; deny on contention. | CAS retries, spin/wait/monitor/semaphore acquisition, waiting for another observer. |
| Fixed-size writes into an already allocated and prepaid slot; readiness publication. | New arrays/objects/closures/exceptions, buffer growth, boxing, string formatting or logging. |
| Returning a retained native/legacy result reference; storing a fixed reason code. | Reconstructing fallback, copying a file, or walking alternatives to discover whether POC exists. |

Dictionary/weak-table lookup is not automatically constant: collision/history work and
hash callbacks need a paid bound. Reflection, symbol lookup and metadata realization
are not ingress merely because their public API is one call. An unknown callback or
virtual operation is not an audited leaf. Known internal leaf calls must include their
full transitive work in the tariff.

Native work that would occur unchanged is identified from baseline behavior, not asserted
free by moving it into a helper. Approved moved-header preparation retains its existing
accounting. Cold-path initialization with allocation or other excluded work must be
prepared/funded before installation or covered by an existing explicit approval.

## 4. Paid work, storage and exhaustion

The following remain paid **before** execution:

- Capture, variable comparison, recursive currentness, collection read/copy and lookup.
- POC metadata storage/capacity changes, buffers and output-sized allocation/rendering.
- Index building, deduplication, collision handling and queries over retained observations.
- Variable notification/help/detach/release/disposal work. Reserve cleanup at installation;
  zero remaining credits do not justify unfunded cleanup or leaking retained references.

Operations cannot be funded from an earlier size if they later traverse a larger live
collection. Reserve against the actual fixed bound used by the algorithm. A false result
after an unbounded copy is not bounded observation.

Storage is allocated only after reservation. A recorder may claim one prepaid fixed slot
with one bounded attempt and publish actual native scalar/reference facts. Capacity
exhaustion/contention produces Unavailable/denial as required by the observation contract;
no unbounded retry, dynamic table growth or late observer reattachment follows. Slot/counter
saturation is fail-closed, not integer wraparound.

When variable funds are unavailable, perform only the admitted fixed deny path:

1. Do not start a copy, lookup, enumeration, reflection probe or allocation.
2. Do not construct a fresh budget, borrow independent sibling funds, or replay a callback.
3. For an optional POC view, return the retained NativeFrame/legacy BaseResult. No output-
   sized restoration is required. Native exceptions and existing independent choices survive.
4. Preserve observation-local status: a point query's Unavailable is not a funded mismatch
   and does not poison an independent query. A missing required native observation/seal
   still fails closed at its owned scope.

Actual mismatch evidence must be covered by an audited fixed fact or paid observation;
missing resources are not mismatch evidence. This model changes resource accounting, not
binding authority, sticky invalidation, callback-once behavior or native exception timing.

## 5. Per-hook documentation required before admitting it

Each manifest entry must contain all of the following. A missing bound is **unadmitted**,
not an assumed small constant. Do not invent numerical tariffs before reviewing the code.

| Field | Required content |
| --- | --- |
| Site ID and source | Exact method/callsite, owner and native/external event being counted. |
| Baseline comparison | Why the event exists independently of Task04; all added argument/dispatch/exit work. |
| Lifetime | Installation, eligible phases, generation/epoch identity, failure and terminal release. |
| `c_i` proof | Complete straight-line branch/callee inventory, fixed maximum and tariff units; no hidden variable operations. |
| Entry prerequisites | Initialized runtime facts and prepaid storage; no cold allocation/metadata work hidden before the guard. |
| Paid operations | Which operations leave ingress, their actual bounds, ledger/child owner and reservation point. |
| Failure matrix | Disabled, exhausted, wrong owner, contention, overflow, native exception, late notifier and disposed behavior. |
| Storage/cleanup | Capacity bound, pending-slot readiness, retained references, cleanup reservation and no resurrection. |
| Validation/review | Source identity, original anchors, boundary/zero-credit/concurrent controls and independent assessment. |

For a fixed native recording path, the intended shape is: read the prebound owner/state;
deny if ineligible; attempt one prepaid slot claim; store fixed native facts; publish
readiness. Actual implementation may differ only with its own audited constant path.
Any comparison, copied payload or query introduced around that path needs its paid rule.
A prebound owner cannot be obtained through an unbudgeted global/history-dependent lookup.

## 6. Existing failures are not excused by this decision

The following remain invalid, not reclassified as constant ingress:

- Three-credit shared prepayment followed by copying thousands of grown-list entries.
- Zero-credit provenance lookup through an unbounded global weak-table history.
- Native registry bucket scans or monitor contention hidden behind a fixed key cap.
- Scanning all candidates/ranges per write or restoring a whole file on POC exhaustion.
- Treating each POC-created loop iteration as a new native event.

Existing stop-on-failure, exhausted-start, one-time notification, detachment, parameter ABA,
independent-credit and native/shared parity regressions stay unchanged. Even repeated
constant bookkeeping is not permission to undo accepted early-stop fixes. No existing fix
or coverage is declared redundant by choosing B.

## 7. Settlement, review and remaining work

**The accounting model is settled by approval.** Do not re-litigate strict-total versus B
without a concrete invalidating finding, changed assumption or explicit new requirement.
An individual hook is settled only after its manifest row, bound, regression evidence and
required review are complete. Changing a tariff/site invalidates that hook's evidence and
requires relevant documentation, validation and review; it does not automatically reopen
approved unrelated hooks. Allowing excluded operations in ingress requires a new decision.

This document is not a measured runtime benchmark or implementation proof. The actual Foo
entry/emitter/dependency manifest, source/configuration epochs, currentness recursion,
owned lookup/storage and exact tariffs still need closure. Full accepted Task04 coverage
and stable implementation validation/review remain required. No production code, existing
tests, source admission or public API was changed to record this decision.
