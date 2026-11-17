// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Threading;

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // One unique paid ticket per externally initiated attempt. Never reused as a claim
    // marker: releasing it cannot cause ABA. The ordinary owner ledger must be accessed
    // serially by its caller; only these fully withdrawn tickets may cross threads.
    // Use the existing reservation root, or an ancestor-preflighted child. PocWorkBudget
    // exposes no ancestor preflight: an underfunded linked ancestor would retain that
    // ledger's existing sticky Exhausted behavior. This adapter cannot certify that setup.
    internal sealed class PocFrameWorkReservations
    {
        private int _used;
        private PocFrameWorkReservations(PocNativeFrame frame) => Frame = frame;

        internal static bool TryCreate(PocNativeFrame frame, PocWorkBudget ledger, out PocFrameWorkReservations? reservation)
        {
            reservation = null;
            if (!frame.TryPay(ledger, frame.AttemptCost)) { return false; }
            reservation = new PocFrameWorkReservations(frame);
            return true;
        }

        internal PocNativeFrame Frame { get; }
        internal bool Take() => Interlocked.Exchange(ref _used, 1) == 0;

        // Fixed source-level tariffs for independently initiated Render/Dispose entries.
        // A unit is one scalar/reference operation, branch, known-leaf call or atomic;
        // getters are expanded, and dispatch/return have slack. These are algorithmic
        // upper bounds, NOT CPU/JIT/GC latency claims. No native hook is certified here.
        // Setup includes the fixed guards/withdrawal dispatch before any paid allocation.
        // 128 assumes the stated root-ledger prerequisite; a linked chain needs its own
        // caller-side finite-depth/reservation audit, not an assumed constant lookup.
        internal const int SetupIngressTariff = 128;
        internal const int RenderIngressTariff = 96;
        internal const int DisposeIngressTariff = 8;

        // Paid work units: fixed object operations plus fixed-size record/cell/character
        // visits (each visit has a fixed scalar bound). Arrays pay initialization +
        // immutable copy + cleanup. Reservations are deliberately worst-case, not refunds.
        internal static long SetupCost(int uses, int slots) => 32L + 3L * uses + 3L * slots;
        internal static long SelectionCost(int slots) => 8L + 3L * slots;
        internal const int BaseCost = 8;

        // Per attempt: ticket/claim/view/CodeFile/delegate/cleanup (64), legacy and POC
        // validation/projection (12A+16U), bitmap + projected offsets (4K+3U included),
        // two complete legacy checks (both operands: <=4M character visits), destination
        // initialization + read/write copy (<=3M), cleanup and runtime-copy slack (<=5M).
        // string.Create writes the final string directly: no growable/intermediate buffer.
        // Reserve the full worst case before even creating the ticket.
        internal static long AttemptCost(int legacy, int uses, int slots, int maxLength) =>
            64L + 12L * legacy + 16L * uses + 4L * slots + 12L * maxLength;

        internal static bool Withdraw(PocWorkBudget owner, long cost) =>
            cost >= 0 && cost <= int.MaxValue && !owner.Exhausted && owner.Remaining >= cost && owner.TrySpend((int)cost);
    }
}
