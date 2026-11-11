# Task04 Foo input freshness investigation

This is an investigation, not a new admission rule or an implementation plan
approved for the input layer. See the [acceptance map](./pre-emission-task04-acceptance-map.md)
and [handoff](./pre-emission-task04-implementation-handoff.md). Foo's raw local
regression is still failing; current production does not admit Foo for local
shortening. No input-layer or assembly-access change has been made.

## Real Foo evidence

The existing ClientModel input produces 38 top-level output providers. For Foo,
the observed path is not pinned, its description, type-documentation and file
warning caches are unmaterialized, and its input has empty summary/doc, a parent
and no experimental details. Cached write attributes and interfaces are owned and
empty; the custom-code view is materialized and absent. The actual provider
and standard writer still have separate header, member and import obligations.

`RealFooInputCanChangeAndRestoreWhileLazyDocumentationCapturesIntermediateValue`
shows `Doc` can change, be captured by canonical lazy type documentation, and
be restored while `PreparedSourceFile.OwnerSnapshot` remains current. This is a
limitation of that owner snapshot for a **proposed** input-dependent permission,
not an existing incorrect Foo reduction.

A separate test with **non-null, test-injected** experimental metadata shows an
owned `List<string>` behind `DependsOn` can change and return to its old contents
while `DisabledFileWarnings` caches the intermediate value and the owner
snapshot remains current. Foo's measured experimental input is null; this
control must not be presented as a property of that input.

Five real Foo writer tests supplied CR, LF, NEL, line separator and paragraph
separator in the documentation input. Each kept the attempted class declaration
inside comments. They establish those examples only, not a universal proof or
an actual-pipeline admission.

## Input dependencies for this header

| Surface | Relevant dependency | Boundary |
| --- | --- | --- |
| Lazy type description | `InputClient.Summary`, `Doc`, and, for the empty-description fallback, whether `Parent` is null; fallback spelling uses the provider's `Name`. | `Description` and `XmlDocs` are separate caches. A cache may predate capture, so present input values alone do not establish its provenance or safety. |
| Lazy file warnings | `InputClient.Experimental`; when non-null, `InputExperimentalDetails.DependsOn` is enumerated to create suppressions. | Details are sealed but `DependsOn` is an `IReadOnlyList<string>` stored by reference; it can be mutable or callback-bearing. Initially retain non-null experimental cases unless an independently owned immutable representation is proved. |
| Attributes | ClientProvider's builder reads custom-code attributes and `Experimental.DiagnosticId`. In this Foo fixture the standard write-attribute cache is already owned and empty, and base dispatch uses the cache. | Capture the actual cache and dispatch; do not infer safety from current input or admit rebuild/unknown override paths. |
| Name and path | `BuildRelativeFilePath` uses the provider's cached `Name`; provider/type identity, custom/partial scopes and path currentness are separate from `InputClient` freshness. | An observed path is not a pinned path. Bind the actual `CodeFile.Name` and ownership receipt; do not freeze or invoke the virtual getter merely to certify it. |

`InputClient.Update` is public and mutates direct properties; its collection
arguments can execute arbitrary enumeration and can throw after earlier scalar
assignments. `InputClientConverter` uses internal setters, including
`Experimental`, and `TypeSpecSerialization` directly updates client names.
Versioning only `Update` misses those paths. The parent object is used only for
nullness in the fallback description; the rest of the file can nevertheless
have independent nested input dependencies. A client revision is not a version
of the entire provider or input graph.

## Epic direction: materialize, then prepare, then emit

Prefer deriving reduction facts from the actual finalized/materialized provider
and IR objects, after legitimate materialization and **before** the single
writer pass. This applies across the epic, not only Foo. It does not mean parsing
or binding generated C# output to select reductions. The plan must preserve
canonical identities, source-observation facts, callback-once behavior and
sticky same-write fallback. If required objects are currently lazy, first assess
an explicit bounded materialization/lifecycle contract: changing when they
materialize may change callback order or behavior. Do not force arbitrary getters
or assume an object has already been materialized. Input freshness tracking is
needed only for dependencies that remain relevant after the actual emitted
objects and their lifetime are accounted for.

A diagnostic enumeration of the real finalized Foo fixture's actual planned
`CSharpGen.GetWrittenProviders` sequence places Foo **19th of 39 written
providers** (18 precede it; the fixture has 38 top-level input providers).
A follow-up enumeration finds the two same-name/same-namespace Foo parts
consecutively: `Foo.cs` at index 18 and `Foo.RestClient.cs` at index 19 (zero
based). This identifies a minimum partial group, not proof that no other file or
source is related. A later source-graph-only diagnostic found opaque nodes in
all 18 preceding captured provider files, including documentation and custom
provider/wrapper boundaries. They are not automatically competitors, but their
actual impact on the selected lookup cannot be dismissed merely because they
have other names. None of these diagnostics wrote or inspected generated output
or invoked type-doc/warning builders. A global eager materialization phase could move Foo's builders ahead of those
preceding writers, changing their observable interaction; fallback cannot undo
an early callback. **Current requirement:** the user clarified that the POC's
decisions must be precomputed generation-wide before any final output-provider
writer starts. The user explicitly excepted the in-memory attribute bootstrap;
how to reduce those attributes in final output remains later work. Earlier
per-file/group and buffered-prefix experiments below are historical evidence,
not an approved decision boundary for the current plan. No emitted output is
inspected or rewritten to choose reductions. Unknown dependencies or callbacks
must retain. Any pre-write materialization still requires a bounded equivalence
proof; input, source
and shared snapshots and final staged commit must remain current. A controlled
comparison using separate real Foo generator instances passed with identical
raw `Foo.cs` content and the same canonical docs/warnings objects reused after
manually materializing the second instance before preparation. The original
writer-order controls passed for XML enabled/disabled, omitted internal type
docs, and callbacks in earlier and related partial files. In particular, a
related partial's callback can change the next partial's lazy documentation;
unknown dependencies must retain rather than hoist that callback. This is
representative fixture evidence, not a production lifecycle, a related-group
closure proof or a raw shortening.

The current standard writer writes its namespace/opening-brace prefix (and,
when present, warning directives) into its private `CodeWriter` before the
normal type-documentation getter runs. Earlier work explored holding that
**unpublished, uninspected** private prefix across group preparation. The user
has since clarified that decisions must be computed before any final output-provider
writer starts, so that pause is not a valid decision boundary for the current plan. The
existing same-pass qualified fallback may capture a companion variant. The
unused internal, exact-standard-writer `WriteWithMaterializedHeader` experiment
pauses at that natural getter and passes only the canonical provider
and documentation objects (not the buffer), and resumes in the same call. The
future group callback must be read-only with respect to the provider and emitted
objects; replacing documentation or mutating callbacks cannot be repaired by a
qualified-name fallback. It is single-use and is not connected to production
group planning yet. Focused
writer lifecycle controls passed 11/11; three ClientModel controls passed,
including raw Foo/qualified-companion parity and an unstarted REST cursor at
the pause. A focused independent safety review identified a session-replacement
hazard: a writer attached to session A could consume a field decision from B and
miss a later conflicting import. The issue was reproduced (`CS0104` under an
incorrect B-based selection), and the paused writer now pins its initial session
before the prefix; it rejects late replacement/attachment. Focused controls for
session pinning, nonempty warnings, active prepared field/body uses and sticky
qualified fallback passed. A focused checkpoint passed 43 core and 8 ClientModel
tests before the subsequent concurrency/late-import test additions; it is not
a final or full-suite gate. A focused independent follow-up found no remaining
concrete session-ownership defect. An explicit opt-in prospective factory now
retains a fieldless generation lifetime before the prefix without granting name
permission; the default source-only path remains unchanged. Independent review
found that placing prospective owner snapshots in the shared channel could veto
an unrelated prepared field; the ordinary path retained that field but the
prospective candidate did not. The revised design keeps a separate sticky
prospective-owner snapshot fact, not a shared field/body veto or group permission.
A competing attachment is also rejected rather than returned as a disposed but
non-null session. The focused review-fix run passed 12/12, including both default
and prospective independent-field controls, owner revision change-and-restore,
competing attachment, missing/stale physical input and deep-body independence.
A retained mutable nested array can change and restore without changing the
owner revision, as confirmed by a test-only diagnostic; owner snapshots alone
are therefore **not** proof of complete group currentness. The focused
follow-up found no remaining concrete defect in those fixes and confirmed this
limitation. The prospective factory now captures an already-materialized `OutputLibrary`
root reference and a saturating assignment revision, without executing its
virtual builder; this root-reference fact is separate from the existing
field/body commitment channels and is not group permission. An exact `List`
root also retains a copied enumerator version; focused tests detect
change-and-restore through version-incrementing `List` operations during or
after creation without changing the visitor-visible list. This does not detect
mutation through `CollectionsMarshal.AsSpan`, establish safety for other mutable
carriers, or address version wraparound or concurrent mutation. The actual
no-extra-visitor Foo fixture uses a `TypeProvider[]` root, so the list witness
does not establish Foo root membership safety. Fieldless creation rejects stale
prospective facts and releases attachments; where an independent field/body
choice already exists, prospective failure remains separate. The dormant
no-decision path grants no shared commit and no local choice. It preserves the
baseline callback counts (write policy 1/1; path/declaring getters 1,3/1,3)
after both regressions were reproduced. Focused root/creation controls passed
11/11; the real-Foo opt-in lifetime and unchanged paused writer passed 4/4. The opt-in
factory and pause are not yet used by a production group caller. Follow-up review
also identified an integration requirement: a fresh root witness alone does not
prove it is the root already used by the active reference-map/enumeration phase,
nor that an arbitrary supplied `OutputLibrary` is the active output. An explicit
foreign-output and self-consistent unowned-phase regressions were reproduced
and now reject. A prospective snapshot without an owned phase remains
non-authorizing; its isolated tests do not represent an active generation. Production
activation must bind the exact pre-existing phase/output/root identity without
re-invoking virtual getters or replaying reference-map callbacks. Review also
found that the reference-map session delegates to a static analyzer result:
future activation must detect reentry/reset and bind the actual session/result,
not only the output-root reference. The source-proof callback is also reentrant; creation now rechecks prospective
root facts after its final invocation. Its reference/list mutation controls,
including independent field and selected body-name results, passed 6/6 in a
focused run after the ordering fix. The current witness starts only after the
virtual source-model getter and initial source-currentness callback, not at
factory entry. A later review reproduced a final source-proof callback leaving a
different generator current; the prospective-only lifetime now rechecks and
sticks that observed mismatch, while independent field decisions remain
separate. An unobserved generator change-and-restore inside one callback remains
unproved; targeted validation of this latest correction is in progress. It must create the phase before enumeration
starts and verify the actual enumerated root. One possible follow-up is to
record the actual provider identities traversed by the existing writer and
compare them with the planned generation set before selecting staged choices;
that would observe the input stream, not generated text or a replay. Three
real-Foo diagnostics now distinguish the boundaries: an isolated iterator can
traverse a substituted array element and later see the original array restored;
the actual standard-writer observations reject a change left in place across a
writer boundary; and a controlled temporary change restored within an earlier
writer callback leaves the same provider stream while that callback sees the
changed root. In the last diagnostic the existing field/shared commits and the
earlier source receipt remained current. This uses a test-only callback, not an
assertion that ordinary Foo generation already produces wrong output. A
separate controlled provider getter reproduced nested `Analyze` during an outer
analysis; the outer analysis then installed a fresh final static result, hiding
the nested result from an endpoint identity check. A separate nested-session
diagnostic showed the outer session's write predicate change after the inner
session disposed. An opt-in activity-entry witness now detects those observed
analyzer calls and fails closed at saturation, without replaying preparation:
reference-map tests passed 89/89 (controlled provider getter counts 28/28), and
the real-Foo writer pause passed with the witness. A root-identity observation
now follows the existing opt-in `CSharpGen` route: a private active-generation
owner binds the output/root reference and revision captured before analysis to
the actual analyzer session and the root obtained by the existing deferred
iterator, without replaying either. **It cannot by itself authorize a pre-write
choice:** the iterator obtains its root before yielding the first provider, but
the observation does not prove that no writer has started when it is used, and
there is no pre-write planner at that point yet. The current requirement is to
prepare before any final output-provider writer (apart from the expressly excepted
in-memory bootstrap). A separate owner-only iterator observation
prevents a caller-supplied observation from impersonating that iterator. The
owner is tied to the direct receiver of the existing virtual output getter: a
reproduced A→B→A reentrant generator switch now denies the observation instead
of binding B's returned output to A. This is only direct-receiver/returned-object
identity, not proof that a forwarding getter's returned object has exclusive
provenance. The unowned base-output fallback also rejects a generator that is no
longer current. A covariant getter review concern did not reproduce on this
runtime: both reflected properties resolve to the derived getter, and the audit
did not invoke it; the audit was nevertheless made conservative for all
same-name properties. Focused foreign-output, forged/premature observation and
enabled/disabled real pipeline controls passed 25/25; the receiver-switch fix
and related controls passed 11/11. Even when current, this identity observation
cannot exclude an operation already in flight, establish concurrency safety or
detect unobserved array/content ABA.
These diagnostics do not prove nested mutable carriers, lexical scopes or
source receipts safe. None of these facts
authorizes a name reduction by itself. Group closure, mutable-carrier safety and callback
behavior after actual activation remain unresolved. This clarifies
the pre-emission rule; it does not waive group closure, source cursor, callback
order, currentness, import or other safety gates, and it does not shorten the
Foo local.

## Candidate approaches; no choice claimed complete

1. **Bounded input freshness witness.** A direct-input revision could cover the
   exact relevant setters, including internal setters and change-and-restore,
   with a coherent capture/read protocol and fail-closed overflow. It must not
   alter `Update`'s callback/order/exception semantics. Do not hold a new lock
   across arbitrary collection enumeration. A counter read twice is not enough
   by itself: prove capture/commit behavior for in-progress or concurrent writes,
   and define the lifetime through actual emission and commit. Reject non-null
   experimental details for the initial Foo profile rather than assuming a
   parent counter tracks mutations to a nested list.
2. **Exact-builder output envelope.** Investigate proving *before emission*
   that every permitted result of the audited default type-documentation
   builder is confined and has bounded imports, without evaluating unknown
   callbacks or forcing caches. Validation of the same canonical object at its
   natural materialization may serve only as a sticky safety veto if it does
   not create a post-write decision owner. The five line-break examples above
   are necessary evidence, not proof for every input or builder. Warning
   materialization and other written surfaces need separate evidence.
3. **Already materialized, owned caches.** Capture and validate exact cache
   objects before writing, without forcing a lazy getter. This is useful for
   some inputs but does not admit the measured Foo state on its own.

The first approach needs a trusted cross-layer way to read a non-public input
witness: the Input assembly does not currently friend production ClientModel or
core, and core does not friend production ClientModel. Friend access grants the
entire receiving assembly internal access; it is not a security sandbox. A
public/protected provider hook would instead create a plugin-controlled surface.
No boundary or public API change is selected here. The exact trusted producer,
dispatch and assembly identity must be established before executing any new
profile code.

## Before allowing the Foo local

A solution must retain the original Foo raw regression, prove actual
occurrence-owned preparation, and cover direct input ABA, already materialized
versus lazy caches, partial/throwing updates, unavailable or mutable nested
collections, source/input/configuration changes, concurrent mutation or an
explicitly enforced lifecycle guarantee, path and import changes, and sticky
same-write qualified fallback. Test hostile subclasses, custom writers and
callbacks without invoking them for proof. Preserve independent field controls.
Then demonstrate real raw output, native binding/diagnostics and the actual
trusted pipeline path. The file's opaque bodies/wrappers, lexical lookup,
source-observation and other actual imports remain separate unresolved gates;
input freshness alone grants no edit permission.
