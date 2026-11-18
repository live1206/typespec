# Task05 checkpoint 2: binary child grouping

Status: this bounded checkpoint is accepted after local focused and broader
validation and complete stable independent review with no remaining findings.
Two Windows-only controls were not executed on this host. This builds on the
accepted [first checkpoint](./pre-emission-task05-checkpoint.md), not on a completed
Task04 or a full Task05 implementation.

## Bounded grammar

With unchanged native child spellings and actual built-in `int` formal bindings,
prepare omission only for these parent/child edges:

| Parent operator | Child position | Child operator |
| --- | --- | --- |
| `-` | Left | `-` |
| `+` | Right | `*` |
| `==` | Left or right | `+` |

The existing direct Boolean return/arrow equality rule remains. Recognized nested
Boolean equality can use it with the child rules. Arithmetic-return **outer**
wrappers remain native: `((x - y) - z)` becomes `(x - y - z)`, not an independently
authorized unparenthesized return expression. Repeating the proven left-subtraction
edge supports a finite nested chain. Other edges retain their wrappers; this is
not a general precedence table or reassociation engine. In particular,
`x - (y - z)` and `x + (y + z)` retain the right grouping.

Original M2 `leftAssociation` and `higherPrecedence` fixtures establish the first
two edge expectations. Equality operands are additional structured-writer cases,
not falsely attributed to those legacy `Reduce` source-string fixtures. Existing
POC expectations and checkpoint-1 regression assertions remain unchanged.

## Mechanism and boundaries

A small static policy recognizes existing native binary/variable nodes; it does
not construct another expression model, bind generated code, parse output, or
mutate shared `AddParentheses` flags. Each decision belongs to its source
occurrence and exact edge, including repeated uses of one producer at different
edges.

The additional parameter frame is source-capture-only and requires known native
integer formals and cached emitted declaration state before new state observations.
The explicit frame API and original direct-equality path retain their behavior.
The existing direct-equality fast comparison is unchanged; nested shapes use the
original full currentness fallback. Only nodes and signatures from a completely
recognized method tree extend file admission. Unknown facts retain native forms.

The new recognition path is limited to 128 region occurrences and depth 24.
Repeated DAG edges count separately; the return statement's two framing nodes
count toward the occurrence limit. These are local recognition/frame limits,
not a claim that the earlier shared source-graph construction has a new global
resource bound. Existing profiles' limits are not lowered or raised.

New plans conservatively require the captured complete written root/serialization
set and known nested collections. The new family then uses **actual native-written
file/source evidence**, not additional live global path/serialization queries.
The standard writer passively records each provider and immutable `CodeFile` after
its existing native file-name read. Selection requires the complete actual output
inventory, distinct safe names with OS-correct containment and current reparse
checks on those actual names, and the used file's receipt generation, name,
root count and consumed occurrence identities. Ambiguous captured file descriptors
retain new-family forms before registration rather than throwing or replaying
path getters. Rooted-but-partially-qualified names are not relative file names.
File checks use the actual invocation
output directory, including compiler-host isolated output; compiler-input authority
remains tied to its original directory. Filesystem-root child names are supported,
while absolute names and the output directory itself are not new-family file names. Extra writes or a genuine native
name/receipt mismatch retain the qualified companion.

This is an explicit new-family freshness boundary: a later change visible only
through a path or serialization getter is not itself a grouping veto when the
actual emitted files, relevant source and bindings are unchanged. It is not a
purity certificate for arbitrary getters. A previously observed getter can still
load a different cold dependency later; the new proof avoids replaying those
queries at expression and final-selection boundaries. It instead checks current
native declaration/context data, the actual AST/formal frame, header and
suppression facts, current source/compiler inputs, and final compiler-result
authority. Cached formal spellings are checked before new source walks. Permission
is freshly checked, never cached as a successful result.

Baseline-compatible field/body/direct-equality candidates retain their global
snapshot and callback schedule. New-only plans do not activate that legacy
snapshot. Compatibility is determined by the **old whole-file grammar**: a
direct-shaped equality sharing a newly admitted arithmetic file belongs to the
new context, not automatically to the old family. The original direct
return/arrow controls retain their exact fixture host-query totals, 27 and 25.

A local AST/frame or method-body expiry vetoes its own grouping file; a coarse
owner epoch from a body-only update is not another file's failure. Shared cached
declaration data remains current, while the owning file retains its full epoch
and AST checks. Shared source/context or inventory failure vetoes the affected
shared permission. Independent field
choices retain their own proof and final data checks, including after new source
callbacks. Legacy final checks run after new callbackful source observations;
all final data seals precede materializing every selected file, which precedes
publication observers. Native children still traverse once with the qualified
companion captured during that same pass. Shared `AddParentheses` flags are not
mutated. Mixed grouping/type-name alternatives in one file still retain native
spelling.

## Development evidence

Before production changes, ten designated shortening cases failed on checkpoint
1 while seven association/oracle/direct-positive controls passed. Native and
selected baseline output compiled and had matching operation fingerprints and
runtime results before the intended raw-form assertions failed. One initial
oracle setup error was corrected without changing its expected semantics; the
original logs were retained.

The combined focused class set contains 1,094 cases: all previous 1,044 IDs
(including the original 993 and the 51 new grammar cases) plus 50 materialization,
native-file evidence and isolation cases. Local validation passed 1,092 cases;
two Windows-only rooted-name controls were not executed on this Linux host.
Every previously executed 1,089 case remains passing. Coverage includes:

- Return and arrow positives for the exact edges, nested left chains and a shared
  producer omitted at one edge but retained at another.
- Native flags unchanged, unsupported mirror edges and disabled child grouping,
  local occurrence/depth boundaries, cold-formal nonforcing and unchanged explicit
  frame admission.
- Actual formal correspondence rather than matching names or advertised type
  hints; null/unknown payload and unknown-carrier no-accessor refusals.
- Actual selected pre-legacy `CodeFile.Content` with `CodeFile.Name`, independently
  compiled/emitted/executed and compared with final legacy-processed publication.
- Real pipeline grouping plus a separate field-name file; child-only fallback
  retaining that field decision and host expiry vetoing both.
- An observed arithmetic lambda's actual omitted caller-expression constant and
  runtime text, with an independent forced-edit counterexample.
- Mutations before writing, at a second nested opening, after writing, and inside
  host freshness queries: same-pass qualified fallback without replay, with actual
  use/hook preconditions and one native visit per binary occurrence.
- Phase-dependent second cold dependencies in custom path and serialization
  accessors, including final pipeline selection, with actual shortening positives.
- Later getter-only value and change/restore acceptance versus actual native name
  or written-inventory mismatch refusal; relevant operand/formal/source expiry.
- Old direct-only versus newly admitted mixed-file classification, and two grouping
  files proving local leaf/body expiry isolation and shared-source/header vetoes.
- Case-sensitive output-root escape and late symbolic-link refusal, with genuine
  shortening controls, actual names, and the compiler-host path rejection control.
- Real compiler-host isolated versus original output roots, late links in either
  tree, filesystem-root child and strict filename controls, physical input-hash
  expiry before selection and final false-result discard after selection.
- Paired baseline/candidate captured-descriptor collision refusal without an
  exception, stable four-use shortening, and platform-specific rooted-name guards.

The synthetic host-expiry tests are not physical compiler-input/discard proofs;
checkpoint 1's physical-proof controls remain in the preserved regression set.
No performance benchmark or production allocation improvement is claimed.

Fresh validation including the rooted-name and captured-descriptor corrections
passed, each with observed exit 0: official pinned generator static checks,
official unfiltered local test-library generation with no generated diffs, full
generator-solution tests (8,209 passed, 963 default-stubbed Spector skips and two
Windows-only skips), and emitter tests (422 passed and 2 skipped). All 1,092 locally
executed focused IDs passed again in the full run; the same two Windows-only IDs
were not executed in either run. The 19 changed C# source checksums match the
actual Core and CoreTest portable PDBs, and four test-host Core assembly copies
match. The collision fixture explicitly asserts zero plans and uses for ambiguous
descriptors and four of each for its stable positive. After that test-only
strengthening, targeted tests, pinned static checks, the full solution and current
PDB checks passed again; generator and emitter inputs were unchanged. An extra
full rerun had first aborted at an unchanged plugin-build test; a fresh-temp retry
with per-run build-server reuse disabled passed, without establishing the earlier
hang's root cause. Complete independent review of the stable whole checkpoint
found no remaining findings. Windows runtime coverage remains unexecuted locally;
these checks do not claim live SDK coverage or CI success.

First-load comparisons separated actual cold-state changes from extra calls.
Native caches and a lazy path dependency initialized at the same baseline phase;
serialization `Count` was already initialized by normal reference analysis. An
opaque nested collection instead first loaded before the method rather than in
native nested-type writing afterward. A publicly reset unwritten root's `Type`
also rebuilt inside a new child check instead of remaining cold through that
point. Both were reproduced before the guards, with and without a genuine old
direct-equality positive. Those eight anchors remain unchanged. Further paired
probes then showed that path and serialization accessors could load a **second**
cold dependency during new use and final selection, despite the first dependency
being warm. Those failures motivated the explicitly approved actual-written-file
freshness boundary above. Additional before-fix anchors distinguish local from
shared expiry. The old direct family's inherited observations remain unchanged.

## Task04 dependency and remaining work

This slice uses native child spellings and therefore does not wait for all of
Task04. A future composition checkpoint must consume the relevant stable Task04
spelling plans **before** choosing grouping; it must not reproduce that planner
or infer permission from a shortened child alone.

Other operators and parent roles, casts, receiver barriers, sibling
relational/generic ambiguity, separators and broader source inventory remain
open. Conditional grouping remains Task12. Real-SDK opportunity, default/public
rollout, broader Task05 completion and remaining allocation/hashing debt are
separate work. Validation and complete independent review establish acceptance
only for this bounded checkpoint, not those broader tasks.
