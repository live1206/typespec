// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.TypeSpec.Generator.Primitives
{
    // Retains an independently computed legacy result. The adapter verifies its mechanical
    // origin/bytes before using it, but never recomputes legacy selection to obtain fallback.
    internal sealed class PocFrameSelectionBaseResult
    {
        private PocFrameSelectionBaseResult(PocNativeFrame frame, CodeFile file, bool field, bool body)
        { Frame = frame; File = file; Field = field; Body = body; }

        internal static bool TryCreate(PocNativeFrame frame, CodeFile file, bool field, bool body, PocWorkBudget ledger,
            out PocFrameSelectionBaseResult? result)
        {
            result = null;
            if (!ReferenceEquals(file.Name, frame.Native.Name) || !ReferenceEquals(file.SourceConsumption, frame.Native.SourceConsumption) ||
                !frame.TryPay(ledger, PocFrameWorkReservations.BaseCost)) { return false; }
            result = new PocFrameSelectionBaseResult(frame, file, field, body);
            return true;
        }

        internal PocNativeFrame Frame { get; }
        internal CodeFile File { get; }
        internal bool Field { get; }
        internal bool Body { get; }
    }
}
