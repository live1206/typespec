// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Resource operations for the same source-currentness predicates. A null
    // budget retains native/shared behavior; no method grants writer authority.
    internal static class SourceCurrentnessChecks
    {
        internal static bool Spend(PocWorkBudget? budget, int count = 1) => budget?.TrySpend(count) ?? true;

        internal static bool Shape(StructuredSourceShape expected, object producer, bool disableXmlDocs, PocWorkBudget? budget, PocTypeTextMemo? textMemo = null)
        {
            if (budget is null) { return expected.Matches(StructuredSourceShape.Capture(producer, disableXmlDocs)); }
            var current = StructuredSourceShape.CaptureForPocRecheck(producer, disableXmlDocs, budget, textMemo);
            return current is not null && expected.MatchesForPoc(current, budget);
        }

        internal static bool Type(DeclaredTypeSnapshot? snapshot, PocWorkBudget? budget) => snapshot is null ||
            (budget is null || snapshot.TryReserveSharedCurrentness(budget)) && snapshot.IsCurrent();

        internal static bool References<T>(IReadOnlyList<T>? current, ImmutableArray<T> expected, PocWorkBudget? budget) where T : class =>
            budget is null ? ContractCollections.SameReferences(current, expected) :
                ContractCollections.SameSharedReferencesForPoc(current, expected, budget);

        internal static bool Copy<T>(IReadOnlyList<T>? current, PocWorkBudget? budget, out ImmutableArray<T> copied) =>
            budget is null ? ContractCollections.TryCopy(current, out copied) : ContractCollections.TryCopySharedForPoc(current, budget, out copied);

        internal static bool OccurrenceLookup(SourceOccurrenceId occurrence, int count, PocWorkBudget budget)
        {
            // Record hashing visits all four strings even for an identical key;
            // include every possible collision comparison before the lookup.
            long keyWork = 5L + (occurrence.File?.Length ?? 0) + (occurrence.MemberPath?.Length ?? 0) +
                (occurrence.RootRole?.Length ?? 0) + (occurrence.EdgePath?.Length ?? 0);
            long work = keyWork * (count + 1L);
            return work <= int.MaxValue && budget.TrySpend((int)work);
        }

        internal static bool OccurrenceComparison(SourceOccurrenceId? left, SourceOccurrenceId? right, PocWorkBudget budget) =>
            budget.TrySpend() && (ReferenceEquals(left, right) || left is null || right is null ||
            (budget.TrySpendStringComparison(left.File, right.File) && budget.TrySpendStringComparison(left.MemberPath, right.MemberPath) &&
             budget.TrySpendStringComparison(left.RootRole, right.RootRole) && budget.TrySpendStringComparison(left.EdgePath, right.EdgePath)));

        internal static bool OrdinalLookup(string key, int count, PocWorkBudget budget)
        {
            long work = (key.Length + 1L) * (count + 1L);
            return work <= int.MaxValue && budget.TrySpend((int)work);
        }

        internal static bool Text(string? current, string? expected, PocWorkBudget? budget) =>
            (budget is null || budget.TrySpendStringComparison(current, expected)) && current == expected;
    }
}
