# Task05 checkpoint 3: direct outer-cast grouping

Status: this bounded checkpoint is accepted after focused and fresh broader
validation and renewed complete stable independent review with no remaining
findings.
This builds on the accepted [second checkpoint](./pre-emission-task05-checkpoint2.md), not a full
Task05 implementation or completed Task04 composition.

## Bounded grammar and original anchors

Prepare omission of only the outer pair manufactured by native `CastExpression`
in a direct method arrow or single return-statement slot:

```csharp
// Native writer                         Prepared selected form
int M(int value) => ((int)value);         // => (int)value;
int M(int value) { return ((int)value); } // return (int)value;
```

The target and method result must be canonical, nonnullable, nonliteral/nonunion
built-in `int`. The sole actual formal must be a non-ref/out/in/params built-in
`int`, without defaults, attributes or unsupported metadata. The operand must be
the exact admitted variable carrier corresponding to that formal in the current
method frame. A matching name or advertised expression type is not binding
proof. Explicit unsupported-base-contract and unsupported-parameter-modifier
markers refuse this new cast profile; legacy admission is unchanged. Other
signature/header/source-envelope restrictions remain in force.

These map to the first two original M2
`StructuralParenthesesGrammarOuterCastGroupingPreservesConversion` cases. The
recorded return prototype contains another raw outer wrapper that native IR does
not represent. Its oracle remains preserved; this checkpoint removes the pair
actually manufactured by the native writer, not arbitrary prototype wrappers.
The successful identity-conversion cases are not evidence for every numeric or
user-defined conversion.

A finite `if`-condition equality prototype was also reproduced before production
edits, then deferred when the original direct-cast anchors proved a smaller
slice. `if` conditions are not completed by this checkpoint.

## Preparation and native writing

The existing source-occurrence preparation records the exact direct cast,
current target, actual emitted formal binding and method source path. Cast nodes
were already supported by structured snapshots and parameter frames; their old
capture schedule is unchanged. The new choice does not globally whitelist casts
or change legacy whole-file admission.

The writer consumes the choice through the same occurrence-owned hook as binary
grouping. It retains the original native type append and exactly one operand
write, in their original order. Only the selected buffer omits the outer pair;
the qualified companion retains it for late same-pass fallback. No shared flag,
new precedence/binding/type-origin model, preparation host/lifecycle, generated
text discovery or second write is introduced.

Newly admitted cast files use the existing actual-native-written file inventory,
current relevant AST/type/formal/source/receipt facts and compiler-input/final
result authority. Unrelated live path/serialization getters are not replayed to
grant new permission. This is not a getter-purity assumption: relevant changes,
missing cached facts, ambiguous descriptors or unsafe actual output names still
refuse. Existing field/body/direct-equality checks, their schedule, and all prior
checkpoint positives remain unchanged. Actual written names and contents, not a
provider's later observed path cache, bind new file evidence.

## Retention and separate work

Other targets and operands, nullable/ref formals, extra formals, nested casts,
unary parents, member/invocation/indexer receivers and arguments, sibling token
ambiguity, optional defaults and lambda/constructor/caller-expression capture
are not admitted by this direct-root slice. Unsupported whole-file shapes may
conservatively retain all grouping. The original source-observation, hidden
source, trivia, token-boundary and native writer-repair anchors remain required.

In particular, the custom `Fixtures.Number` cast with a parenthesized unary
operand and shortened type spelling is not replaced by a built-in cast bridge.
Task04 spelling-plan composition and broader cast/receiver support remain open.
Conditional-expression grouping remains Task12. SDK adoption, default/public
activation, production performance and broader Task05 completion are separate.

## Validation status

Before production changes, two original direct-cast counterparts failed only the
prepared plan/use/raw shortening assertions; four native retention controls
passed. Actual native and selected filenames, compilation, operations, emitted
IL execution, boundary-value results, receipts and one native cast/operand visit
were valid before those failures.

The initial six cases then passed. All 62 new cases and every prior 1,094 focused
outcome pass: 1,154 locally executed passes and the same two Windows-only controls
not executed. Guards cover actual-formal versus type-hint/name proof, unsupported
contexts, mutation/restore/host windows, same-pass qualified fallback without
replay, and forced target/operand-corruption oracles. Complete review found the
two unsupported-contract markers were not explicitly refused. Four negative-first
cases reproduced that admission gap while all 40 existing cast guards passed;
all 44 now pass with only the new cast capture tightened.

Real compiler-session source/hash expiry is exercised before selection (qualified
fallback and false result) and after short-form selection (false result requiring
caller discard). Actual selected filenames and contents, final physical output,
captured lambda constants/runtime text, independent files, and genuinely cold
second path/serialization dependencies are covered. The cold cases retain an
actual new cast use both with and without a separate genuine legacy positive.

The legacy final pipeline already elides identity casts entirely on the accepted
baseline, with pre-emission both enabled and disabled. Four independently built
baseline controls establish this behavior. Strict native-to-selected pre-legacy
conversion checks remain unchanged. Final operations are compared to an actual
native final-pipeline control, while boundary runtime results still match native
raw code. The comparison does not globally ignore conversions or change legacy
policy; disabled-control output supplies no captured-input publication authority.

After the metadata correction, fresh official pinned static checks and
unfiltered local test-library generation passed with no generated diffs. The
unfiltered generator solution passed 8,271 tests, with 963 default-stubbed Spector
skips plus the two existing Windows-only skips. Emitter tests passed 422 with 2
skipped. All 1,156 focused outcomes match the full run. The six changed C# source
checksums match actual Core/CoreTest portable PDBs, and four actual test-host Core
assembly copies match. Renewed complete independent review of the whole corrected
checkpoint found no remaining findings. Acceptance applies only to this bounded
slice, not broader Task05 coverage or Task04 composition.
Current WSL is the agreed validation domain; other-platform runtime verification
is not an acceptance requirement. No CI, SDK or performance success is claimed.
