// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Shared only by one additional POC observation. Units bound visited
    // nodes, collection elements and input characters, not exact CPU steps;
    // fixed-count validation passes may each inspect already-charged input.
    // Exhaustion is a veto, never an unbounded or user-provided traversal.
    // A linked child enforces its own cap while charging the enclosing
    // operation once. Budgets contain counters only, never source owners.
    internal sealed class PocWorkBudget(int remaining, PocWorkBudget? parent = null)
    {
        internal int Remaining { get; private set; } = remaining;
        internal bool Exhausted { get; private set; }

        internal bool TrySpend(int count = 1)
        {
            if (count < 0 || Exhausted) { return false; }
            if (count > Remaining) { Exhausted = true; return false; }
            if (parent is not null && !parent.TrySpend(count)) { Exhausted = true; return false; }
            Remaining -= count;
            return true;
        }

        // Identity equality is callback-free, not constant-time. Fund the
        // complete possible native bucket walk before each dictionary/set probe.
        internal bool TrySpendReferenceLookup(int count)
        {
            long work = count + 1L;
            return count >= 0 && work <= int.MaxValue && TrySpend((int)work);
        }

        internal bool TrySpendStringComparison(string? left, string? right)
        {
            if (ReferenceEquals(left, right) || left is null || right is null || left.Length != right.Length) { return TrySpend(); }
            long work = (long)left.Length + right.Length + 1;
            return work <= int.MaxValue && TrySpend((int)work);
        }

        // A retained capture must not keep an enclosing operation alive after
        // that operation has completed. Existing remaining credits are local.
        internal void DetachParent() => parent = null;

        internal void Reject() => Exhausted = true;
    }
}
