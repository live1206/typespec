# Task05 handoff: ordinary parentheses and writer grammar

Contract: [Task05](https://github.com/microsoft/typespec/issues/12454#issuecomment-6543541641).
Dependencies: Tasks03 and 04. Apply the [shared guide](./pre-emission-implementation-guide.md).

## Scope

Prepare grouping, precedence and separator choices for the exact accepted M2
payload/parent-role inventory. Include nested chains, binary associativity and
left/right operand differences, required cast operands, member/invocation/indexer
receiver barriers and the admitted outer-cast grouping cases.

No reassociation, general pattern-parentheses simplification, unsupported/default-literal payload expansion or broad hidden-source repair. Conditional-profile
grouping belongs to Task12. Do not remove existing writer assignment/default
repairs or change legal expression-statement emission.

## Minimal mechanism

Use one parent/child grammar policy over the existing structured nodes. Inputs
include operator/payload kind, exact parent edge and ancestry, selected child
spellings, boundary tokens/separators and source-observation facts. Prepare
keep/omit/separator choices before emission; do not write once to discover
precedence, lex generated text, or build another general expression model.

A child spelling can change lexical ambiguity without changing its type. Compose
with Task04 plans before deciding parentheses, and later Task07 receiver choices
through preparation ordering, not another write or a dependency cycle. Source
capture/layout restrictions are separate from precedence.

Binary/cast writers already manufacture grouping; the inspected IR has no dedicated
parenthesized-expression node for every raw prototype wrapper/trivia case. Decide
the minimal trusted grouping contract first. Do not mutate a shared AddParentheses
flag to authorize every occurrence of an expression.

## First checkpoint

Start with an original structured binary expression in a return/arrow slot:
remove unnecessary outer grouping but retain the right-associated subtraction
counterpart. Include the no-recorded-type-reference case so admission needs no
dummy field or type reference. Use the actual parent role, plus an observed
counterpart that retains. Assert prepared/raw form, native operations/diagnostics
and captured text, one traversal and changed-child same-pass fallback.

## Checkpoint implementation status

The bounded first-checkpoint implementation and its remaining allocation debt are
described in [Task05 first checkpoint](./pre-emission-task05-checkpoint.md).
The final-currentness and suppression-admission corrections passed fresh focused
and broader canonical validation and complete stable independent review. This
bounded first checkpoint is accepted. See its status for precise scope, evidence
and limits. Neither the checkpoint nor
the isolated trial is full task or SDK adoption evidence.

[Checkpoint 2](./pre-emission-task05-checkpoint2.md) implements a bounded set of
binary operand edges and nested chains using unchanged native child spellings.
The approved actual-native-written-file/source freshness trial and reproduced
review corrections pass local focused regressions and fresh broader validation
(two Windows-only controls are not executed here). Complete stable independent
review found no remaining findings; this bounded checkpoint is accepted.
New plans use current source/formal and actual output evidence
instead of additional live global path/serialization queries. The baseline
field/body/direct-equality behavior is unchanged. This does not require full Task04 completion;
composition with Task04 spelling plans remains a separate checkpoint.

[Checkpoint 3](./pre-emission-task05-checkpoint3.md) prepares the native outer pair
of an exact built-in `int` cast of its actual `int` formal in a direct return or
arrow slot. Its 62 new cases, all 1,094 prior focused outcomes and fresh broader
validation pass after the unsupported-metadata correction. Renewed complete
stable independent review found no remaining findings; this bounded checkpoint
is accepted. Native
type spelling, one operand traversal and late qualified fallback are preserved.
Other cast targets/operands, receivers and Task04 composition remain separate.

[Checkpoint 4](./pre-emission-task05-checkpoint4.md) prepares exact native integer
equality in the `if` condition of a closed Boolean method body. Its 91 new cases
and all 1,156 prior focused outcomes pass, including the host- and provider-callback
corrections. Fresh broader validation passes and complete stable independent review
found no remaining findings; this bounded checkpoint is accepted. Required `if`
delimiters, native body writing, parameter-frame capture and
legacy admission remain unchanged. The shared input callback and the actual native
header configuration are checked without replaying getters or changing emitted headers. Other
statement/call roles and the full accepted inventory remain open.

[Checkpoint 5](./pre-emission-task05-checkpoint5.md) prepares only the grouped
right child in a direct native string-coalescing return or arrow body. It never
reassociates operands or removes the root's own pair. All 30 new cases and prior
focused outcomes are verified and fresh broad validation passes. Complete
independent review found no verified findings; the bounded checkpoint is accepted.
Local initializers, arguments, and sibling lists remain open.

[Checkpoint 6](./pre-emission-task05-checkpoint6.md) prepares only the native
pair in a direct, used `int` local initializer, with the second statement returning
that exact declaration. All 23 new cases and prior focused outcomes are verified,
and fresh broad validation passes. Complete independent review found no verified
findings; this bounded checkpoint is accepted. Arguments and sibling lists remain open.

[Checkpoint 7](./pre-emission-task05-checkpoint7.md) covers a closed native
ordinary argument, sibling additions sharing one original argument-list fact,
and an explicit caller-expression override. An omitted text argument retains
native spelling. All 20 new cases and prior focused outcomes pass after scoped
corrections for retained metadata and formal shadowing. Fresh broad validation
also passes; complete independent review found no outstanding findings. This
bounded checkpoint is accepted. Wider call, cast and raw source roles remain open.

[Checkpoint 8](./pre-emission-task05-checkpoint8.md) prepares the outer cast pair
in three exact native roles: widening local initializer, left operand of
widening addition, and a Boolean cast in an ordinary `?:` condition. All 24 new
cases, prior focused outcomes and fresh broad validation pass. Complete
independent review found no verified findings; this bounded checkpoint is
accepted. Other cast and source roles remain open.

## Remaining accepted coverage

- Left/right binary operands, nested grouping chains and outer-cast cases.
- Cast and member/invocation/indexer receiver requirements.
- Sibling relational/generic argument-list ambiguity and required separators.
- Comments, LF/CRLF, token boundaries, source guards and original writer repairs.
- Combined type spelling plus grouping without either decision authorizing the other.

Anchors: StructuralParenthesesTests, StructuralParenthesesSafetyTests,
StructuralParenthesesGrammar*Tests and existing writer/expression regressions.
Preserve StructuralParenthesesNonReassociatingCasesKeepOperations and
StructuralParenthesesRetainsParserAndAncestralBarriers. Conditional cases embedded
in grammar-safety files are cross-referenced to Task12, not silently dropped.

## Stops and completion

Unknown payload/role facts retain; they do not justify a general parser or flag
reset. A missing accepted positive stays open. Do not repair unrelated statement
syntax under the label of parentheses cleanup.

Complete only after the accepted inventory, raw/pipeline composition and native
grammar/source controls pass the shared validation gates. A simple grouping demo
does not close the left/right, separator or source-observation cases.
