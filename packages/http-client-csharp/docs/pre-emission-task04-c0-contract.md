# Task04 C0 — inspection versus writer authority

**Status: C0 DESIGN CONTRACT DONE.** This closes one local design decision, not Task04
choice creation, implementation, the Foo raw positive, or the remaining Foo blueprint.
Selected resolution: **Option A**, preserving the original prepared-positive inspection
query while isolating Task03 writer/store authority.

This decision follows the isolated five-source-file review against snapshot
`.pi/issue-12454/c0-contract-fqlVS2/`, followed by parent assessment of the cited assertions.
Those live sources still matched the snapshot when integrated. Source HEAD was
`2afe54d5de261897d49a182e5eda2c6d3411ba95`, tracked diff SHA-256
`49c38f91188c59343543eda88fb2ad74fe4c26ad56c77537866311890f0f7d8f`.
No production/test edits, expectation changes or test execution were performed to close
this contract. The original prepared/raw failures remain implementation anchors.

Read with the [Foo blueprint](./pre-emission-task04-foo-blueprint.md) and approved
[bounded-overhead contract](./pre-emission-task04-bounded-overhead.md).

## 1. API semantics

| API / state | Contract |
| --- | --- |
| `GetTypeOfNamePermission(file, occurrence)` | Read-only prepared-choice inspection, returning `IPreparedTypeOfNameInspection?`; not an emission capability. The internal legacy method name is retained for the original anchor. |
| Existing Task03 result | When the unchanged Task03 lookup admits a current permission, inspection returns that same concrete object, not a wrapper or clone. |
| Future Task04 result | May return a genuine, separately owned, occurrence-specific prepared choice even when Task03 lookup/store is empty. Source/target/spellings and preparation dependencies must already be established. |
| Diagnostic candidate alone | Never sufficient to return a choice. Do not adapt/promote a proposal merely to make the non-null test green. |
| Null result | No genuine choice, wrong generation/file/occurrence/role, stale/unavailable preparation evidence, or unsupported preparation profile. Task03's single-file exclusion alone does not exclude independent Task04 inspection. |
| `GetTask03TypeOfNamePermission(file, occurrence)` | Concrete Task03-only permission lookup. Preserve its existing admission, currentness and single-file restriction. |
| Standard Task03 writer | Uses only the concrete Task03 lookup and existing consumption/use-registration path, never the inspection interface. |

Sources: [BodyNames.cs:50-66](../generator/Microsoft.TypeSpec.Generator/src/Providers/PreparedFieldTypes.BodyNames.cs#L50),
[TypeOfNamespacePermission.cs:20-90](../generator/Microsoft.TypeSpec.Generator/src/Providers/PreparedFieldTypes.TypeOfNamespacePermission.cs#L20),
[CodeWriter.SourceOccurrences.cs:62-73](../generator/Microsoft.TypeSpec.Generator/src/Writers/CodeWriter.SourceOccurrences.cs#L62).

Inspection must not create/promote choices, populate `_typeOfNames`, start a writer,
consume a native occurrence, or grant final shortening authority. "Read-only" does not
promise a pure getter: existing currentness checks may observe source state and record
invalidation. Preserve those existing semantics and callback/resource restrictions.
Shared invalidation denies dependent choices; a Task03-only admission failure is not
itself a reason to deny an independently valid Task04 choice.

## 2. Inspection properties and pre-write freshness

- `Occurrence`: the exact owned occurrence, not just a matching producer/type name.
- `SimpleSpelling` / `QualifiedSpelling`: prepared spellings for that occurrence and target.
  The qualified spelling is not a replacement for capturing actual native fallback.
- `CanonicalCompilerMetadataTarget`: the canonical compiler target established in preparation.
- `IsCurrent`: preparation dependencies remain valid under the admitted choice contract.
  It is **not** completed native consumption, final seal approval or writer authority.

A genuine choice may be current before any writer starts. Pending native consumption and
commit guards do not make pre-write inspection impossible. Conversely, a non-null/current
view does not authorize emitting or committing its short spelling. Keep future consumption
and commit obligations explicit and separate. A retained stale handle grants no authority;
point-query resource unavailability is not fabricated evidence of a semantic mismatch.

No new lookup/binder, source admission, lifetime or resource exception is selected here.
The actual production query currently delegates only to Task03; genuine Task04 inspection
remains future implementation, not something certified by this document.

## 3. Test contract — preserve behavior, separate the assertions

Test links below refer to ClientModel's `PreparedLocalTypeClientTests.cs` unless noted.

| Anchor | Required disposition |
| --- | --- |
| [Prepared positive:42-57](../generator/Microsoft.TypeSpec.Generator.ClientModel/test/Providers/PreparedLocalTypeClientTests.cs#L42) | Preserve non-null genuine choice, exact PipelineMessage spellings and no writer started before/after inspection. Future wiring should also verify exact occurrence, canonical target and preparation currentness. |
| [Raw positive:25-39](../generator/Microsoft.TypeSpec.Generator.ClientModel/test/Providers/PreparedLocalTypeClientTests.cs#L25) | Preserve unchanged: the actual `Three(options)` using-local named `message` must shorten in designated raw output. Later integration gate, not C0 completion. |
| [Task03 isolation:61-81](../generator/Microsoft.TypeSpec.Generator.ClientModel/test/Providers/PreparedLocalTypeClientTests.cs#L61) | Preserve empty Task03 store, null Task03 lookup, candidate observation and not-started checks. Do not permanently require inspection-null for the same finalized fixture as the prepared positive. |
| [Wrong role:85-100](../generator/Microsoft.TypeSpec.Generator.ClientModel/test/Providers/PreparedLocalTypeClientTests.cs#L85) | Preserve inspection-null for the containing statement; its declaration's choice cannot be borrowed. |
| [Candidate-only diagnostic:2072-2094](../generator/Microsoft.TypeSpec.Generator.ClientModel/test/Providers/PreparedLocalTypeClientTests.cs#L2072) | Preserve candidate presence and qualified diagnostic output. Writer-isolation assertions use the Task03-specific lookup. Inspection-null additionally requires an explicitly candidate-only setup, not an assumption that this constructor can never create a choice. |
| [Source shadow:2142-2190](../generator/Microsoft.TypeSpec.Generator.ClientModel/test/Providers/PreparedLocalTypeClientTests.cs#L2142) | Preserve candidate/target/spelling controls; shadowed source must not gain a genuine bare-name choice. |
| [Supplemental-only:5385-5421](../generator/Microsoft.TypeSpec.Generator.ClientModel/test/Providers/PreparedLocalTypeClientTests.cs#L5385) | Preserve all eight observations, shared opacity/currentness and budget checks. Observation alone is not a choice; distinguish Task03-null from an explicitly candidate-only inspection-null. |
| [Unconfined diagnostic:6205-6222](../generator/Microsoft.TypeSpec.Generator.ClientModel/test/Providers/PreparedLocalTypeClientTests.cs#L6205) | Preserve inventory/not-started and partial-proof denial intent. Do not turn temporary missing choice into a permanent veto against a future separately established genuine choice. |
| [Original Task03 host:29-118](../generator/Microsoft.TypeSpec.Generator/test/Writers/PreparedFieldTypeTests.OriginalTypeOfHostAcceptance.cs#L29) | Preserve raw reductions, canonical targets, caller-text retention, same-instance permission/inspection behavior, receipts and compiler/runtime assertions. |

The apparent contradiction is resolved by specifying different questions:

```text
Does this occurrence have a genuine prepared choice?       inspection may be non-null
Does Task03 grant its standard writer permission here?      Task03 lookup remains null
Was the Task03 permission store populated by Task04?         no
Do candidate/observations alone establish a choice?          no
```

This is not permission to delete every inspection-null assertion. Wrong-role and real
source/proof denials stay null. Candidate-only controls must explicitly retain their
precondition; finalized-header genuine-choice positives must not be reclassified as
candidate-only. The copied `WithRealFoo` helper has distinct constructor paths, but their
names alone are not a permanent guarantee about future choice creation.

## 4. Implementation handoff, not work performed here

C0-specific mechanical follow-up:

1. Keep the existing interface/concrete Task03 split and writer call site.
2. Reconcile authority-only negative assertions to the Task03 lookup. In the finalized
   isolation case, replace/remove the unconditional inspection-null requirement only
   while retaining the direct store/Task03 checks and original prepared-positive anchor.
3. Preserve a deterministic candidate-only inspection-null control. Record the missing
   preparation fact explicitly rather than relying on current incomplete implementation.
4. Preserve same-object legacy inspection and all original source/retention controls.

Apply the focused-implementation regression safeguards before any test wiring change:
compare preserved base/candidate behavior, do not weaken the original positive, and run
only the implicated focused checks first. No expectation or source was changed in this step.

Later work, outside C0: create actual Task04 choices; add their separately owned inspection
lookup after legacy lookup; capture native uses once; validate emission/epoch/consumption
contracts; select POC alternatives. None is needed to settle C0's API/test meanings, and
none is implied implemented by declaring this local contract done.

## 5. Done gate and settlement

**C0 design is done:** API meanings and assertion dispositions are explicit, source-linked
and compatible with both original behavioral invariants. Remaining mechanical test wiring
and genuine choice construction are labeled, not hidden. No unapproved public behavior,
writer admission or input/lifecycle/resource exception was introduced.

The frozen-source delegate completed in 11 tool calls. Parent assessment checked the
actual positive, isolation, wrong-role and candidate/supplemental/unconfined assertions
and source drift. This is a local design review, not test execution, an exhaustive branch
review or implementation sign-off. The overall Foo blueprint remains incomplete.

Reuse this contract. Reopen C0 only for a concrete incompatible source/test requirement,
changed API assumption or explicit new requirement—not because a later capture, emitter,
epoch or resource implementation is unfinished.
