# Task04 write-once design: authority before implementation

**Status: proposal, not implementation approval or Task04 completion.** This document
selects a write-once architecture, makes its missing obligations explicit, and defines
when to stop investigating or settle a decision. **Resource Alternative B is now approved**
under the [bounded-overhead contract](./pre-emission-task04-bounded-overhead.md); that
approval does not certify existing hooks or close the remaining Foo profile. A certificate
name, passing traversal receipt, or fallback is not evidence that an accepted positive works.

Read with the [implementation handoff](./pre-emission-task04-implementation-handoff.md),
[acceptance map](./pre-emission-task04-acceptance-map.md),
[input-freshness analysis](./pre-emission-task04-input-freshness.md), and
[shared implementation guide](./pre-emission-implementation-guide.md). Their accepted
scope and safeguards remain authoritative. This proposal does not exclude pending
roles, authorize default activation, or reopen previously approved lifecycle choices.

## Executive recommendation

Retain the native field/body output as an immutable fallback frame. Record the actual Foo
type-formatting use without executing its formatter twice. Attach only a precomputed,
occurrence-owned short alternative. Budget optional POC views after native lengths are
known; POC rejection returns the native frame with its independent legacy choices intact.
Seal only under a finite emission/export, source/configuration and auxiliary-effect contract.

This selects the output/observation algorithm, not a fictional completed certificate. The
remaining design decisions are explicit: the lexical/export profile for the original
admitted Foo consumer, its actual-use/epoch boundaries, and ownership/costs of additional
work. The [bounded closure report](./pre-emission-task04-write-once-closure.md) selects the
local algorithms. The user approved B with explicit per-hook documentation, as defined in
the bounded-overhead contract. Unrestricted normal CLI admission is not an extra first-
milestone requirement; do not substitute a different fixture/source authority either.
The next design deliverable is the finite Foo entry/component/epoch/tariff manifest.

## 1. Baseline and objective

Inspected branch: `feature/pre-emission-task04`, HEAD
`2afe54d5de261897d49a182e5eda2c6d3411ba95`, with the existing uncommitted Task04 batch.
The last expanded checkpoint (557 Core / 225 Client, 4,377 unchanged hashes) predates
that batch. Focused later runs are evidence for their tested revisions only. No tests,
experiments, regeneration, production edits, commits, or pushes were performed to
prepare this proposal. Historical diagnostics cited below are not current-tree validation.

The first milestone is the unchanged real Foo `PipelineMessage` using-local anchor,
prepared before output writing and short in designated raw writer output. All other
accepted Task04 cases remain pending until independently delivered. Passing all provider
observations while this anchor remains qualified is not a successful milestone.

**Write once:** execute each native output-provider writer, its traversal, getters,
formatters and callbacks once. Capture already-prepared spelling alternatives during
that pass, validate their dependencies, then choose captured bytes before publication.
Multiple buffers and a final numeric-range renderer are allowed; a second provider
write, generated-output parsing, post-write binding, or discovering reductions from
text is not. The approved early in-memory attribute bootstrap is a separate exception.

## 2. Decisions proposed for settlement

| ID | Decision | Qualification |
| --- | --- | --- |
| D1 | Reuse prepare -> one native write -> staged validation -> captured-alternative selection -> publish. | Existing field/Task03 controls establish this mechanism in their admitted cases, not Task04 authority. |
| D2 | Give Task04 its own occurrence-owned plan/use/commit channel. | Do not remove Task03's single-file admission or populate its permission store with POC candidates. |
| D3 | Prepare all spelling and lookup decisions before output writers. | Later observations only validate/deny captured choices; they cannot rebind or discover a new spelling. |
| D4 | Describe coverage compositionally from exact existing writer/IR contracts. | No general C# parser, broad binder, arbitrary callback-purity system, or new compiler host. |
| D5 | Model observation results locally as Match / Mismatch / Unavailable. | A reused budget is not the owner of sticky unavailability. |
| D6 | Bound the work actually performed, not a mutable collection's earlier size. | No prepaid call into an unbounded legacy helper. Existing caps and narrowly approved runtime exceptions remain unchanged. |
| D7 | Keep failures dependency-scoped. | POC-only failure does not invalidate independent field/Task03 permission; shared failure denies dependent channels. |
| D8 | Require feasibility closure before implementation batches. | An unresolved ownership, coverage, or bounded-work obligation is a stop, not an invitation to grow more machinery. |
| D9 | Obtain POC fallback from the actual native formatter in its original pass. | A qualified string reconstructed from an earlier plan cannot stand in for changed native output or preserve skipped native exceptions. |
| D10 | Close relevant auxiliary effects as well as the provider stream. | Entries invoking `WriteAdditionalFiles`/scaffolding need an exact no-relevant-effect contract, not just non-C# filenames; unknown effects veto that path. |
| D11 | Resource B: fixed audited ingress plus paid variable work. **Approved 2026-11-12.** | Per-site bound, owner, prerequisites, variable costs, failure paths and cleanup must be documented. No loop/allocation/reflection/hash-history/lock/retry in ingress; existing caps and regressions remain. |

D1 is the recommended architecture, not an assertion that the current POC can use it.
D2-D10 need the implementation-level obligations below discharged and design review.

## 3. Reusable implementation and its evidence boundary

| Existing component | Reuse | Does not establish |
| --- | --- | --- |
| `CSharpGen.ExecuteAsync` preparation, staging and final selection | Own a single finalized-provider phase; stage files until native enumeration finishes. | Global absence of other writers, immutable inputs, or POC commit authority. |
| `PreparedPocHeaderFinalization` | Approved materialization of audited built-in headers before capture. | Arbitrary getter replay, complete grammar, import safety, or a general model freeze. |
| `PreparedFieldTypes` field/body permission paths | Reuse applicable lookup/source facts without relaxing their admission. | That a generation-wide POC candidate satisfies Task03 admission. |
| `CodeWriter` qualified companion and `PreparedNameAlternative` | Capture both spellings in one native pass; retain owned text and numeric offsets. | Binding, currentness, or permission to enable a channel. |
| `CodeFile.SelectPreparedNames` | Render only captured alternatives; two-argument calls deny POC by default. | A new simplifier or semantic finalization engine. |
| POC provider stream and `PreparedPocGenerationEnvelope` | Observe actual yield/write order and occurrence consumption. | Grammar, lexical confinement, complete scope/import safety. `HasUncoveredEmission` remains true. |
| Construction witnesses | Recognize admitted construction provenance without replacing native carriers. | Current elements, immutable ownership, or an unlimited/unfunded registry lookup. |

The latest handoff and original repros live under `.pi/issue-12454/`. In particular,
`final-review-20261111/repro-*/README.md` separates reproduced failures from partial fixes.
Do not rerun an already established baseline merely to rediscover its defect.

## 4. End-to-end protocol

The following names describe conceptual records and operations, not APIs already present.
They should map onto existing ownership where possible, not introduce a parallel lifetime.

### 4.1 Preparation: immutable choices, explicit dependencies

Create a generation-owned `OrdinaryNamePlan` containing:

- generation, provider/file, occurrence and role IDs; exact admitted writer identity;
- target symbol/type identity and native type/producer identity or revision witnesses;
- the prepared short spelling and expected native qualified shape, including retained children;
  the expected spelling is a validation fact, not a replacement for actual native output;
- source-observation and lexical-scope facts, namespace/import dependencies;
- references to the finite coverage contracts required by this occurrence;
- the expected consume site and count, and links to funded validation ownership.

For the first Foo plan, the role is the actual using-local declaration's type, not an
interpolated surrogate. Its short spelling is `PipelineMessage`; its qualified spelling
is `global::System.ClientModel.Primitives.PipelineMessage`. Preserve the using/local
construction, variable name, initializer, native namespace registration and source policy.

Refactor reusable pure fact computation out of existing permission preparation only
where necessary; preserve the original Task03 entry-point guards and regression anchors.
A stored `PreparedPocOrdinaryCandidate` is input to preparation, never permission by itself.

Before any final output writer starts, all eligible short alternatives and lookup proofs
exist. The qualified fallback bytes are captured from actual native emission later. Missing
required facts deny this plan; they do not justify rebinding during writing.

### 4.2 Native writing: consume plans, observe actual values

At the designated standard writer slot:

1. Match the generation/file/occurrence/producer/role and admitted writer identity.
2. Begin a bounded recording window around the existing native type formatter; record
   payload offsets, not a second formatted result.
3. Execute the native formatter exactly once. Preserve all native namespace registrations,
   reads, callbacks, surrounding syntax and exceptions. Observe facts from its actual local
   values/branches, without reading the producer again or enumerating its arguments twice.
4. Complete a `NativeTypeUse` only on normal native return: plan ID, actual consumed
   descriptor/branch evidence, qualified payload range, original-buffer range and use order.
   If that evidence does not match the prepared expected shape, retain native output and
   reject that POC use; do not generate a different short spelling.
5. Attach the already-prepared short alternative to a matching completed use. Fuse its
   tentative raw rendering with the existing buffer-finalization/offset-transfer pass.
   Keep the actual native bytes intact in the qualified companion for final fallback.

**Selected integration:** replace the POC bypass with a native-site recording adapter,
not another formatter. `DeclarationExpression.WriteCore` and `CodeWriter.AppendType`
are the concrete first integration sites; existing Task03 behavior need not be changed.
The initial admitted native shape is exact `CSharpType`, non-generic/non-nested/non-array,
non-nullable, not a keyword/alias or attribute name, at the exact Foo using-local slot.
Broader accepted shapes remain pending, not excluded. Enter the recording scope without
reading `Variable.Type`; observe the value passed by the existing interpolation to
`AppendType`. Begin its type-token range after `using ` and any indentation, and end it
before the following space and variable. This excludes the declaration prefix, variable,
initializer, terminator and header. No generic child POC ranges are admitted initially.
Convert payload offsets to file offsets using the existing header/offset-transfer boundary.

**Selected representation:** retain an immutable `NativeFrame` with the exact existing
field/body primary content, qualified content (if the existing path creates it), and
legacy metadata. POC recording never changes those native buffers or requires creating
a new whole-file qualified companion. Store POC uses separately, with native-primary and
qualified coordinates and plan IDs. Validate ordered, non-overlapping, in-range spans,
matching lengths/ownership and native-shape evidence before materializing any POC view.

`Content` may expose a budgeted tentative POC-short view for the designated raw-writer
test; the preserved `NativeFrame` is still the fallback. Range rendering is numeric only:
never parse, search, or bind text. Metadata validation must precede every POC-enabled fast
path; the current `CodeFile` all-allowed fast path is not a sufficient validation gate.
The private representation needs adaptation; the existing mixed-channel array alone does
not supply these guarantees. A POC range overlapping a legacy range is denied initially;
nested/child choices need an explicit later range-tree design, not overlapping replacements.

If no plan matches, run the ordinary native route. An exception completes no POC use and
propagates unchanged. No temporary provider write, second formatter call, guessed stale
fallback, or second argument enumeration is allowed. The adapter's exact read/order and
copying-cost audit is a required feasibility obligation, not proved by this interface.

A required event missing, duplicated, reordered, wrong-owner or observed with a changed
value rejects the affected plan/file. A start snapshot followed by later independent
native enumeration is not evidence about the values actually emitted.

### 4.3 Seal: validate captured uses, never discover decisions

After successful exhaustion of the native provider stream and admission of every other
C# output path, and before publication:

- seal stream ownership, writer completion and coverage receipts;
- validate only dependencies of used plans, plus genuinely shared dependencies;
- distinguish observation unavailability from a funded mismatch;
- deny POC if any required proof is missing, unavailable, rejected or not owned;
- derive immutable selection results, separate from field/body selection.

`Seal` must identify its precise linearization point and how concurrent failure/disposal
interacts with it. A flag read followed by an unguarded permission publication is not a
protocol. Mutations of aliased native objects are not prevented by freezing the plan.
There is no approved general freeze or serialization assumption.

If arbitrary relevant mutation cannot be covered by an admitted revision/consumption
protocol, stop at feasibility gate F2 below. Do not paper over the gap with a last equality
check or a new lock. Future unrelated edits after a valid sealed artifact are not the same
thing as an undetected in-flight mutation of a dependency; that boundary must be specified.

The proposed generation-owned lifecycle is:

| State | Allowed activity | Terminal behavior |
| --- | --- | --- |
| Prepared | All plans fixed; no native output has started. | Only an exact admitted stream/writer may acquire a consumption lease. |
| Observing | One owned native provider stream; live required consumption leases. | Overlap, duplicate use or observed violation rejects POC, not the native operation. |
| Sealing | Entry closed; stream exhausted; all acquired required leases released. | Validate and consume owned receipts; no new use can join this generation. |
| Sealed | Immutable accept/deny decisions and actual native alternatives. | Render/publish only those terminal artifacts; no new permission or native use. |
| Rejected / Disposed | No further POC authorization. | Retain native fallback and detach/release exact owned observers. |

Use a common atomic state/lease protocol for entry closure, sticky rejection and sealing,
with bounded single-attempt claims rather than unbounded CAS retry loops. An outstanding
lease, contention or uncertain terminal observation causes bounded POC denial: never spin
or wait for completion. A completed seal is the linearization point for its immutable
receipt/artifact, not permission for later calls to join the generation. A pre-seal acquired
observation must release before a successful seal, and its failure must be visible through
the same terminal-state protocol before release. Delayed notifier callbacks may help detach
but cannot publish fresh authority or resurrect observers. Post-seal attempts return no
permission and cannot mutate the sealed artifact.

This lease protocol covers participating operations only. Each aliased Foo dependency
still needs its concrete consumption/revision boundary; delayed notification of a prior
mutation cannot be made safe by sealing first. Source/configuration facts must identify
their validity epoch through seal and the owned artifact's compilation-input identity.
Do not require indefinite immutability after generation, but also do not classify later
in-generation auxiliary effects as unrelated external edits.

This specifies the required semantics, not an already proved implementation. The actual
state-word/lease ownership, independent source/configuration validity interval, failed-entry
ingress cost and attach/dispose schedules must be discharged at F2/F3. Do not equate adding
an atomic flag with closing those obligations.

### 4.4 Select, publish, release

1. Select the independently authorized field/body result from the preserved `NativeFrame`
   through its established path. Call this `BaseResult`. POC-only failure returns this
   result, not `WithoutPreparedNames()` on a mixed POC object.
2. If POC metadata/evidence is invalid, missing, denied or underfunded, discard its view
   and return `BaseResult`. No POC restoration copy is needed: native buffers were retained.
3. Otherwise validate the separate POC metadata and project coordinates through selected
   legacy ranges using one ordered metadata merge. Reserve all additional work first;
   then copy `BaseResult` with the already-prepared POC replacements in one bounded pass.
4. Reuse a tentative view only when its validated native frame, complete selection vector
   and sealed authority match exactly. Never return unvalidated POC `this` via an old fast path.

Malformed POC metadata denies POC for that file without changing independent legacy flags.
Malformed shared/legacy metadata is a shared failure and retains the established native
fallback behavior. This distinction fixes the proposal's earlier reliance on the mixed
renderer, whose invalid-range fallback can discard every channel.

The first slice may use one file-level POC allow bit only if all POC uses in that file
share the same authority. Independently accepted/denied occurrences later require explicit
per-use selection; do not silently replace occurrence-owned policy with file-wide permission.

Publish terminal strings, then release generation-owned observers and retained references
only when the chosen validity contract no longer depends on them. If auxiliary closure
requires evidence through later callbacks, an earlier release cannot satisfy it; no
unapproved lifetime extension or late check can retroactively revoke a published file.
No rendered `CodeFile` starts a new preparation lifetime. Failure before successful seal
and selection authorizes no newly shortened POC output. Existing generation is **not
transactional**: after seal, file-by-file writes or auxiliary work can fail after earlier
files reach disk. Preserve and disclose that partial-publication behavior; no rollback or
atomic directory transaction is included in this design.

Worked selection: a native frame may contain an independently shortened `Widget field;`
and native `using global::System.ClientModel.Primitives.PipelineMessage message = Create();`.
POC acceptance selects `using PipelineMessage ...`; POC denial or malformed POC coordinates
returns the same field-short/native-local base result, never a globally qualified field.
These are captured numeric ranges; no search for either type name is performed.

## 5. Dependency and coverage closure

This table is a proof checklist, not a claim that the listed evidence is complete.

| Dependency | Required producer/evidence | Invalidation / current gap |
| --- | --- | --- |
| Semantic target and lexical scope | Existing customization/reference symbols and occurrence-owned lookup facts, prepared before writing. | Captured name/arity/owner and type-parameter competitors must match the admitted role. Full advanced-role mapping remains pending. |
| Source observability | Existing source-policy/caller-text facts for this occurrence and ancestors. | Capture/unknown policy vetoes; cannot infer purity from unchanged final text. |
| Native selected type and identifier | Observation at actual standard formatter/declaration consumption; admitted revisions as needed. | Parameter ABA and delayed-notification anchors remain mandatory. Snapshot equality does not prove no ABA. |
| File imports/aliases | Pre-write visibility proof plus native namespace-registration receipt. | A late conflicting import vetoes the affected plan; a short choice must not introduce new import side effects. |
| Candidate-file lexical/role confinement | Exact structured writer/IR contracts for headers, declarations, body boundaries, wrappers, raw leaves and directives. | Current stream receipts are insufficient; unknown raw/opaque escape remains a veto. |
| Other generated declarations | Namespace/type-export inventory plus native provider/type identity receipts. | Other files need proof they cannot introduce unmodeled declarations/global imports. Do not confuse this with proving every expression's binding. |
| Expected written provider set | Owned root/session and actual complete native yield/write stream. | Removal-set prepayment/currentness is unresolved; omitting competitors needs an independently sound decision witness. |
| Additional files and scaffolding | Preflight contract for the actual receiver, inputs and nested effects through auxiliary completion, excluding relevant changes to provider output, compiler inputs, source/reference/configuration facts and late rewriters; or an explicitly approved lifecycle/trust alternative. | `WriteAdditionalFiles` runs after provider output. Unknown virtual overrides/scaffolding cannot be certified by the provider receipt or a no-C# filename claim. |
| Root/serialization/member ownership | Exact admitted carriers, revisions and native consumption where needed. | Prepay-then-legacy checks and unobserved aliases need audit. |
| Headers/docs/warnings | Approved canonical header boundary and native materialization observations. | Honor approved canonical behavior; do not reintroduce rebuilds. Retry/child-limit classifications must be consistent. |
| Shared source/reference/configuration facts | Existing trusted source proof and complete snapshot dependency contract. | New policy or stale source denies dependent plans; do not broaden host trust or replay callbacks. |
| Resource/lifetime ownership | Generation-owned ledgers, finite actual algorithms and terminal observer release. | Registry lookup/scans, shared copy windows and in-flight failure accounting remain open. |

### Dependency boundary, not universal certification

The design must prove the boundary, not assume either extreme:

- Generation-wide facts cover possible exported competitors, shared source/configuration,
  native provider decisions and cross-file lexical escape/global-import effects.
- Candidate-file facts cover its imports, lexical/scope boundaries and relevant emission.
- Occurrence facts cover target, role, source policy, construction and exact consumed use.

A noncandidate file does not automatically require expression-level semantic proof.
However, declaring it irrelevant requires an exact export/lexical-confinement contract;
unknown raw output can invalidate that claim. Existing body receipts cannot certify this.
If closing the first Foo slice needs a general language-emission framework, stop and
present the smallest missing emitter contract and alternatives, as the handoff requires.

### Auxiliary-output admission

`CSharpGen.ExecuteCoreAsync` calls `WriteAdditionalFiles` after writing provider files and
optionally executes project scaffolding. Inspect those paths before the first output
writer. The exact current `ScmCodeModelGenerator.WriteAdditionalFiles` produces schema
JSON and optional `.NuGet.targets`; that source audit supports a candidate no-additional-C#
contract for this exact implementation, not for virtual overrides or arbitrary future
MSBuild behavior. The contract must cover the actual later receiver, configuration, nested effects and
output paths from preflight through completion of auxiliary work. It excludes new C#,
modifying provider files already written, changing relevant source/reference/options,
registering late rewriters, or targets/scaffolding that inject unmodeled compiler inputs.
An exact outer method identity alone does not establish those properties. A preflight
witness must remain valid over that interval; no late recheck after publication can retract
already shortened files.

The compiler-host branch skips `WriteAdditionalFiles` and rejects hosted/new-project modes;
its existing trusted-caller restrictions still apply. Normal generation cannot borrow that
fact. If a path's effect/ownership interval cannot be proved, deny POC for that path before
writing. Do not move callbacks earlier, run them twice, or add a transaction to close it.
An accepted path requires a finite contract or an approved lifecycle change.

Scope correction: the authoritative Task04 contract separates input admission and broader
activation from the rule rewrite. Unrestricted normal-CLI auxiliary support is not an
extra first-milestone requirement. Map the unchanged original Foo/pipeline anchors to
their existing admitted host first. An existing admitted mode that skips auxiliary work
may discharge that part of its closure; this does not authorize a new compiler host,
a substitute fixture, or silent reassignment of an accepted normal-entry anchor.

## 6. Observation and resource protocol

### Results and stickiness

| Result | Point query | Required native observation / final seal |
| --- | --- | --- |
| Match | Return evidence for this observation, with its owner/epoch. | Advance receipt/accept only the covered dependency. |
| Mismatch | Record a genuinely observed, funded violation as sticky where required. | Reject the affected receipt/plan; restoration cannot erase it. |
| Unavailable | Return no evidence, without poisoning independent funded queries/siblings. | Missing required evidence fails closed for this write/seal. |

Unavailable reasons include insufficient parent or child funds, unsupported carrier,
detected concurrent enumeration invalidation, or missing provenance. Not every false
helper result is a funded mismatch. Do not manufacture `Exhausted` to represent other
unavailability, and do not store an observation's result as a reusable budget-level bit.
Native exceptions remain a separate native control-flow outcome.

### Actual-work rules

1. Reuse the existing phase limits: 1,048,576 generation, 262,144 recipe, 65,536
   supplemental, 4,096 envelope, and existing separate limits. Moving ownership must
   not mint a fresh full allowance. Charge linked child work according to the existing
   ledger contract; limits are ceilings, not permission to double-count capacity.
2. An exact mutable list can only be observed using a fixed paid bound and admitted
   operations. Never follow reservation with `ToArray`/legacy enumeration of its new size.
3. Recursively compare POC type/currentness facts using the same bounded operations;
   funding a wrapper does not bound shared `IsCurrent` or `ContractCollections.TryCopy`.
4. HashSet observation needs a real internal-scan/capacity bound over the actual read.
   Earlier capacity plus later legacy traversal is not sufficient. No extra size check
   can prove the unseen mutation window closed.
5. Lookup before reservation, unowned process-history tables, native monitor contention
   and repeated bucket scans are not fixed by a post-hoc budget debit.
6. State every cost including capture, comparison, allocation/copy, dictionary collision
   work, notification/help/detach and disposal. A fixed per-entry cap does not fund an
   arbitrary number of calls. Native work that already exists and additional Task04 work
   must be distinguished explicitly; no unapproved new runtime/ingress exception.
7. Budget ownership must itself handle supported competing calls; do not assume a mutable
   ordinary `PocWorkBudget` is thread-safe. Atomic claims are not a proof of all retry or
   in-flight ingress costs.

### Output-work algorithm and exhaustion

Let `L` be the native primary length, `Q` the native qualified length when present,
`A` the legacy range count and `U` the POC use count. The first slice permits no nested
POC ranges. All counts/lengths are observed scalar values, checked against existing caps;
no full string scan is needed to learn them.

- Before recording, pay for fixed-capacity POC metadata up to the admitted plan count,
  including its capacity/allocation cost. If unavailable, attach no POC recorder. Do not
  grow an unbounded list while formatting. Fund descriptor comparisons before reading
  variable-size children; observe native reads rather than adding enumeration.
- Native buffers and established field/body selection remain the baseline behavior.
  POC does not eagerly clone a full file while its final length is unknown. Its separate
  metadata can be discarded without scanning or reconstructing native output.
- After native length is known, reserve validation/coordinate work proportional to
  `A + U` and any required owned-descriptor comparisons. Only a paid validation may
  admit a POC view. Bound all loops and capacity changes, not just expected-case lookup.
- Before each POC-introduced materialization, reserve allocation, copying and final string
  construction for the entire resulting length, plus metadata traversal. Shortening cannot
  increase length; `max(L, Q)` is a safe size ceiling. A `StringBuilder` with hidden growth
  is not the algorithm: use a pre-sized/fixed render destination with an audited final
  string construction/copy cost. Exact runtime tariffs remain an F3 audit obligation.
- At most one tentative raw POC view and one differently selected final POC view may be
  materialized per file; exact-vector reuse avoids the second. Charge their separate
  reservations against the same owning ledger, not fresh per-file full budgets.
- Underfunded tentative rendering returns native raw content; underfunded final POC
  rendering returns `BaseResult`. This may deny the positive on large inputs, but must
  still admit unchanged real Foo within existing caps. That positive needs actual evidence.
- Rejecting POC introduces no output-sized restoration work. Existing legacy rendering
  is not magically free: attribute only the unchanged baseline work to its existing path,
  and explicitly fund any scan/copy added by the adapter. If that distinction or a helper's
  bound cannot be established, F3 stays blocked.

This chooses a rejection algorithm and allocation ownership; it does not claim that
fusing work into `ToString` or retaining two buffers proves a total resource bound.

### Open helper decisions: preferred direction and hard stop

| Area | Preferred direction | Stop / evidence needed |
| --- | --- | --- |
| Shared-reference and type-currentness copy | POC-specific fixed-bound observation with explicit result propagation; preserve shared native entry points. | Audit all sticky callers and recursive helpers; stable exact/one-short/mismatch/restored-retry positives must survive. |
| Removal decisions | Prefer the existing preplanned expected write/export set and complete actual native stream/export receipts; avoid POC enumeration of the live removal HashSet. Preserve native policy calls/timing. | Conditional artifact authority is not final live-set equality. Preserve every original removed-competitor/currentness control; stop if a verified regression requires stronger semantics. Do not add a new ownership subsystem or empty-only veto by default. |
| Candidate inventory/per-write scans | Prepare bounded ownership/index facts once; use exact actual owner/plan at consumption. | Must retain unused competitors and changes; a cache that memoizes freshness is not allowed. |
| Construction witness lookup | Fund before lookup; require bounded generation-owned or directly owned provenance access. | Current global weak-table history is not automatically bounded by current generation entries. Ownership/lifetime changes need explicit assessment. |
| Native context registry | Preserve the already approved post-map canonical-context behavior; avoid making late native recording an unbounded POC service. | Honor actual native callers and early carrier coverage. Any remaining recording needs a concrete bounded ownership/concurrency design, not just replacing a lock with CAS. |
| Supplemental child checks | Explicit observation-local propagation through real callers. | Reuse valid doc-child evidence; the separate segment-child concern is suspected until faithfully reproduced. |

Do not start repairing each row independently before establishing that the combined
protocol still admits the real Foo anchor. A collection of local fixes is not an
end-to-end feasible design.

## 7. Feasibility gates before production changes

| Gate | Required exit evidence | Stop condition |
| --- | --- | --- |
| F0: specification | Map the unchanged Foo prepared/raw/pipeline anchor and required retention controls to actual writer slots. | A substitute fixture, legacy-only reduction or expectation weakening is needed. |
| F1: finite coverage | Enumerate required exact writer/IR components and identify existing versus missing contracts, including other-file exports. | Unknown behavior requires a general parser/emitter framework, new host or arbitrary callback support. |
| F2: freshness/lifetime | Show each relevant mutation is covered, specify seal linearization, competing failure/disposal and release. | Only final equality, construction provenance, serialization or locking makes the argument work. |
| F3: bounded work | Under approved B, document each qualifying entry site and transitive fixed tariff, and each variable operation's actual bound/ledger owner. Internal loops/recursion remain paid; storage/cleanup are prepaid. Retain original positives. | An unaudited ingress path, unbounded traversal/global history, unpaid allocation/runtime work or missing ownership remains. |
| F4: complete design review | Review the stable design and all required component contracts; assess findings against code. | Any requirement is satisfied only by a certificate name, asserted trust, or incomplete review coverage. |

Current gate disposition:

- **F0: partially mapped, not freshly executed.** The unchanged Foo test, declaration slot,
  formatter and selection sites are identified. The actual pipeline anchor's existing host/
  entry mode and applicable retention controls still need mapping before F0 is complete.
  Do not substitute another fixture/source authority. Current-tree execution is deferred
  until an approved coherent implementation exists.
- **F1: open.** Existing raw-name inventories/traversal receipts do not provide the required
  lexical/export and candidate-file contracts. Auxiliary outputs now have explicit admission
  requirements; no generic override/scaffolding approval is claimed.
- **F2: open.** Native-value recording and the lifecycle above select the protocol direction,
  but alias/ABA coverage and its atomic ownership/validity interval need source-level closure.
- **F3: B accounting approved; implementation obligations open.** Each native/protocol
  entry needs the bounded-overhead manifest and a transitive fixed tariff; variable work,
  storage and cleanup retain existing limits. Shared-copy/registry/global-history paths
  are not made compliant by approval. Observation-local results remain required.
- **F4: reviewed as an architectural proposal, not closed for implementation.** One bounded
  stable-document review conditionally endorsed the direction and identified six required
  corrections: recorder/mixed-channel isolation, auxiliary timing/effects, mutation/leases,
  output-work bounds, partial publication, and a finite design-work budget. Those corrections
  are incorporated here but are not claimed independently revalidated. The bounded-resource
  feasibility delegate reached its 70-call limit without a committed answer; no approval is
  inferred and it was not restarted. Neither review is exhaustive implementation sign-off.

The design is therefore **not yet implementation-ready**. Its purpose is to find a small
explicit blocking decision before spending more time on production infrastructure. Do not
claim Task04 permission or flip `HasUncoveredEmission` while a required row is open.

### Finite Foo closure deliverable and inspection budget

The single closure deliverable is the section 5 dependency table completed with **exact
writer/export component, actual read/mutation boundary, owned lifetime, qualifying entry
and fixed tariff, paid-work bounds, accept/deny rules and evidence reference** for each
required Foo row. B does not require an absolute cap on independently arriving entries;
it does require their sites/constant paths and all variable work to be bounded.
No additional proof framework or new family may be introduced to complete the table.

The lead design agent owns F1-F3 and the integrated table; independent review owns F4,
not unbounded implementation discovery. For any explicitly resumed closure pass:

| Gate | Owner | Maximum source-inspection allowance | Required stop output |
| --- | --- | --- | --- |
| F1 coverage/auxiliary effects | Lead design agent | 6 named files / 15 minutes | First unprovable emitter/export/effect contract and affected positive. |
| F2 mutation/seal | Lead design agent | 8 named files / 15 minutes | Aliased dependency with no valid boundary, or unresolved state/epoch transition. |
| F3 actual work | Lead design agent | 6 named files / 15 minutes | Specific unbounded helper/ingress/output operation and missing owner/tariff. |
| F4 review | One independent reviewer | One stable document plus 5 targeted files / 15 minutes | Required corrections and explicit incomplete areas, not a fresh repository audit. |

The total resumed source-discovery allowance is 45 minutes and 20 named files across
F1-F3; delegating or renaming a question does not reset it. Required cross-references count
against the same allowance. Existing faithful evidence is reused. No automatic retry or
new pass follows exhaustion. The completed review pass has already exposed the open
contracts recorded here; this document does not schedule another pass by default.

If a row cannot close within the allowance, stop and present a decision record:
current invariant, concrete source gap, smallest additional contract/lifecycle change,
within-scope alternative, positive-coverage cost, and exact approval needed. In particular,
leases cannot manufacture alias ownership and a global table cannot manufacture a per-
generation bound. An empty-only collection veto or a different host/source fixture cannot
silently replace original nonempty/Foo/pipeline anchors. Do not add unrestricted default-
host admission as an unstated prerequisite either.

## 8. Bounded experiments: authorize a question, not a search

Start with read-only code analysis and existing faithful repros. An experiment is justified
only when its answer changes one named design decision and existing evidence cannot answer it.
Record before execution:

- gate and exact hypothesis; native method and unchanged fixture;
- positive prerequisites proving the path is genuinely admitted;
- baseline/candidate distinction, actual measured facts and what they do not establish;
- pass, reject and inconclusive outcomes, plus next action for each;
- isolated artifact location, build ownership, command, expected time and run budget.

Default experiment budget: one baseline/candidate/control matrix and at most one fixture
setup correction. Stop on a second setup failure, on reaching 30 minutes wall time, or
when the hypothesis is answered, whichever is first. A known longer test requires its
explicit timing budget before launch. Increasing the budget requires a stated reason,
not another automatic iteration. Never overlap builds sharing artifacts or put diagnostic
hooks into production. A faithful existing red need not be reproduced again unchanged.

Candidate experiments, **not scheduled; select only when a named design decision needs them**:

| Question | Existing evidence / action | Acceptance or stop |
| --- | --- | --- |
| Does the fixed-bound shared comparison preserve observation-local retry? | Existing real-list baseline and scratch prototype; first finish caller/result design on paper. | Exact/short/funded-mismatch/restored-retry controls and growth rejection; no budget-wide marker. |
| Can expected/actual native decisions replace live-set enumeration without losing an original retention? | Use existing expected write/export facts and native receipts; reuse the growth red. | Keep every original expectation. If a verified retention requires final live-set semantics, stop with that contract difference; do not weaken the test or retry enumeration variants. |
| Is the separate supplemental segment child result misclassified? | One faithful admitted-segment baseline with currentness prerequisites, only if needed by F1/F3. | If prerequisites cannot be established within the setup budget, record inconclusive and stop. |
| Does the finished seal/failure protocol cover existing interleavings? | Reuse unchanged parameter ABA/delayed-notification/concurrent-start anchors. | No stale reattachment, duplicate unpaid failure work or native/shared regression; source reasoning still required beyond tested schedules. |

Do not execute the full matrix merely because it exists. First select the design option;
then execute only experiments needed to disambiguate its remaining feasibility question.
No number of successful schedules proves arbitrary concurrency or a total resource bound.

## 9. Settlement and reopening rules

A component may be marked **settled within its declared boundary** when:

1. its contract, assumptions, unsupported cases and ownership are explicit;
2. every required proof obligation has source/algorithm evidence, not just a test count;
3. original positives plus paired retention/failure controls pass on the relevant revision;
4. independent review has no unresolved verified finding in that boundary; and
5. the design/behavior has the required approval, recorded with evidence and source identity.

A settled component is reused. Do not redesign or rerun it because a new hypothetical
sounds similar. Reopen only for a concrete counterexample, a changed dependency/assumption,
a changed runtime/input contract, or a newly approved coverage expansion. Record exactly
which evidence was invalidated; rerun affected checks, not every historical experiment.

Stop an approach immediately when it requires forbidden replay, a new broad host/framework,
raised caps, a locking/serialization assumption, or weakening an accepted anchor. If two
candidate approaches hit the same unresolved ownership/contract gap, escalate that gap
instead of inventing a third variant. A stop report names the evidence, smallest missing
contract, alternatives, positive-coverage cost, and precise user decision needed. Continue
independent authorized design work; do not silently reinterpret the task as complete.

## 10. Implementation and validation sequence after design closure

1. **Coherent prerequisite batch:** implement the agreed observation/ownership repairs
   together with all affected result propagation. Preserve the current dirty batch and
   original repro anchors. No new POC authority yet. Validate only implicated helpers and
   real Foo/Task03 admission/behavior controls.
2. **Foo vertical slice:** prepare the actual role's authority, record the real native type
   formatting once, attach its prepared alternative, close auxiliary-output admission,
   seal it and select the POC channel. No changes to Task03 admission.
3. **Milestone evidence:** unchanged `RealFooUsingDeclarationWritesPreparedPipelineMessageType`
   succeeds in raw output; pre-write decision and actual CSharpGen pipeline evidence agree.
   Compare native binding/diagnostics and source/runtime observations as applicable.
4. **Retention/one-write evidence:** source shadow, caller text, import conflict, mutation/ABA,
   delayed failures, early/late rejection, budgets, exceptions and normal/exceptional release.
   Confirm native getters/callbacks/carriers/order and independent shared/field choices.
5. **Stable checkpoint:** required affected broader controls, all-39 stream, original aggregate
   controls and expanded frozen-source Core/Client checkpoint. Refresh affected source/binding
   comparisons. Exhaustive independent review of the complete stable change set and follow-up
   on verified findings are mandatory before a validated checkpoint commit/push.
6. **Later Task04 slices:** common declarations, shapes/prefixes, expression operands and
   advanced certified roles, each through the same protocol and unchanged acceptance map.
   If a slice needs materially new emission infrastructure, apply the existing scope gate.

A useful first milestone does not complete Task04. SDK regeneration, public GitHub replies,
thread resolution, force push, default activation and a performance campaign remain outside
this design authorization. Do not commit the existing unvalidated batch merely to publish
this proposal.

## 11. Design-review record and validation limits

- Feasibility inspection `d89cfca2324a88fc8ddc0e70b1856bb0e` identified the absent permission
  path, need for actual native fallback capture, scope/import/currentness gaps and auxiliary
  output admission. These are code-grounded design obligations, not current-tree test results.
- Resource investigation `d0d24cb16da3d471bab5d6c22c90d3fd6` exceeded its 70-call limit and
  returned no committed answer. It was stopped, not extended or treated as approval.
- Bounded document review `df6f33fceff25c9fc492bd2f6471b0fbd` conditionally endorsed the
  architecture and raised the six corrections listed under F4. The lead independently
  checked those findings against the actual native declaration/formatter, `CodeFile` and
  `CSharpGen` paths and incorporated them in one document-only batch. Revised contracts
  are proposals, not an independently validated implementation or a closed F1-F3 proof.
- No production source, existing tests or regression expectations were changed. No builds,
  tests, experiments, regeneration, commits or pushes were run. Local Markdown links and
  whitespace were checked; the pre-existing tracked diff and four intended untracked tests
  retain their recorded fingerprints. These checks do not prove the proposed algorithms.

## 12. Bounded closure pass

The user subsequently authorized a fixed source pass. Both delegates completed within
their limits; the lead completed the resource pass. No production changes or experiments
were made. See the [closure report](./pre-emission-task04-write-once-closure.md) for the
selected algorithms, corrected admission scope, precise remaining resource decision,
source boundaries and gate disposition. That report does not grant a resource exception
or mark all Foo emission templates implementation-ready. Document integration review
`df053480c6601259f813513d373d55a85` completed; its four consistency corrections were assessed
and applied in one documentation-only batch. This is not a second source audit, completed
Foo proof, or implementation sign-off.
