# Task04 Foo vertical slice: source-linked blueprint and stop report

**Status: three local DESIGN CONTRACTS DONE; overall Foo blueprint remains incomplete.**
[C0](./pre-emission-task04-c0-contract.md) closes inspection/writer authority and test meanings.
[Native capture](./pre-emission-task04-native-capture-contract.md) closes exact native reads,
recording boundaries, range/retention/reset handoff and denial/exception behavior.
[Native frame/selection](./pre-emission-task04-native-frame-contract.md) closes retained-output,
projection, failure isolation and paid-view algorithms. These are source-linked contracts,
not implemented hooks or Task04 choice/emitter/alias/epoch authority. The dirty tree is
not validated and POC shortening remains disabled. Source identities and parent assessment
are recorded in each contract; the original investigation below remains historical.

## 1. Concrete anchor and entries

* The direct fixture [PreparedLocalTypeClientTests.cs:25-56](../generator/Microsoft.TypeSpec.Generator.ClientModel/test/Providers/PreparedLocalTypeClientTests.cs#L25)
  uses the actual `ClientProvider` named `Foo`, its `Three(options)` method, the method
  root at index 0 and its `DeclarationExpression` with `IsUsing`. The target resolves to
  `System.ClientModel.Primitives.PipelineMessage`. The required raw change is only
  `using global::System.ClientModel.Primitives.PipelineMessage message = this.CreateThreeRequest(options);`
  to `using PipelineMessage message = this.CreateThreeRequest(options);`; initializer,
  disposal form and symbol identity remain unchanged. The four measured opportunities
  are not four granted permissions.
* `WithRealFoo` [8056-8210](../generator/Microsoft.TypeSpec.Generator.ClientModel/test/Providers/PreparedLocalTypeClientTests.cs#L8056)
  loads `ClientStructureDefault.json`, the runtime and SCM references and a **controlled**
  complete-input snapshot, runs visitors/back-compat/reference-map preparation, then uses
  `CreateForGenerationWithFinalizedPocHeaders`. Its direct `TypeProviderWriter.Write()`
  is not an asynchronous pipeline execution or production-host certification.
* The separate real-Foo actual pipeline anchor
  [PreparedPocGenerationClientTests.cs:22-130](../generator/Microsoft.TypeSpec.Generator.ClientModel/test/Providers/PreparedPocGenerationClientTests.cs#L22)
  runs `CSharpGen.ExecuteForCompilerBuildAsync` with the same model, a linked source,
  SCM/runtime references and a controlled `ITrustedCompilerBuildSession`. It asserts a
  pre-output proposal and owned-stream receipt, **not** short raw or final Foo output.
  The existing host must be extended with output/binding evidence, not replaced by a toy
  fixture. [CSharpGen.cs:316-330](../generator/Microsoft.TypeSpec.Generator/src/CSharpGen.cs#L316)
  skips `WriteAdditionalFiles` for compiler-build mode; its
  [context initialization](../generator/Microsoft.TypeSpec.Generator/src/SourceInput/CompilerBuildGenerationContext.cs#L26)
  rejects hosted/new-project mode initially. The later scaffolding branch re-reads the
  global generator's `IsNewProject`: a stable generator/state would skip it, but this test
  does not directly establish that later observation. That interval remains an explicit
  effect obligation, not a proven skip. This says nothing about normal CLI auxiliary
  callbacks or any broader host.
* **Test-contract conflict requiring a choice:** the original prepared positive at
  [42-56](../generator/Microsoft.TypeSpec.Generator.ClientModel/test/Providers/PreparedLocalTypeClientTests.cs#L42)
  expects `GetTypeOfNamePermission(file, declaration)` non-null; the later Task03-store
  isolation control at [61-73](../generator/Microsoft.TypeSpec.Generator.ClientModel/test/Providers/PreparedLocalTypeClientTests.cs#L61)
  uses the same finalized-header fixture and occurrence and expects that same method null
  after querying the POC candidate. These assertions cannot both describe a stable,
  read-only projection of an enabled choice. Do not solve this by query-order effects,
  storing a Task04 permission in Task03, or silently weakening either behavioral invariant.
  **Resolved locally by Option A:** the read-only inspection query can expose a genuine
  future Task04 prepared choice while Task03 writer/store lookup remains null. Non-null
  inspection never grants emission authority; a diagnostic candidate cannot be relabeled
  as that choice. Assertion dispositions and source references are in the linked C0 contract.
  The finalized isolation test must not permanently require inspection-null for the same
  fixture as the original prepared positive. Wrong-role/source denials and explicit
  candidate-only controls remain. No test expectation was changed to close the contract;
  future mechanical wiring must preserve the original positive and use focused base/candidate
  comparison. The actual choice/native consumer is still absent. The historical isolated
  triad had prepared/raw positives failing and candidate/store isolation passing; this is
  diagnostic evidence, not feature acceptance.

## 2. Source-linked sequence and change boundaries

1. [CSharpGen.cs:176-232](../generator/Microsoft.TypeSpec.Generator/src/CSharpGen.cs#L176)
   owns reference-map phase, finalized headers and the provider iterator. Preparation
   [PreparedFieldTypes.cs:95-105, 150-210, 478-514](../generator/Microsoft.TypeSpec.Generator/src/Providers/PreparedFieldTypes.cs#L95)
   builds shared source files before writers. Extend this existing lifetime with a distinct
   occurrence-owned Task04 choice; do not make a candidate into permission or add a dummy
   field. [BodyNames.cs:22-60](../generator/Microsoft.TypeSpec.Generator/src/Providers/PreparedFieldTypes.BodyNames.cs#L22)
   and [TypeOfNamespacePermission.cs:67-86](../generator/Microsoft.TypeSpec.Generator/src/Providers/PreparedFieldTypes.TypeOfNamespacePermission.cs#L67)
   remain Task03's single-file producer/store. The existing
   [POC candidate query](../generator/Microsoft.TypeSpec.Generator/src/Providers/PreparedFieldTypes.PocOrdinaryCandidate.cs#L966)
   is explicitly a proposal, not authority. Refactor pure lookup facts only as needed.
2. The standard [TypeProviderWriter.WriteCore](../generator/Microsoft.TypeSpec.Generator/src/Primitives/TypeProviderWriter.cs#L84)
   installs source/qualified-fallback tracking, reads warnings, sets namespace, writes
   docs/attributes/type and members, calls `ToString`, and transfers content, path, source
   receipt and alternative snapshot to `CodeFile`. Preserve actual getter/callback ordering,
   exceptions and one native writer pass. Exact standard writer/owner is required.
3. [DeclarationExpression.WriteCore](../generator/Microsoft.TypeSpec.Generator/src/Expressions/DeclarationExpression.cs#L25)
   emits `using`/`out`/`const`, then the type, space and variable. Enter a prebound Task04
   recording scope at the type site without another `Variable.Type` read. The existing
   [source-occurrence helper](../generator/Microsoft.TypeSpec.Generator/src/Writers/CodeWriter.SourceOccurrences.cs#L56)
   currently reads `producer.Variable.Type.Namespace` for Task03 and uses only its permission;
   do not reuse that bypass as the new POC formatter. Pass the already evaluated actual type
   from the normal interpolation to [CodeWriter.AppendType](../generator/Microsoft.TypeSpec.Generator/src/Writers/CodeWriter.cs#L697).
   Record its actual branch/type/name/namespace as native locals are consumed; call
   `UseNamespace` at its original site. Admit initially only the precise non-keyword,
   non-array, non-nested, non-generic, non-nullable Foo type shape. No extra getter,
   `FrameworkType` read that native did not make, argument enumeration or formatter replay.
4. Capture the native type-token boundaries after indentation, excluding `using`, space,
   variable and initializer. [AppendRaw](../generator/Microsoft.TypeSpec.Generator/src/Writers/CodeWriter.cs#L780)
   accounts for indentation and payload positions; [ToString](../generator/Microsoft.TypeSpec.Generator/src/Writers/CodeWriter.cs#L1057)
   constructs the actual license/using header once and shifts payload offsets by its actual
   length. Preserve both native primary and qualified coordinates and exact bytes; no
   guessed qualified spelling. Normal native exceptions propagate and leave no complete use.
5. Keep `CodeFile`'s existing primary/qualified data, legacy metadata, path and source
   receipt as an immutable **NativeFrame**. Add separate POC uses and validate every POC
   range and channel overlap before any POC fast path. The current
   [CodeFile.SelectPreparedNames](../generator/Microsoft.TypeSpec.Generator/src/Primitives/CodeFile.cs#L50)
   defaults `pocAllowed` to false, has an all-true early return, and sends invalid mixed
   metadata to all-qualified output. Those behaviors are not a safe Task04 POC adapter.
   First compute the unchanged legacy field/body `BaseResult`; invalid/exhausted POC returns
   exactly that result. Existing shared failures still retain their existing shared effect.
6. [CSharpGen.cs:232-279](../generator/Microsoft.TypeSpec.Generator/src/CSharpGen.cs#L232)
   stages files and performs final legacy choice before workspace publication. Add a paid,
   no-rebinding Task04 seal after all required actual writes, input and effect checks; select
   the precomputed alternative only for a complete sealed use. Native or shared failure
   does not become a POC-only failure, and POC failure cannot veto independent legacy
   choices. Current raw-writer `Content` needs a separately paid tentative view; final
   selection may use it only if the exact decision vector matches. Workspace processing,
   asynchronous enumeration and disk publication follow selection. A seal alone does not
   guarantee validity until the result is consumed: establish an admitted source/configuration
   interval through publication, or an enforceable host contract that discards the entire
   isolated result if final validation fails. The current final `ValidateResult` can mark
   captured inputs unused after writes; that fact alone does not prove a discard. If neither
   path is established, retain native output and keep this slice blocked.

## 3. Dependency and emission manifest

`Match` = sufficient evidence for this row; `Mismatch` = sticky for the observed affected
plan/file; `Unavailable` = point-query result only unless a required native observation is
missing at seal, in which case that plan is denied. None is permission by itself.

| Dependency/scope | Producer and enforcement at planned owner | Interval, failure and remaining evidence |
| --- | --- | --- |
| Slot/target/use | Planned generation/file/occurrence ID, exact standard writer and role; actual formatter type and branch as above, one native use. Match symbol against precomputed lookup, not a string whitelist. | Until use completion and file seal. Changed/missing/duplicate use denies POC; native exceptions propagate. Current `TryWritePreparedNamespaceSite` is not actual native-use evidence. |
| Lexical scope and imports | Candidate's pre-write compilation/lookup facts, enclosing declaration and source graph; native `UseNamespace` observations, including later members. | Check all affecting imports and exact local scope through physical file completion. Source shadow and later conflicting import tests at [7237, 2679](../generator/Microsoft.TypeSpec.Generator.ClientModel/test/Providers/PreparedLocalTypeClientTests.cs#L2679). Required full grammar and confinement are **not closed**. |
| Source observation and captures | Existing occurrence observation at the exact annotation edge, including enclosing caller-expression/lambda capture, trivia, layout and configuration; paid local query. | From preparation through actual use/seal under existing input-proof contract; unknown or observed change denies POC without poisoning an independent funded query. Do not clear all implicit-operation barriers. |
| Generated competitors, exports and removal | Precomputed finite written-provider/export plan, including accessible source/reference and nonreferenced generated names; native ordered `GetWrittenProviders` yields and exact write completion. | Check expected-vs-actual output artifacts, including serialization/nested export and escaped text. This is not a live removal-set ABA witness. Preserve [removal control](../generator/Microsoft.TypeSpec.Generator.ClientModel/test/Providers/PreparedLocalTypeClientTests.cs#L2238); an unresolved need for stronger live semantics must stop rather than copy a mutable hash set after prepayment. |
| Root/stream identity | Existing output-root setter witness plus actual ordered provider/serialization/native writer receipt. | Covers root reference replacement, not raw element aliases. Changes, unexpected/duplicate/missing stream, uncompleted writer or unknown exported effects deny POC. Keep [root control](../generator/Microsoft.TypeSpec.Generator.ClientModel/test/Providers/PreparedLocalTypeClientTests.cs#L4986). A 39-file receipt alone is not confinement. |
| Candidate-file emission | Exact standard writer skeleton, header envelopes, declaration/statement/expression contracts and actual native child/order receipts. | From pre-write capture through completed writer, including native getters and raw aliases. [RawInventory.HasUncoveredEmission](../generator/Microsoft.TypeSpec.Generator/src/Primitives/PreparedPocGenerationRawInventory.cs#L50) remains true; do not certify from token inventory or clear this guard. |
| Other-file escape/export | For every actual written physical file, certified namespace/type/member export skeleton and inability of any emitted child to escape that skeleton; semantic binding of every unrelated expression is not required. | Through full stream completion. Unknown custom writer, raw template, directive, docs or callback escape denies authority. Exact per-file rows and composition are not established in this pass. |
| Mutable dependencies | Observe values at real native read/consumption; otherwise use a completed, overlap-aware/saturating mutation witness, or an already admitted ownership contract. `ScopedApi.Update` increments revision before assignment; argument carriers and other collections admit raw aliases. | Preparation-to-native-use and through seal as appropriate. Equal revisions/snapshots, construction provenance and participating leases do not cover nonparticipating aliases or ABA. Uncovered aliases deny; no blanket immutability assumption. |
| Input/configuration epoch | Reuse the admitted complete physical-input/source-proof and policy contract, not `SourceInputModel` object identity; check the same source/config validity at required pre-write and seal points. | This pass does not prove that the existing proof covers every concurrent external input mutation across the whole interval. Do not invent a stronger host guarantee; specify and approve one only if the actual contract cannot provide it. |
| Host and auxiliary effects | Actual compiler-build entry skips additional-file callback; context initially rejects new project. Later scaffolding consults global generator state again; the fixture has not asserted that observation or stability. The trusted caller's existing contract is still required for generators that need additional C# files. | Later state/effects must be established through publication for this entry. Normal CLI `WriteAdditionalFiles`/scaffolding are separate effect-closure obligations and are not an added first-slice prerequisite or implicitly safe. |
| Lifetime/disposal | Generation owner retains plan, funded slots, receipt and NativeFrame through final selection; release on normal and exception paths. | Rejection sticky before release, no resurrection, late notification/disposal safe; independent shared state remains usable. Current incomplete worktree's notification code is not sign-off. |

### Named emission boundaries that remain open

The existing [Foo test](../generator/Microsoft.TypeSpec.Generator.ClientModel/test/Providers/PreparedLocalTypeClientTests.cs#L7144)
identifies built-in wrappers including `PipelineResponseProvider`; its `Original` reaches
[ScopedApi.WriteCore](../generator/Microsoft.TypeSpec.Generator/src/Snippets/ScopedApi.cs#L31),
which delegates to the original expression. Four response wrappers are opaque in the
shared graph and wrap `InvokeMethodExpression` with mutable argument carriers
([test 7186](../generator/Microsoft.TypeSpec.Generator.ClientModel/test/Providers/PreparedLocalTypeClientTests.cs#L7186),
[implementation](../generator/Microsoft.TypeSpec.Generator/src/Expressions/InvokeMethodExpression.cs#L107)).
Proven wrapper dispatch is not proof of argument values, child grammar or lexical effect.
`FormattableStringExpression.WriteCore` emits raw literal pieces and consumes holes in
sequence ([source](../generator/Microsoft.TypeSpec.Generator/src/Expressions/FormattableStringExpression.cs#L31));
`IfElsePreprocessorStatement.WriteCore` emits the raw condition and both branch structures
([source](../generator/Microsoft.TypeSpec.Generator/src/Statements/IfElsePreprocessorStatement.cs#L21)).
The prior inventory reports 11 visible interpolations, four directives and eight opaque
Foo witnesses; those counts are not exact compositions or proof that descendants cannot
escape. The missing contract is the actual per-template literal/child lexical-state
transition and export boundary for *each reached instance*, including header, opaque child
and branch effects. It must be enforced at the native consumed values under paid bounds,
with unknown composition denied. We have not established those rows or their complete alias
coverage. A generic parser/purity engine, blanket callback trust or new host is not silently
approved. Stop if the real positive requires one; present its precise scope/cost for approval.

## 4. Records, state and resource plan (conditional)

* `OrdinaryNamePlan`: existing preparation owns generation/file/occurrence and exact role,
  target symbol and precomputed short name, expected native descriptor, source/scope/import
  proofs, expected writer/use and a prepaid slot. It contains no post-write lookup authority.
* `NativeTypeUse`: preallocated occurrence slot holds actual consumed descriptor/branch and
  native-primary/qualified ranges, one completion state and use order. Publish completion
  only after native formatter returns. Mismatch/missing/overflow denies the POC plan.
* `NativeFrame`: immutable legacy content, optional qualified content, legacy alternatives,
  `SourceConsumption`, path/name; owned through selection. Separate POC range vector has no
  nested or overlapping legacy ranges initially. A sealed file decision references the frame
  and immutable admitted per-use vector, never a new lifetime.
* `ObservationResult` is `Match | Mismatch | Unavailable`; a native exception is not
  converted to one of these. Only actual evidence proves Match. Required missing evidence,
  active entry, overflow, inability to fund or uncertainty at seal means no POC selection.
* For **participating** observations, one bounded claim before the covered read/event,
  publish mismatch/evidence before releasing; terminal admission-close and seal each make a
  single attempt, never wait or retry. A losing/concurrent claim vetoes POC conservatively.
  Failure publication precedes observer detachment; a delayed notifier can be helped via
  fixed identity-checked operations and may not republish a disposed observer. Prepay
  cleanup when attaching. Raw aliases that do not participate remain uncovered; this state
  machine does not turn them into witnessed mutations. Exact atomic encoding and proof
  against in-flight failure/notification remain implementation obligations, not solved by
  this pseudocode. Existing [file consumption start/boundary](../generator/Microsoft.TypeSpec.Generator/src/Primitives/PreparedPocFileConsumption.cs#L226)
  and [writer detachment](../generator/Microsoft.TypeSpec.Generator/src/Writers/CodeWriter.SourceOccurrences.cs#L78)
  are partial WIP, not a proven seal protocol.

### Proposed entry and payment manifest under Alternative B

All rows count only actual original-native events or independently initiated protocol calls;
no POC traversal step grants a new `N_entry`. Each row is **unadmitted** until its full
transitive fixed-path bound and cold prerequisites are audited. No numeric `c_i` is invented.

| Entry site / qualifying event | Bounded ingress shape; paid work, storage and cleanup |
| --- | --- |
| `CSharpGen.WriteFiles` native provider begin/end | Prebound state/owner and one atomic fixed transition. Expected-stream preparation, per-provider lookup/compare, storing paths, capacity and stream completion checks use reserved generation ledger work. No callback replay. |
| `CodeWriter.BeginSourceRoot` / `EnterSourceNode` native events | Fixed pointer/phase/slot checks, at most one claim/publish; variable subtree comparisons and entry list traversal paid before access. Unfunded or mismatch denies POC, not shared receipt. |
| `DeclarationExpression.WriteCore` / `AppendType` native type use | Prebound pointer, one slot claim, fixed primitive/reference facts, completion/deny; no extra type getters, reflection, metadata realization or extra argument evaluation. Variable text comparisons and the preallocated slot paid before use. |
| `CodeWriter.UseNamespace` actual native registration | Fixed prebound recording/deny only; any lookup, collision scan, variable string comparison and imported-name storage paid before accessing it. No duplicated registration. |
| Native frame finalization and seal | Native finalization itself is baseline. POC-induced validation of `A + U` legacy/POC ranges, offset arithmetic, allocation and output copying are paid. Terminal seal is a single fixed claim after paid validation, not a free scan. |
| External start/query/failure/release/dispose | One fixed state/owner check and single claim; reason publication and identity-checked one-shot help/detach use prepaid fixed slots. Traversal, cleanup, dictionary accesses and repeated operations are paid; a rejected call cannot initiate unbounded work. |

The existing generation/recipe/supplemental/envelope ceilings (1,048,576 / 262,144 /
65,536 / 4,096) and other existing independent budgets are unchanged. Reserve before
lookup, comparison, traversal, copy, allocation and cleanup; an earlier count does not
fund a later unconstrained mutable list/hash traversal. Use bounded actual-read algorithms,
not legacy helper calls after prepayment. Do not fund construction-witness global weak-table
history or native monitor waits by calling them constant. The current related-type and
shared-reference legacy calls remain known gaps; removal-set growth and currentness repros
are under `.pi/issue-12454/final-review-20261111/repro-{removal-growth,type-currentness,shared-references}/`.

For output, let `L`, `Q`, `A`, `U` be native primary/qualified lengths and legacy/POC
range counts. Pay capacity before recording; after lengths known reserve validation for
`A+U` and render into a fixed/pre-sized destination with an audited final-string cost up
to `max(L,Q)`. At most one tentative and one differing final POC view, each separately
funded; reuse an exact-vector view. On exhaustion return the retained NativeFrame or
legacy `BaseResult` reference without restoration scans. Numeric `c_i`, runtime copy/string
tariffs, exact record layout and offset arithmetic are deferred only until coding/audit;
they must be measured/proved before hook admission and checked at exact/one-short budgets.
Current evidence does not show real Foo fits the caps after all required checks.

## 5. Future validation and order, not performed here

1. **C0 contract resolved:** use the linked inspection/Task03 authority contract.
   Mechanical implementation/test wiring remains separate. Do not edit original positive/
   retention expectations without focused-implementation base/candidate comparison.
2. Complete actual per-instance Foo/other-file emission and alias/source-epoch contracts
   in a bounded source pass. If this requires a new validity/effect assumption, new host or
   broad infrastructure, ask for that precise approval; native retention remains safe but
   leaves the Foo positive pending. Do not make a token-inventory count authoritative.
3. Only after the contracts are closed, implement one coherent slice in the boundaries of
   §2-4. Use existing faithful repros rather than rerunning established unchanged reds to
   rediscover them. Preserve original `Three(options)` prepared/raw assertions and exact
   native symbol/initializer/disposal; extend the actual admitted pipeline test to assert
   raw-before-legacy and final output, binding and diagnostics.
4. Focused matrix: source shadow, caller text, import conflict including later member;
   competing source/reference names, removal/root/stream and serialization changes;
   witnessed mutations, ABA and unsupported raw aliases; exact/one-short/independent
   credits; mixed field/body/POC ranges and invalid POC metadata; native exception and
   callback once/order; competing start, delayed notification, normal/exceptional release.
   Preserve Foo/all-39 and independent field controls. Review tariffication and test budget
   isolation at the final implementation, not just happy-path output.
5. At stable code run required expanded frozen-source Core/Client and applicable actual
   pipeline checks, then one exhaustive independent review and follow-up for verified
   in-scope findings. The old 557/225 checkpoint predates this dirty tree. This first slice
   does not complete Task04, activate default CLI, or replace `Simplifier`.

## 6. Historical pass accounting and remaining work

Inspected **21 distinct implementation/test files**, including targeted searches of
`BuildHostGenerationTests.cs` and `PreparedFieldTypeTests.PocFileBudget.cs`; this exceeded
the handoff's 20-file limit by one. The other targets included the actual-host context,
raw-inventory, consumption, `ScopedApi`, `InvokeMethodExpression`,
`FormattableStringExpression` and directive emitter. Documentation and prior reports were
read separately. No experiment, build, test, regeneration or live source/test edit. Worktree
tracked-diff hash on entry was
`ca87a66568dae6dbd6e92ef6eadf1167d856127a1a0f96ce156b1a33120afe43`.

**C0 is no longer an open design decision:** Option A and test meanings are closed by the
linked local contract; its source/test implementation is not claimed complete. Emission/
alias/epoch contracts and paid closure remain technical blockers and need concrete evidence
or precisely scoped approval, not a blanket policy change. Those later dependencies do not
reopen C0. This remains a partial Foo blueprint, not implementation approval or a complete
manifest. Independent bounded review `de9c6438d87c7fd23028e4315231f7412`
covered this document and five already-read files, found the inspection-authority,
seal-to-publication and scaffolding-interval qualifications incorporated above, and could
not independently verify the other cited emitters or resource contract. It is not
implementation sign-off.
