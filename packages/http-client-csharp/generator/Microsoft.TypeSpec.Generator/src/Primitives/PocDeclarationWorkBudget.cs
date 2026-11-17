// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Threading;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // One generation-wide, fail-closed ledger for the added declaration
    // observations. Bounded CAS retries avoid relying on writer serialization.
    internal sealed class PocDeclarationWorkBudget
    {
        private int _remaining = 262144;
        private int _exhausted;

        internal bool TrySpendReferenceLookup(int count)
        {
            if (count < 0 || count == int.MaxValue) { Interlocked.Exchange(ref _exhausted, 1); return false; }
            var cost = count + 1;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                if (Volatile.Read(ref _exhausted) != 0) { return false; }
                var remaining = Volatile.Read(ref _remaining);
                if (cost > remaining) { Interlocked.Exchange(ref _exhausted, 1); return false; }
                if (Interlocked.CompareExchange(ref _remaining, remaining - cost, remaining) == remaining)
                { return Volatile.Read(ref _exhausted) == 0; }
            }
            Interlocked.Exchange(ref _exhausted, 1);
            return false;
        }
    }
}
