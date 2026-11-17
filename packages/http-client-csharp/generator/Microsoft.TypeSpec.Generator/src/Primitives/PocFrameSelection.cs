// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    internal enum PocFramePhase { Tentative, Final }

    // Immutable owned vector. This is a rendering decision, never binding authority.
    internal sealed class PocFrameSelection
    {
        private readonly bool[] _enabled;
        private PocFrameSelection(PocNativeFrame frame, bool[] enabled) { Frame = frame; _enabled = enabled; }

        internal static bool TryCreate(PocNativeFrame frame, ImmutableArray<bool> enabled, PocWorkBudget ledger, out PocFrameSelection? selection)
        {
            selection = null;
            if (enabled.IsDefault || enabled.Length != frame.SlotCount || !frame.TryPay(ledger, PocFrameWorkReservations.SelectionCost(enabled.Length))) { return false; }
            selection = new PocFrameSelection(frame, enabled.AsSpan().ToArray());
            return true;
        }

        internal PocNativeFrame Frame { get; }
        internal int Count => _enabled.Length;
        internal bool IsEnabled(int slot) => _enabled[slot];
    }
}
