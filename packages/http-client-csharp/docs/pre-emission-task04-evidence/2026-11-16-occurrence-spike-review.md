## Verdict: **BLOCKED literal experiment; evidence supports stopping**

I found **no additional blocking implementation defect within the declared observation-only scope**. This is a trustworthy failed bridge—not a working Task04 shortener, a cost certificate, or evidence that every occurrence-first implementation is impossible.

### Review scope and baseline preservation

I read the complete 382-line `experiment.diff`, the review manifest, all added emitter/test code, both complete baseline/spike `TypeProviderWriter` implementations, the real-Foo fixture, and relevant candidate-currentness, capture, frame and budget helpers.

The patch is addition-only:

- Removing the new entry method leaves the original writer source unchanged by inspection.
- Both original Foo acceptance tests retain their original assertions.
- No instance fields were added to default `TypeProviderWriter` or `CodeWriter` instances.
- The searched call sites select the experimental entry explicitly; ordinary `Write()` does not select it.
- I did not execute tests, calculate hashes, or independently enumerate the entire 4,403-file source union. Source/hash stability remains supported by the retained parent receipts rather than a newly executed verification.

### One minor evidence finding

**The opaque-tail regression logs, but does not assert, two reported facts.**

`Microsoft.TypeSpec.Generator.ClientModel/test/Providers/PreparedLocalTypeClientTests.cs:174` prints:

- `output.SourceConsumption?.WasCurrentAtEndOfWrite`;
- whether the post-write `GetPrecomputedPocOrdinaryCandidate(...)` returns nonnull.

Neither value has an assertion there. By contrast, the test explicitly asserts `CandidateCurrentAtReturn == true`, callback count one, valid parsed syntax, and the two different type bindings.

This does **not** invalidate the observed counterexample or the blocked disposition. It means “both receipts were true in the retained run” should not be described as “the regression enforces both receipts remain true.” Preserving the frozen spike is reasonable; identify this distinction in its final report.

### Implementation assessment

**Native traversal and output**

`TypeProviderWriter.cs:88–130` has one `WriteType(writer)` call and one `writer.ToString()` call. It preserves the native warning, namespace, documentation, member-writing and `RelativeFilePath` evaluation sequence. There is no writer replay, output parsing, binding query or fallback reconstruction in this entry.

`Task04OrdinaryObservation.Complete`, lines 59–99, returns its supplied native `CodeFile` on every ordinary return path. Its mechanical result never replaces that object.

The independent-fixture identity test compares complete `Content`, `QualifiedContent` and `Name`, with one writer invocation per fixture. That is meaningful output evidence for this Foo fixture—not a general exception/callback-equivalence certificate.

**Ownership and authority separation**

Attachment requires the physical writer to own the exact preparation and candidate file owner. Completion checks exact occurrence-ID, declaration and `CSharpType` identities against the published native-use receipt.

The retained candidate carries the original metadata target; the new bridge performs no target lookup. The mechanical selection contains only `false`. Neither candidate currentness nor the supplied frame’s `Completed: true` value becomes semantic authority: there is no enabled choice or authority factory.

The unattached-preparation refusal path continues native writing without installing capture. I found no ownership bypass that grants spelling permission.

**R1 observation budgeting**

`PreparedFieldTypes.Task04OrdinaryObservation.cs:29–39` passes the existing `_pocCandidateSourceBudget` into input, related-scope and source-observation checks. The inspected transitive candidate-currentness route uses these supplied budgets rather than selecting its default fresh-budget branches.

The new route does not call `candidate.IsCurrent`, `PocInputsAreCurrent`, a metadata binding query, or the direct host source-proof callback. This supports the stated R0→R1 correction, **not** closure of the complete wrapper fee, linked-account audit, storage or compiler-production costs.

### The two blockers are concrete

**1. Currentness does not establish emitted lookup confinement**

The opaque callback emits a nested class named from `candidate.SimpleSpelling`, executes once, and leaves parse-valid C#.

The test’s semantic assertions precede its intentionally failing mechanical-success assertion. The stable failure at that final assertion therefore does not erase the earlier evidence:

- the qualified declaration resolves to `System.ClientModel.Primitives.PipelineMessage`;
- replacing only that type syntax with the bare identifier resolves to the injected nested `Foo` type;
- the observation’s return-boundary candidate-currentness check remains true.

This is a sound counterexample to **candidate currentness plus traversal being sufficient authority**. It does not show that an occurrence-first route with genuine emission-confinement authority is impossible.

**2. The selected frame protocol cannot fund its attempt**

The code creates the frame and disabled selection, then calls the accepted reservation path before rendering. For the reported Foo dimensions:

```text
64 + 12×1 + 16×1 + 4×1 + 12×8307 = 99,780
```

The stable shared-account balance is reported as 39,535; the test derives the required cost rather than hardcoding that balance.

`PocFrameWorkReservations.Withdraw` checks available funds before calling `TrySpend`, explaining why this refusal leaves the account nonexhausted. Rendering does not run, and the original native object returns.

This proves refusal of **this literal reservation protocol with this remaining account**. It is not a necessary CPU-work lower bound or a universal cap-overrun theorem.

### Validation interpretation and disposition

The retained stable Client result records **9 passed / 4 failed**: exactly the two original Foo acceptance failures and the two preserved mechanical-success failures. The separate budget-refusal, independent-output-identity and unattached-preparation tests pass. The parent’s retained Core receipt reports 96 passing capture/frame cases; I inspected part of that case listing but did not rerun it.

Keep the following classified as **disclosed incompleteness**, not newly discovered spike defects:

- cold compiler-production work;
- complete new-wrapper work/storage accounting and ledger ancestry;
- serialization and root release;
- additional fallible allocations and exception equivalence;
- single-use experimental lifecycle;
- absence of genuine live emission authority.

**Stop and retain the literal experiment as BLOCKED.** Do not activate shortening, promote the candidate, weaken the preserved failures, or infer universal occurrence-first impossibility. Required production proof, expanded 575/225 validation, source-currentness gates and a complete production implementation review remain unmet.