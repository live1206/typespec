// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.TypeSpec.Generator.Input;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Never invoke arbitrary IReadOnlyList indexers/enumerators or lazy builders during metadata capture.
    internal static class ContractCollections
    {
        internal static bool TryCopy<T>(IReadOnlyList<T>? list, out ImmutableArray<T> items)
        {
            if (list is T[] array)
            {
                items = ImmutableArray.CreateRange(array);
                return true;
            }
            if (list is List<T> values && list.GetType() == typeof(List<T>))
            {
                items = ImmutableArray.CreateRange(values);
                return true;
            }
            if (list is ImmutableArray<T> immutable && !immutable.IsDefault)
            {
                items = immutable;
                return true;
            }
            items = [];
            return false;
        }

        // Arity-only observations need no element copy. Keep exactly the
        // shared copy profile; do not expand POC-only carriers or call an
        // arbitrary interface Count implementation.
        internal static bool TryGetCount<T>(IReadOnlyList<T>? list, out int count)
        {
            count = list switch
            {
                T[] array => array.Length,
                List<T> values when list.GetType() == typeof(List<T>) => values.Count,
                ImmutableArray<T> immutable when !immutable.IsDefault => immutable.Length,
                _ => -1
            };
            return count >= 0;
        }

        internal static bool TryCopySharedForPoc<T>(IReadOnlyList<T>? list, PocWorkBudget budget, out ImmutableArray<T> items)
        {
            if (!TryGetCount(list, out _)) { items = []; return false; }
            return TryCopyForPoc(list, budget, out items);
        }

        // Reserve the legacy current-carrier copy and reference comparisons
        // without expanding its collection profile or copying anything yet.
        internal static bool ReserveSharedReferencesForPoc<T>(IReadOnlyList<T>? list, int snapshotCount, PocWorkBudget budget) =>
            budget.TrySpend() && TryGetCount(list, out var currentCount) &&
            budget.TrySpend(currentCount) && budget.TrySpend(snapshotCount);

        internal static bool ReserveForPoc<T>(IReadOnlyList<T>? list, PocWorkBudget budget)
        {
            var count = list switch
            {
                T[] array => array.Length,
                ProducerOwnedReadOnlyList<T> owned => owned.CurrentItems.Length,
                List<T> values when list.GetType() == typeof(List<T>) => values.Count,
                ImmutableArray<T> immutable when !immutable.IsDefault => immutable.Length,
                _ => -1
            };
            return budget.TrySpend(count);
        }

        internal static bool IsExactList<T>(IReadOnlyList<T>? list) => list is List<T> && list.GetType() == typeof(List<T>);

        internal static bool TryCopyForPoc<T>(IReadOnlyList<T>? list, PocWorkBudget budget, out ImmutableArray<T> items)
        {
            if (IsExactList(list))
            {
                var values = (List<T>)list!;
                // Bind the enumerator before reading the size and never copy
                // beyond the paid size, even if the live list grows later.
                var enumerator = values.GetEnumerator();
                int count = values.Count;
                if (!budget.TrySpend(count)) { items = []; return false; }
                var builder = ImmutableArray.CreateBuilder<T>(count);
                for (int index = 0; index < count; index++)
                {
                    if (!TryMoveNextExactList(ref enumerator, out var moved) || !moved) { items = []; return false; }
                    builder.Add(enumerator.Current);
                }
                if (!TryMoveNextExactList(ref enumerator, out var extra) || extra) { items = []; return false; }
                items = builder.MoveToImmutable();
                return true;
            }
            if (!ReserveForPoc(list, budget)) { items = []; return false; }
            if (list is ProducerOwnedReadOnlyList<T> owned)
            {
                // The exact sealed producer carrier retains the actual array.
                // No wrapper getter/indexer, witness snapshot or enumeration.
                items = ImmutableArray.CreateRange(owned.CurrentItems);
                return true;
            }
            return TryCopy(list, out items);
        }

        // Exact List<T> uses the BCL enumerator, not a user callback. Contain
        // mutation exceptions only at that operation and publish no partial copy.
        private static bool TryMoveNextExactList<T>(ref List<T>.Enumerator enumerator, out bool moved)
        {
            try { moved = enumerator.MoveNext(); return true; }
            catch (InvalidOperationException) { moved = false; return false; }
            catch (IndexOutOfRangeException) { moved = false; return false; }
        }

        // Capture-only provenance. Do not use this fallback to claim that an
        // opaque carrier's current elements still match its construction values.
        internal static bool TryCopyPocConstruction<T>(IReadOnlyList<T>? list, PocWorkBudget budget, out ImmutableArray<T> items)
        {
            if (TryCopyForPoc(list, budget, out items)) { return true; }
            // An admitted list that changed during capture is not an opaque
            // carrier eligible for a construction-time witness fallback.
            return !IsExactList(list) && !budget.Exhausted && ConstructedCollectionWitness.TryCopyConstruction(list, budget, out items);
        }

        internal static bool SameReferencesForPoc<T>(IReadOnlyList<T>? list, ImmutableArray<T> snapshot, PocWorkBudget budget) where T : class
        {
            if (!TryCopyForPoc(list, budget, out var current) || current.Length != snapshot.Length) { return false; }
            for (int i = 0; i < current.Length; i++) { if (!ReferenceEquals(current[i], snapshot[i])) { return false; } }
            return true;
        }

        // Preserve the shared carrier profile, but avoid an intermediate copy
        // for exact array/immutable-array POC checks. This reads live elements;
        // it does not treat the original collection as a freshness witness.
        internal static bool SameSharedReferencesForPoc<T>(IReadOnlyList<T>? list, ImmutableArray<T> snapshot, PocWorkBudget budget) where T : class
        {
            // Match the existing call's evaluation order for a default snapshot.
            int expectedCount = snapshot.Length;
            if (list is T[] array && array.GetType() == typeof(T[]))
            { return SameReferenceSpanForPoc(array.AsSpan(), snapshot, expectedCount, budget); }
            if (list is ImmutableArray<T> immutable && !immutable.IsDefault)
            { return SameReferenceSpanForPoc(immutable.AsSpan(), snapshot, expectedCount, budget); }
            return ReserveSharedReferencesForPoc(list, expectedCount, budget) && SameReferences(list, snapshot);
        }

        private static bool SameReferenceSpanForPoc<T>(System.ReadOnlySpan<T> current, ImmutableArray<T> snapshot,
            int expectedCount, PocWorkBudget budget) where T : class
        {
            if (!budget.TrySpend()) { return false; }
            if (current.Length != expectedCount)
            {
                // Preserve the original underfunded-mismatch boundary: no
                // mismatch is reported as observed until both counts are paid.
                _ = budget.TrySpend(current.Length) && budget.TrySpend(expectedCount);
                return false;
            }
            if (!budget.TrySpend(current.Length)) { return false; }
            for (int index = 0; index < current.Length; index++)
            {
                if (!ReferenceEquals(current[index], snapshot[index])) { return false; }
            }
            return true;
        }

        internal static bool SameReferences<T>(IReadOnlyList<T>? list, ImmutableArray<T> snapshot) where T : class =>
            TryCopy(list, out var current) && current.Length == snapshot.Length &&
            current.Select((item, index) => ReferenceEquals(item, snapshot[index])).All(same => same);
    }
}
