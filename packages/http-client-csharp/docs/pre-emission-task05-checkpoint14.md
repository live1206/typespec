# Task05 checkpoint 14: direct checked casts

Status: accepted for this bounded internal use after focused and broad validation
and complete independent review. Builds on [checkpoint 13](./pre-emission-task05-checkpoint13.md).

An internal checked-expression node owns the required `checked(...)` delimiters.
Its operand is written once through the native writer. For an exact direct arrow
return containing an `int` cast of a bound `long` formal, preparation may remove
only the cast's redundant outer pair:

```csharp
checked(((int)value)) // native and qualified fallback
checked((int)value)   // selected
```

The existing `Snippet.Checked` helper, its direct cast delimiter retention, and
the checked-binary profile are unchanged. Only the exact native operand
occurrence is eligible. The actual cached formal, type and source currentness,
written-file inventory, receipt, qualified fallback and final compiler input
checks are still required. A false compiler result requires isolated output
discard, even if a shorter form was selected earlier.

The checkpoint-13 barrier suite passed all 22 cases before the change. With only
the new syntax node and source edge in place, the native test passed and the
omission test failed at the expected retained pair. After adding the narrow
permission, all five new tests pass, including checked narrowing and overflow,
actual selected and final compiler output, and input expiry before and after
selection. Focused validation passed **1,453 tests**, with two unchanged
Windows-only skips and all 1,450 prior outcomes preserved exactly.

The full generator solution passed **8,570 tests** with 965 skips (963 stubbed
Spector tests and two Windows-only controls). All 1,455 focused outcomes match
the full run. Emitter tests passed **422**, with two skipped. Pinned checks and
unfiltered local test-library generation passed with no generated diffs. Five
changed C# source checksums matched actual portable PDBs, and four test-host Core
assembly copies matched.

This change does not add typed default, arbitrary raw wrappers or trivia, or
cross-task spelling and preprocessor correspondence. WSL is the agreed
validation domain; no CI or other-platform success is claimed.
