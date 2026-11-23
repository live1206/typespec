# Task05 checkpoint 7: native argument-list grouping

Status: this bounded profile is accepted after focused and fresh broad
validation and complete independent review. Builds on
[checkpoint 6](./pre-emission-task05-checkpoint6.md).

## Exact supported source roles

A closed, single written type with an ordinary instance arrow caller and an
audited private callee supports these native counterparts:

```csharp
Echo((x + y))                 // → Echo(x + y)
Echo((a + b), (c + d))        // → Echo(a + b, c + d)
Capture((x + y), "manual")    // → Capture(x + y, "manual")
```

Only the candidate addition pairs can be removed. Required invocation
delimiters, commas, sibling order, native writes and shared flags remain. Each
addition requires two actual owned canonical `int` formals. The two siblings
share **one original ordered argument-list fact**, with the carrier, argument
identities and invocation revision checked again as needed. This is separate
from the existing occurrence, selection and receipt currentness checks.

The `Echo` callee is an actual written private identity body or a two-`int`
addition of its own formals. The capture callee is the exact ordinary private
`object Capture(object value, [CallerArgumentExpression("value")] string text = "") => text`.
The existing generated selection checks actual owner/callee closure and
applicability, including built-in boxing for the capture argument. Provider
intent, a signature or a formal map alone cannot authorize omission.

Omitted caller-expression text retains `Capture((x + y))`. Roslyn strips the
outer argument pair from the synthesized direct constant in this fixture,
so the measured omitted constant is `x + y` even with the pair retained.
The explicit caller text is still `manual`. The scoped proof uses actual
selected argument presence and source edges; it does not treat every attributed
call, wrapper or enclosing capture as safe.

## Isolation and actual output authority

The source-only frame admits exact addition nodes after cached-formal preflight.
The explicit frame, original generated lookup environment and unrelated legacy
callback schedules stay unchanged. A dedicated generated group reuses actual
header, declaration, suppression, type and call-selection checks. For this
native route only, the written file inventory supplies path identity instead of
an explicit path pin. Disabled XML docs contribute no emitted source; the
native cold default docs builder is audited without materializing it during
preparation. Existing generated import and metadata header policies are not
relaxed.

A second written type, mixed relational or unknown sibling, opaque list, or
unproved/competing callee stays outside this exact profile. The call must not be
shadowed by a caller formal, and the declaration evidence must come from the
emitted method and formals rather than retained original symbols. A separate legacy
choice does not gain or lose permission from this admission. Currentness and
same-pass fallback still require actual source roles, names, receipts, input
proof and final compiler authority. A false final result requires isolated
output discard, even if shortened source was selected first.

## Evidence and limits

The accepted checkpoint-6 baseline had three specific missing-omission
failures and two passing controls. The fixture uses an explicitly owned array
for the observer attribute; initial unowned-carrier and caller-text-oracle
failures are tracked separately. The corrected fixture preserved actual
selected names, compiled operations and runtime results, single native visits
and a current receipt before the intended failures. All five foundational
cases passed with the candidate, without pinning the native path, warming the
XML docs, or forcing the callee parameter carriers.

All 20 new cases pass, with **1,318 focused passes and two unchanged Windows-only
skips** and all 1,300 prior IDs, names and outcomes preserved. The suite covers
relational, opaque-list and competing-call refusals, caller boxing, restored
sibling lists, cold second dependencies, and actual captured compiler-input
expiry before and after selection. Four existing reflection cases caught an
entry-point signature change; the original two-argument method was restored
and the native proof uses a separate named entry point. Existing tests were
unchanged and all four previously failing cases now pass.

Two review regressions were reproduced against the accepted baseline. A retained
original without the emitted observer allowed an omitted-text argument to shorten;
the direct compiled text stayed `x + y`. A formal named `x` shadowed an unqualified
`x` call; both forms already had `CS0149`, but the candidate still shortened it.
The scoped guards now retain both native pairs. Actual emitted observer metadata
and lexical binding were checked before the regression assertions.

Pinned checks and unfiltered local test-library generation passed with no
generated diffs after the corrections. The full generator solution passed
**8,435 tests**, with 965 skips (963 default-stubbed Spector cases and two Windows
controls). All 1,320 focused outcomes match the full run. Emitter tests:
**422 passed, two skipped**. Ten changed C# source checksums matched actual
portable PDBs and four test-host Core assembly copies matched. Complete
independent review found no outstanding findings.
WSL is the agreed validation domain; no CI, Windows, SDK, performance or default
activation result is claimed.

This does not complete arbitrary argument/call/cast shapes, raw delimiter trivia,
Task04/Task07 child-spelling composition, or Task12 conditional correspondence.
