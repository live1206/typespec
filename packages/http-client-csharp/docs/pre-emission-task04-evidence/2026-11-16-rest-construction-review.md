## Verdict

**NO NEW CODE FINDINGS within the narrow, non-authorizing, point-in-time constructor-profile boundary.** The isolated partial increment can be retained as experimental work. This is **not** acceptance of REST whole-body composition, confinement, shortening, integration, or production readiness.

The trial hypothesis—**adding this constructor profile alone closes the selected REST body**—was **not met**. The required positive remains enabled and RED at the later `LiteralExpression` frontier. Consequently, the required REST-positive and only-two-original-Foo-failures gates **failed**, notwithstanding the successful Core run.

### One minor report correction

In `REST-REPORT.md`, the final table describes the successful `rest-build-core` and `rest-build-client` commands as:

> no TRX (build or compile failure)

Both have actual exit code **0**. Change those cells to **“successful build; no test TRX expected.”** This is an evidence-wording correction, not a production defect or reason to reinterpret any test result.

## Review coverage and source assessment

I read the complete two-file incremental patch and corresponding changed source, together with the requested reports, audit, read/cost frontier, falsification plan, and parent TRX-verification receipt. I followed the affected paths through constructor writing, type formatting, source-shape capture/currentness, declaration/type snapshots, constructor/signature/parameter metadata, collection admission, and native-step projection.

### Production admission

`PreparedPocNativeCallEffect.cs:99–110` adds only the constructor arm:

- It admits the sealed built-in `NewInstanceExpression` in `Value` or `StatementValue` roles.
- It requires **null initializer**, an explicit existing `SafeType`, and an accepted live argument carrier.
- Existing recursion still profiles every captured argument child. The arm does not turn constructor identity or retained constructor intent into permission.
- It introduces no writer field, native-writer edit, binding query, Task03 permission, name-choice activation, or override of uncovered-emission status.

`SafeType` retains the existing restrictive profile: exact `CSharpType`, no literal/union metadata, declaring type or generic arguments, safe identifier/namespace text, and exact built-in runtime `Type` before runtime family queries. Arrays, pointers, byrefs, generic types and generic parameters are excluded by that runtime-family check. Custom runtime-type rejection does not require invoking its virtual family getters.

### Native emission and child projection

`NewInstanceExpression.cs:37–64` confirms the relevant branch behavior:

- Native writing emits `new`, formats the explicit type inside the original `NewInstanceScope`, and then writes arguments.
- With initializer null, `WriteArguments` is called even for zero arguments.
- The threshold of six changes layout, not which arguments are written.
- Rejecting every initializer avoids newly admitting initializer-count, dictionary, whitespace or initializer-child behavior.

`CodeWriter.cs:699–764` confirms that type formatting is separate from expression child-node events. `WriteArguments` at `1235` consumes argument occurrences in their existing order. The new arm neither replaces that enumeration nor changes its exceptions, scope cleanup, callbacks or disposal behavior.

`StructuredSourceShape.cs:170–180` retains the type, argument-carrier identity, and ordered argument children. Its snapshots/currentness checks preserve type version/name/namespace and relevant reference relationships. The existing native-step projection keeps repeated shared-child **occurrences**, rather than deduplicating identities. The five detached layout cases check that projection and native token/layout behavior at 0/1/5/6/7 arguments.

These facts support only the stated local snapshot/profile boundary—not actual formatter-scalar consumption authority.

### Non-emitting constructor intent

I found no newly exposed opaque constructor-provider getter in the inspected path:

- `ConstructorProvider.Signature` and declaration revision access are nonvirtual.
- `ConstructorSignature` and `ParameterProvider` are sealed; the inspected metadata getters are ordinary retained-state accessors.
- Signature parameter/attribute collections pass through the existing guarded collection-copy machinery rather than arbitrary interface enumeration.
- The POC attribute snapshot declines source-data projection instead of forcing its lazy machinery.

Constructor intent remains a conservatively observed dependency, **not an emitted child, binder result, or spelling authorization**. Its observation/copying costs remain owed.

## Regression and test assessment

The expectation change is appropriately limited:

- The archived original source is present at  
  `.pi/rest-before/PreparedLocalTypeClientTests.cs`.
- The separately archived before-production positive-anchor source is named  
  `.pi/rest-before/with-positive-anchor.cs`.
- The original negative control’s null local effect, null owned effect, and primary native-output parity assertions remain.
- Its unsupported-frontier assertion advances from `NewInstanceExpression` to the exact later `LiteralExpression` path; qualified-output and legacy-alternative parity assertions are added.
- The separate desired positive remains an ordinary `[Test]`. It was not ignored, deleted, or replaced with a refusal assertion.

The 20 new cases comprise one required actual REST positive and 19 detached finite-profile controls. The latter cover argument occurrences/layout, unsafe names/namespaces, unsupported type families, custom runtime type, both initializer forms, opaque argument carriers, unsafe children, tiny funds, and observed mutation/restoration withdrawal.

Important limits are accurately disclosed:

- Mutation tests call `IsCurrent` **after mutation and before restoration**. They demonstrate observed, sticky withdrawal—not detection of every unobserved transient change.
- Detached native-byte/step tests are not owned actual REST confirmation or constructor callback/final-callback tests.
- The actual REST positive fails at local capture, **before owned-effect construction and native writing**. Its subsequent native/head/query/output/callback assertions are unreachable and provide no positive evidence.
- Constructor diagnostic prints are observations, not asserted certificates.
- The baseline-case comparison receipt reports all prior **61** cases retained with unchanged outcomes and all **20** new cases present.

## Validation interpretation

I did **not** run tests or independently calculate hashes. The following are the supplied parent-verified actual-TRX outcomes, consistent with the reviewed source and receipts:

| Check | Passed | Failed | Interpretation |
|---|---:|---:|---|
| Before production | 2 | 3 | Desired REST positive fails at `$/statement[0]/expression/value/value:…NewInstanceExpression`; original Foo two also fail |
| Finite focus | 21 | 1 | Required REST positive remains RED at the later literal |
| Fresh affected Core | 1,127 | 0 | Completed green |
| Fresh affected Client | 78 | 3 | Original Foo two plus required REST positive |

The candidate REST frontier is:

```text
$/statement[2]/expression/argument[0]/value:unprofiled-native-effect:LiteralExpression
```

Thus, mandatory checks were **executed**, but the required acceptance gates were **not satisfied**. Matching the anticipated three failures does not make that gate green.

## Boundaries that remain open

The reports correctly retain, rather than silently solve:

- Actual type-formatter scalar read-position/context authority.
- Temporal mutable-collection and concurrency obligations, without inventing caller-purity or future-freeze premises.
- Complete added-work, transient/retained storage, cleanup, lifetime, default-path and producing-input ancestry certificates.
- Cold compiler work and the full `W_added <= sum(c_i * N_i) + W_paid` proof under unchanged distinct caps.
- Actual-consumed declaration/documentation scalars and full signature/member confinement. The inherited head component remains documentation-node/native-declaration-boundary input association, with completion before closing/constraints.
- Whole-member, file/export, all-39-file, expanded production/source-currentness and integration gates.

No `LiteralExpression` redesign was introduced in this increment. **Retain the partial experiment with the red positive and these qualifications intact; do not promote this review into REST/member acceptance or activation authority.**