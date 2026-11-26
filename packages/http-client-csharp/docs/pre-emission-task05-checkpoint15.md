# Task05 checkpoint 15: typed default in an exact nullable generic cast

Status: accepted for this bounded internal use after focused and broad validation
and complete independent review. Builds on [checkpoint 14](./pre-emission-task05-checkpoint14.md).

The internal typed-default expression owns the required `default(T?)` syntax.
It uses the existing type formatter; `Snippet.DefaultOf` stays unchanged. The
exact prepared case is an ordinary parameterless arrow method with one written
`T` and `where T : struct`. The return, cast target and typed-default operand
share one audited nullable descriptor bound to that actual declaration:

```csharp
public T? M<T>() where T : struct => ((T?)default(T?)); // native and fallback
public T? M<T>() where T : struct => (T?)default(T?);   // selected
```

Only the cast's redundant outer pair is eligible. A different formatting
parameter also named `T` is not accepted as ownership. The actual written
constraint and ordered source occurrences, currentness, receipt and same-pass
qualified fallback remain required. Compiler input authority is separate:
false final authority requires isolated output discard, even after selection.

On the accepted baseline, the legacy helper emits `((T?)null)` for this native
signature. The representation-only stage emitted exact `((T?)default(T?))`, with
all three nullable roles bound to the generated method parameter, an identity
conversion and null results for actual `int` and `Guid` calls. Only the expected
omission assertion failed. All eight final targeted tests pass, including the
wrong-owner control, restored constraint fallback, and physical compiler input
expiry before and after selection. Focused validation passed **1,461 tests**, with
two unchanged Windows-only skips and exact preservation of all 1,455 prior outcomes.

The full generator solution passed **8,578 tests**, with 965 skipped (963 stubbed
Spector tests and two Windows-only controls). All 1,463 focused outcomes match
the full run. Emitter tests passed **422**, with two skipped. Pinned checks and
unfiltered local test-library generation passed with no generated diffs. Five
changed C# source checksums matched actual portable PDBs, and four test-host Core
assembly copies matched.

No existing callers are migrated. This does not add a generic binder, change
legacy admission, or solve arbitrary raw wrapper/trivia or cross-task spelling
and preprocessor correspondence. WSL is the agreed validation domain; no CI or
other-platform success is claimed.
