# Task05 checkpoint 6: native used-local initializer

Status: accepted for this bounded profile after focused and fresh broad
validation and complete independent review. Builds on
[checkpoint 5](./pre-emission-task05-checkpoint5.md).

## Exact native counterpart

The original M2 local initializer has a `var` declaration and an extra raw
wrapper. This checkpoint covers only the pair represented by the native writer:

```csharp
public int M(int x, int y) { int value = (x + y); return value; }
```

The selected form writes `int value = x + y;` and returns the same local.
The declaration, its type spelling and the return stay unchanged. The binary is written once and its shared grouping
flag is not mutated. This is not arbitrary raw wrapper, `var`, or trivia support.

## Bounded ownership and freshness

Admission requires exactly an ordinary native two-statement method body. The
first statement declares and initializes a canonical `int` local from direct
built-in addition of two actual owned `int` formals. The second returns the same
local declaration identity; a hint or name match does not prove identity. The
local and cached formal declarations and names must be distinct. Unsupported
signature metadata, modifiers or defaults, unusual declaration flags, other
bodies, nested arithmetic and collisions retain native spelling.

The source-only parameter frame recognizes only the exact assignment,
declaration and initializer nodes after cached-formal preflight. The existing
explicit and unused-local call frame grammars remain unchanged. No generic
local binder, precedence model, text discovery or writer change is introduced.
Only the initializer occurrence can remove its pair. The exact context IDs feed
the existing same-pass qualified fallback and native-written file inventory,
current source/type/receipt and final compiler-input checks.

## Evidence and limits

On accepted checkpoint 5, one specific missing-omission failure and four
passing retention controls reproduced before implementation. Actual native and
selected names/source, operations, runtime including overflow, one native visit,
unchanged shared flags and source receipt were valid before the negative result.
The first candidate run passed all five.

All 23 new cases pass: **1,298 focused passes and two unchanged Windows-only
skips**, with all 1,277 prior IDs, names and outcomes preserved. Tests include
bound local aliases, shadowing and unsupported metadata controls, declaration
revision expiry before and after the opening delimiter, and genuinely cold
second path and serialization dependencies with and without a separate legacy
choice. Real compiler cases verify actual selected/final files and operations:
expiry before selection retains native spelling; physical input expiry after
short selection still requires the trusted caller to discard the isolated output.

Pinned checks and unfiltered local test-library generation passed with no
generated diffs. The full generator solution passed **8,415 tests**, with 965
skips (963 default-stubbed Spector cases and two Windows controls). All 1,300
focused outcomes match the full run. Emitter tests: **422 passed, two skipped**.
Four changed C# source checksums matched actual portable PDBs and four test-host
Core assembly copies matched. Complete independent review found no verified findings.
WSL is the agreed validation domain; no CI, other-platform, SDK, performance
or activation result is claimed.

Used-local support does not complete argument positions, the original sibling
argument-list proof, explicit caller-expression override positives, additional
source/trivia shapes or Task04/Task07 child spelling composition.
