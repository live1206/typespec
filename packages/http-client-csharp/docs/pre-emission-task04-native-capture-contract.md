# Task04 native type-use capture contract

**Status: LOCAL DESIGN CONTRACT DONE.** This closes an observation-only integration:
record the actual native formatter use without changing its text, invoking it twice or
granting spelling authority. Actual Task04 choice creation and Foo emitter/alias/epoch
proof remain separate. [C0](./pre-emission-task04-c0-contract.md) and
[native-frame selection](./pre-emission-task04-native-frame-contract.md) stay closed.

Evidence: frozen-source delegate `d24fa69107653c2429f7b2be23b3339c5`, 13 turns / 12 calls,
then parent source assessment. All six live sources matched the snapshot when integrated.
HEAD `2afe54d5de261897d49a182e5eda2c6d3411ba95`, tracked diff
`49c38f91188c59343543eda88fb2ad74fe4c26ad56c77537866311890f0f7d8f`.
No code/test changes, builds or experiments were performed to close this contract.

The [bounded-overhead contract](./pre-emission-task04-bounded-overhead.md) is normative.
This document fixes algorithms/ownership boundaries, not numerical tariffs or record layout.

## 1. Preserve the exact existing read/format sequence

[DeclarationExpression.cs:26-34](../generator/Microsoft.TypeSpec.Generator/src/Expressions/DeclarationExpression.cs#L26):

1. Write existing `using `, `out ` and `const ` prefixes.
2. Call `TryWritePreparedLocalDeclarationType(this)` unchanged in meaning.
3. If it succeeds, retain Task03 prepared emission; **do not additionally format or capture**.
4. If it fails, run the original `writer.Append($"{Variable.Type}")` once, with an optional
   non-emitting recording window around that branch only.
5. Write the existing separating space and `Variable.Write(writer)`.

[CodeWriter.SourceOccurrences.cs:59-75](../generator/Microsoft.TypeSpec.Generator/src/Writers/CodeWriter.SourceOccurrences.cs#L59)
already reads `producer.Variable.Type.Namespace` while evaluating the preflight call.
**Do not hoist/reuse that result.** The fallback must perform its original `Variable.Type`
read at its original position. Associate the resulting CSharpType argument at the existing
[dispatcher:135-146](../generator/Microsoft.TypeSpec.Generator/src/Writers/CodeWriter.cs#L135),
not by reading the declaration again.

The window receives only an optional already-bound occurrence/choice handle. Missing
handles mean no discovery, new type inspection or allocation. Supplying the handle needs
its own admitted fixed/paid lookup contract; a global dictionary search is not ingress.
A capture handle is output-evidence ownership, not a way to fabricate a genuine choice.

## 2. Record facts at their actual native reads

The ordinary path in [AppendType:697-754](../generator/Microsoft.TypeSpec.Generator/src/Writers/CodeWriter.cs#L697)
performs two distinct namespace reads:

```text
UseNamespace(type.Namespace)     // registration input
AppendRaw("global::")
AppendRaw(type.Namespace)        // emission input, read again natively
AppendRaw(".")
[optional declaring-type output]
AppendRaw(type.Name)
```

[UseNamespace:426-437](../generator/Microsoft.TypeSpec.Generator/src/Writers/CodeWriter.cs#L426)
can invoke existing import observation between those namespace reads. Record them
separately. Sharing one cached namespace changes native behavior. CSharpType's Name and
Namespace getters return fields ([CSharpType.cs:194-221](../generator/Microsoft.TypeSpec.Generator/src/Primitives/CSharpType.cs#L194));
record their actual returned immutable strings, not later getter values.

Observe existing condition operands/results exactly once and keep an observed mask.
Do not call friendly-name lookup twice, inspect Count to corroborate Any, reread flags,
compare via user equality, reflect over the type or construct FullyQualifiedName.
A false compound condition does not prove all operands false: record actual IsArray and
whether the right operand ran. Short-circuited facts remain unobserved, not guessed.

Initial usable capture requires actual root arguments/branches showing: IsArray false;
friendly-name lookup false; ordinary qualified mode rather than declaration/name-only;
DeclaringType observed null; Arguments.Any false; no nullable suffix emitted. Record only
what native execution demonstrates; no suffix is not automatically proof every nullable
flag was read/false. Other shapes still format natively but produce no usable simple-Foo
capture. This is observation of the initial nongeneric/non-nested/non-array route, not
binding or input-completeness proof.

## 3. NativeTypeUse evidence and exact range boundary

A separate immutable snapshot record carries:

- generation/file/occurrence and optional opaque choice/slot identity;
- actual CSharpType reference and formatter arguments/root association;
- observed branch facts/mask, registration namespace, emitted namespace and emitted name;
- primary and qualified start/length, native order, completion/availability state.

Type identity is reference identity, not Equals/reflection. For the first usable capture,
match the actual argument to the already-owned expected CSharpType reference supplied by
the handle; retain that existing owned reference/match fact. A different/unowned type still
formats natively but makes this capture unavailable. Do not retain an arbitrary new type
and its reachable object graph after native execution merely because one reference fits
in a slot. Broader identity support needs its own ownership/retention bound. An IdentityVersion
is at most an invalidation witness, not an epoch/completeness proof. Missing/unavailable
recording is not an invented mismatch and cannot authorize reduction.

Instrument known native append **sites/kinds**; do not identify chunks by parsing,
searching or comparing captured output text. For the simple route, the designated chunks
are global prefix, emitted namespace, dot and emitted name. A foreign append, nested/
reentrant formatting, unexpected sequence or incomplete root invalidates that capture
without suppressing any native operation.

Start immediately **after native AddSpaces** and before the first global-prefix payload
copy. End after normal return of the root AppendType. Prefixes, indentation, separating
space and variable name are outside the range. Starting before AppendRaw would wrongly
include indentation ([CodeWriter.cs:793-848](../generator/Microsoft.TypeSpec.Generator/src/Writers/CodeWriter.cs#L793)).

Keep an independent primary payload counter and the existing qualified coordinates. Update
primary accounting at every native advance site: raw chunks, raw character/newline and
indentation. Earlier Field/Body reductions can make starts differ, although this simple
native use has equal payload lengths. If no native qualified companion exists, explicitly
alias qualified coordinates to primary and record that mode; do not use a zero inactive
counter or create a new whole-file buffer just for capture. Never recover positions by
scanning text.
New-counter overflow makes capture unavailable; it must not throw a new native exception
or suppress/change the existing qualified counter's checked behavior.

## 4. Window ownership, reentrancy, drain and disposal

The optional guard is a fixed-size/no-boxing representation or otherwise explicitly paid;
no unreserved scope object or closure is allocated per declaration. Its `finally` path
clears/restores recording state only for the exact owning window and valid writer/drain
epoch. Never blindly restore an old pointer after failure, disposal or a reentrant drain.

No normal-completion record is published on native failure. Reentrant/nested activity may
conservatively abandon capture, but native formatting continues unchanged. Window identity
and epoch prevent a stale end/finally from completing another window or resurrecting one
that was detached. Counter/epoch saturation is fail-closed, not wraparound.

At ToString, distinguish completed records from an in-progress window. Transfer completed
owned records for that drain; a drain during an active capture abandons the incomplete use.
Reset added counters/window ownership for subsequent native drains even on the empty path.
Do not change native drain/header/exception behavior or publish partial metadata after a
native failure. Later writes cannot mutate already published frame metadata.

## 5. Header transfer and native fallback

[CodeWriter.ToString:1058-1138](../generator/Microsoft.TypeSpec.Generator/src/Writers/CodeWriter.cs#L1058)
constructs the actual header once. Add its actual length independently to both payload
starts. No header replay, import enumeration to estimate positions, or qualified-to-primary
reconstruction. Empty/headerless/repeated drains need explicit implementation controls.

Publish a separate NativeTypeUse snapshot; do not append a POC alternative here or change
existing Field/Body metadata. Mixed-range validation and selection belong to the separate
frame contract and are paid. Transfer ownership of fixed storage or make a paid immutable
copy; do not expose mutable/pool-backed memory to the resulting frame.

[TypeProviderWriter.cs:86-110](../generator/Microsoft.TypeSpec.Generator/src/Primitives/TypeProviderWriter.cs#L86)
obtains native strings before disposing its writer. Retain those actual strings as fallback:
never reconstruct `global::` + saved namespace + name. No span, reader or pooled segment
escapes. Capture denial preserves native/legacy output, not a switch that discards valid
independent reductions.

## 6. Resource algorithm — reserve before additional retention

Fixed ingress includes only the audited state checks, actual-read scalar/reference facts,
site tags, bounded counter/slot operations and exact-owner release. All transitive/argument
work is included in its tariff. No plan search, loop, allocation, lock or retry is hidden.

1. At file attachment reserve/allocate fixed recording capacity `K`, fixed state and cleanup
   under the existing owner/ledgers. No growth or new per-file full allowance.
2. Claim one prepaid slot/window with the admitted ownership protocol; no slot means no
   usable capture. The actual argument must match an already-owned expected type identity
   for a retained usable record; unowned reachable graphs are not fixed-size storage. No
   native getter is evaluated merely to classify the missing-handle path.
3. **Before storing a returned namespace/name reference beyond the native expression,**
   reserve its retained-character accounting using that same returned string's Length.
   Conservatively charge each stored value. If unavailable, do not retain it; abandon
   recording and forward the native value unchanged. Handle null without adding a throw.
   Do not defer this reservation until normal completion while retaining arbitrarily large
   strings in scratch. Temporary native operands are not permission for added retention.
4. Prepay any variable comparison against a plan using actual immutable string lengths;
   comparison may be deferred, but never replay getters. Merely recording facts need not
   perform a plan comparison or grant permission.
5. Reserve snapshot allocation/ownership transfer and a bounded `K`/completed-record pass
   before it runs. Fund header rebasing, immutable copying, clearing and release. On lack
   of funds/overflow discard POC metadata, not native output.

This tightens the delegate's completion-time retention proposal to satisfy the approved
reserve-before-storage rule. Setup/allocation/copy costs remain implementation-audited;
new allocation failure follows existing policy, not a fabricated resource-mismatch result.
Native formatting remains the original traversal; do not replay it or relabel moved/header
work free. Ordinary non-thread-safe budgets are not assumed safe for competing entries.

### 6.1 Passive implementation: carrier, tariffs and terminal publication

The passive implementation reuses the **original reference-width `_nameAlternatives`
slot**, discriminating the original native List from paid `PocNativeTypeCapture` state.
It adds **no CodeWriter instance field or backing property**. Native add/take operations
keep the original List and its ownership; no POC-only list copy or hidden global table is
introduced. Null, the already-owned `_scopes`, and the writer itself are distinct
fresh/claimed/terminal values; none requires a new marker allocation. See
[slot/setup/list/release helpers](../generator/Microsoft.TypeSpec.Generator/src/Primitives/PocNativeTypeCapture.Writer.cs#L12).

One setup claim precedes the existing ledger debit and optional-state allocation. Initial
claim losers and guard rejections before `TrySpend` do not allocate or debit. Once reservation
succeeds, an attempt may allocate its paid state and then lose the final installation CAS
to disposal; it returns false, releases that state and may consume the full `1024 + L`
reservation without a refund. Failed ledger withdrawal retains the ledger's existing
behavior and does not allocate capture state. The sticky `_hasWrittenPayload` guard still
rejects setup after any native payload; an uncaptured empty drain remains admissible.
Caller-exclusive root ownership, or a genuinely preflighted finite parent chain, remains
required: linked/concurrent ordinary ledger withdrawals are not certified constant ingress.

`SetupWork = 1024` is unchanged: fixed initialization 128, fixed state storage 256,
one immutable-snapshot allocation/storage 256, and field transfer/clearing/cleanup 384.
These are conservative work/storage units, not machine-cycle or object-size claims.
The caller additionally prepays `L` retained characters; each of the three exact returned
string references is charged by its Length **before** retention, including duplicate
registration/emission values. At most L UTF-16 characters are retained (at most 2L content
bytes); no traversal of caller-owned type/declaration graphs is added. State and publication
implementation: [PocNativeTypeCapture](../generator/Microsoft.TypeSpec.Generator/src/Primitives/PocNativeTypeCapture.cs#L10).

The existing uniform fixed-ingress tariff remains **c_i = 4096** per qualifying original
native or external protocol event. Local added primitive-operation envelopes include
argument work and bounded callees; they exclude the separately prepaid allocation/transfer:

| Original event | Added path bound |
| --- | --- |
| Declaration fallback begin/finally | 128: owner/start/end/mask/abandon checks. |
| Native Append / type dispatcher | 32 / 64: state/mode/identity checks. |
| Native AppendType entry and normal return | 192 combined: positions, root/mode/required-fact checks. |
| Evaluated boolean / namespace registration / designated chunk | 64 / 96 / 128: fact or Retain path; no additional getter. |
| Native raw append / char-span-indent advance | 128 / 64: site/counter/overflow checks. |
| Native legacy metadata add/take | 64: union dispatch/reference transfer; original List work remains native. |
| Native ToString entry/completion/finally | 256: drain/rebase/publish/release; prepaid snapshot work is separate. |
| External setup / Dispose / metadata query | 128 / 64 / 16; setup debit/allocation is separately paid. |

These paths are acyclic: no observer-created loop, list scan, retry, lock, reflection,
metadata discovery or comparison is covered by a free entry. Original formatter loops,
imports, buffer copies and header enumeration are neither replayed nor observer-created
entries. There is no new runtime/cold-path exception to the normative resource contract.

The paid state stays reachable while native finalization drains buffers and builds headers.
Publication claims its exact live token **before** snapshot allocation; an exact-token CAS
alone exposes the immutable record. Release invalidates either live or publishing state
with at most two publication CAS operations, plus one drain exchange and fixed scratch
clearing. Slot-level release uses at most two CAS operations. No retry loop or waiting is
needed. A prior published snapshot survives later Dispose; a later native drain may clear
its current diagnostic view without reviving publication. None of this makes native buffer
writing thread-safe or supplies Foo binding/shortening authority.

Regression evidence includes the unchanged 25-field native layout/zero-credit control,
actual native header CompareInfo disposal and dispose-then-throw parity, exact/one-short
setup/retention, failed/competing setup, installation/publication stress controls,
empty/repeated/reentrant drains and saturation. Source-level transition analysis covers
both installation/release race winners; stress runs do not establish exhaustive schedule
coverage. Tests are evidence, not authority or a substitute for the complete caller/hook
resource proof; merged validation and independent follow-up remain acceptance gates.

## 7. Failure and implementation gates

| Condition | Behavior |
| --- | --- |
| Successful existing Task03 path | Unchanged; no added native formatting/capture. |
| Absent handle, unsupported shape, foreign/reentrant activity, capacity/funds unavailable | No usable capture; native operations and text continue unchanged. |
| Native formatter exception | Original exception propagates, no catch-and-reformat, window cannot publish completed evidence. |
| Native header/ToString exception | Existing propagation, no partially published capture/frame. |
| Stale window/drain/disposed owner | No completion/reattachment; exact owned cleanup only. |

Future modifications are limited to fallback window in DeclarationExpression; already-
bound handle plumbing in SourceOccurrences; dispatcher/actual-read/site/counter/header
capture in CodeWriter; separate snapshot adjacent to the existing metadata handoff.
No CSharpType semantics, new formatter, parser or binding engine is needed.

Implementation must validate byte-identical primary/qualified output on/off; original
preflight and fallback reads/order preserved; distinct registration/emission namespace
values; successful Task03 path not additionally formatted; prefix/indent/header/legacy
coordinate correctness; absent/exhausted/unsupported/null/reentrant/drain cases; original
exceptions and cleanup. Comparison/retention exact/one-short and independently funded
observation controls remain required. These tests are future work, not run here.

This local capture contract is settled. Numeric tariffs, exact field layout and mechanical
offset implementation may be refined under these rules. Wider Foo authority and actual
choice construction do not reopen it; a concrete incompatible read/exception/ownership
finding would. The original contract closure was documentation-only; the subsequent
passive implementation audit is recorded in §6.1.
